#!/usr/bin/env python3
"""Преобразование отчёта партии (JSONL) в книгу Excel с диаграммами.

Отчёт пишет MetricsSystem по ходу партии в user://metrics. Каждая строка файла —
самостоятельный объект JSON: header, sample, event или footer. Здесь всё это
раскладывается в лист данных, лист событий и лист диаграмм.

Запуск:

    python tools/metrics_to_xlsx.py                     # последний отчёт
    python tools/metrics_to_xlsx.py путь/к/report.jsonl
    python tools/metrics_to_xlsx.py report.jsonl -o книга.xlsx

Требуется openpyxl:  pip install openpyxl
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

try:
    from openpyxl import Workbook
    from openpyxl.chart import AreaChart, LineChart, Reference
    from openpyxl.utils import get_column_letter
except ImportError:  # pragma: no cover
    sys.exit("Нужен openpyxl: pip install openpyxl")


PROJECT_NAME = "red_planet"


def user_metrics_dir() -> Path | None:
    """Каталог user://metrics для текущей операционной системы."""

    if sys.platform.startswith("win"):
        appdata = os.environ.get("APPDATA")
        if not appdata:
            return None
        base = Path(appdata) / "Godot" / "app_userdata" / PROJECT_NAME
    elif sys.platform == "darwin":
        base = Path.home() / "Library" / "Application Support" / "Godot" / "app_userdata" / PROJECT_NAME
    else:
        base = Path.home() / ".local" / "share" / "godot" / "app_userdata" / PROJECT_NAME

    metrics = base / "metrics"
    return metrics if metrics.is_dir() else None


def latest_report() -> Path:
    directory = user_metrics_dir()
    if directory is None:
        sys.exit("Каталог отчётов не найден — укажите путь к файлу явно")

    reports = sorted(directory.glob("session-*.jsonl"), key=lambda p: p.stat().st_mtime)
    if not reports:
        sys.exit(f"В {directory} нет отчётов")

    return reports[-1]


def read_report(path: Path) -> tuple[dict, list[dict], list[dict], dict]:
    """Разбор файла. Повреждённая последняя строка отбрасывается молча.

    Аварийное завершение игры обрывает запись на середине строки, и терять из-за
    этого весь отчёт было бы неверно: предыдущие замеры целы.
    """

    header: dict = {}
    samples: list[dict] = []
    events: list[dict] = []
    footer: dict = {}

    with path.open(encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue

            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                continue

            kind = record.get("type")
            if kind == "header":
                header = record
            elif kind == "sample":
                samples.append(record)
            elif kind == "event":
                events.append(record)
            elif kind == "footer":
                footer = record

    if not header:
        sys.exit(f"{path}: нет строки заголовка, файл не является отчётом партии")

    return header, samples, events, footer


def write_data_sheet(book: Workbook, header: dict, samples: list[dict]):
    sheet = book.active
    sheet.title = "Данные"

    channels = header.get("channels", [])

    sheet.append(["Время, мин"] + [c.get("caption", c["id"]) for c in channels])
    sheet.append([""] + [c["id"] for c in channels])

    for sample in samples:
        values = sample.get("v", [])
        row = [round(sample.get("t", 0.0) / 60.0, 4)]
        row.extend(values[i] if i < len(values) else None for i in range(len(channels)))
        sheet.append(row)

    sheet.freeze_panes = "B3"

    for index in range(1, len(channels) + 2):
        sheet.column_dimensions[get_column_letter(index)].width = 14

    return sheet


def write_events_sheet(book: Workbook, events: list[dict]):
    if not events:
        return

    sheet = book.create_sheet("События")
    sheet.append(["Время, мин", "Вид", "Волна", "Террор", "Бюджет", "Потрачено", "Состав"])

    for event in events:
        sheet.append([
            round(event.get("t", 0.0) / 60.0, 4),
            event.get("kind", ""),
            event.get("wave", ""),
            event.get("terror"),
            event.get("budget"),
            event.get("spent"),
            event.get("composition", ""),
        ])

    for index, width in enumerate((12, 10, 18, 10, 10, 12, 60), start=1):
        sheet.column_dimensions[get_column_letter(index)].width = width


def write_charts_sheet(book: Workbook, header: dict, data, rows: int):
    """Диаграммы по раскладке из заголовка отчёта.

    Стек рисуется областями с накоплением, прочее — линиями. Канал overlay
    добавляется отдельной линейной диаграммой поверх той же области построения:
    сумма слагаемых и сглаженная кривая отвечают на разные вопросы.
    """

    if rows < 2:
        return

    sheet = book.create_sheet("Графики")
    columns = {c["id"]: index for index, c in enumerate(header.get("channels", []), start=2)}

    time_axis = Reference(data, min_col=1, min_row=3, max_row=rows + 2)
    row_at = 1

    for plot in header.get("plots", []):
        ids = [i for i in plot.get("channels", []) if i in columns]
        if not ids:
            continue

        stacked = plot.get("mode") == "stack"
        chart = AreaChart() if stacked else LineChart()
        chart.title = plot.get("title", "")
        chart.height = 8
        chart.width = 18
        chart.x_axis.title = "Время, мин"

        if stacked:
            chart.grouping = "stacked"
            chart.overlap = 100

        for channel_id in ids:
            column = columns[channel_id]
            series = Reference(data, min_col=column, min_row=1, max_row=rows + 2)
            chart.add_data(series, titles_from_data=True)

        chart.set_categories(time_axis)

        for series in chart.series:
            series.smooth = False

        overlay = plot.get("overlay")
        if overlay in columns:
            line = LineChart()
            column = columns[overlay]
            line.add_data(
                Reference(data, min_col=column, min_row=1, max_row=rows + 2),
                titles_from_data=True,
            )
            for series in line.series:
                series.smooth = False
            chart += line

        sheet.add_chart(chart, f"A{row_at}")
        row_at += 16


def convert(source: Path, target: Path):
    header, samples, events, footer = read_report(source)

    book = Workbook()
    data = write_data_sheet(book, header, samples)
    write_events_sheet(book, events)
    write_charts_sheet(book, header, data, len(samples))

    book.save(target)

    minutes = footer.get("seconds", samples[-1]["t"] if samples else 0.0) / 60.0
    outcome = footer.get("outcome", "не записан")
    print(f"{source.name}: замеров {len(samples)}, событий {len(events)}, "
          f"длительность {minutes:.1f} мин, исход {outcome}")
    print(f"готово: {target}")


def main():
    parser = argparse.ArgumentParser(description="Отчёт партии JSONL → книга Excel с диаграммами")
    parser.add_argument("report", nargs="?", help="файл отчёта; без него берётся последний")
    parser.add_argument("-o", "--output", help="куда сохранить книгу")
    args = parser.parse_args()

    source = Path(args.report) if args.report else latest_report()

    if not source.is_file():
        sys.exit(f"Файл не найден: {source}")

    target = Path(args.output) if args.output else source.with_suffix(".xlsx")
    convert(source, target)


if __name__ == "__main__":
    main()
