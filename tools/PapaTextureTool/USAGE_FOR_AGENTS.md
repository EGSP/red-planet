# PapaTextureTool — инструкция для агентов

Консольная утилита для конвертации текстур `.papa` в `.png` с сохранением альфа-канала.

## Быстрый запуск

1. Собрать утилиту:

```powershell
dotnet build "c:\workroot\Разработка\Программы\csharp\red-planet\tools\PapaTextureTool\PapaTextureTool.csproj"
```

2. Запустить конвертацию:

```powershell
dotnet run --project "c:\workroot\Разработка\Программы\csharp\red-planet\tools\PapaTextureTool\PapaTextureTool.csproj" -- "<input1>" [<input2> ...] -o "<output_dir>"
```

## Что можно передавать во входы

- отдельные файлы `.papa`;
- директории (поиск `.papa` выполняется рекурсивно).

## Рекомендуемый запуск для биомов и декалей

Чтобы минимизировать ошибки по не-текстурным `.papa`, передавайте в утилиту в первую очередь каталоги `textures`:

```powershell
dotnet run --project "c:\workroot\Разработка\Программы\csharp\red-planet\tools\PapaTextureTool\PapaTextureTool.csproj" -- `
  "c:\workroot\quarantine\downloads\torrent\Planetary.Annihilation.TITANS.v2026.07.02\Planetary Annihilation\media\pa\terrain\desert\textures" `
  "c:\workroot\quarantine\downloads\torrent\Planetary.Annihilation.TITANS.v2026.07.02\Planetary Annihilation\media\pa\terrain\grass\textures" `
  "c:\workroot\quarantine\downloads\torrent\Planetary.Annihilation.TITANS.v2026.07.02\Planetary Annihilation\media\pa\terrain\ice\textures" `
  -o "c:\workroot\quarantine\downloads\torrent\Planetary.Annihilation.TITANS.v2026.07.02\converted_asssets"
```

При необходимости можно передавать и весь `terrain`, но тогда в выводе будут ошибки для модельных `.papa` из `fbx`.

## Формат вывода

- для каждого входного каталога сохраняется относительная структура;
- для каждого `.papa` создается одноименный `.png`;
- утилита пишет итоговую строку вида:
  - `Done. Converted: N, Skipped: K, Failed: M`.

## Коды завершения

- `0` — ошибок нет;
- `2` — были ошибки обработки хотя бы одного файла.

