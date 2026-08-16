using System;
using System.Collections.Generic;
using Tomlyn.Model;

/// <summary>
/// Построение графа зависимостей одной сущности: наследование, секционные <c>base</c>,
/// ссылки на файлы переменных и связанные инструменты.
///
/// ПОЧЕМУ СЫРОЙ TOML. В собранном каталоге путей <c>base</c> и <c>vars</c> уже нет: они
/// растворены в итоговых значениях. Граф отвечает на вопрос «откуда взялся этот файл»,
/// поэтому строится по исходным текстам с подставленными черновиками.
/// </summary>
public sealed class ContentGraphBuilder
{
    private readonly ContentFileIndex _index;
    private readonly Catalog _catalog;
    private readonly IReadOnlyDictionary<string, string> _texts;

    /// <summary>Ключи уже добавленных узлов: один файл не должен появиться дважды.</summary>
    private readonly HashSet<string> _nodeKeys = new(StringComparer.Ordinal);

    /// <summary>Ключи уже добавленных рёбер вместе с подписью: связей может быть несколько.</summary>
    private readonly HashSet<string> _edgeKeys = new(StringComparer.Ordinal);

    public ContentGraphBuilder(
        ContentFileIndex index, Catalog catalog, IReadOnlyDictionary<string, string> texts)
    {
        _index = index;
        _catalog = catalog;
        _texts = texts;
    }

    /// <summary>Построить граф от указанной сессии.</summary>
    public ContentGraphData Build(OpenEntitySession session)
    {
        if (session == null)
            return null;

        var graph = new ContentGraphData
        {
            ContextId = session.Id,
            RootKey = session.Path,
        };

        AddNode(graph, session.Path, session.Id, session.DisplayName,
            KindOf(session.Kind, session.Id, session.Path));
        AddDependencies(session.Path, session.Path, graph, new HashSet<string>(StringComparer.Ordinal));

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
            AddTools(session, graph);

        return graph;
    }

    /// <summary>Оружие и рабочие инструменты сущности — отдельными узлами со своими цепочками.</summary>
    private void AddTools(OpenEntitySession session, ContentGraphData graph)
    {
        var unit = _catalog?.Unit(session.Id);
        foreach (string toolId in unit?.ToolIds ?? Array.Empty<string>())
        {
            var entry = _index.ById(toolId);
            if (entry == null)
                continue;

            var tool = _catalog?.Tool(toolId);
            var kind = tool is WeaponDefinition
                ? ContentGraphNodeKind.Weapon
                : ContentGraphNodeKind.WorkTool;

            AddNode(graph, entry.Path, toolId, tool?.DisplayName ?? entry.DisplayName, kind);
            AddEdge(graph, session.Path, entry.Path,
                kind == ContentGraphNodeKind.Weapon ? "weapon" : "tool");
            AddDependencies(entry.Path, entry.Path, graph,
                new HashSet<string>(StringComparer.Ordinal));
        }
    }

    /// <summary>Зависимости одного файла: корневой base, секционные base и ссылки на переменные.</summary>
    private void AddDependencies(
        string nodeKey, string path, ContentGraphData graph, HashSet<string> visited)
    {
        if (!visited.Add(path) || !_texts.TryGetValue(path, out string text))
            return;

        var table = TomlText.Parse(text);
        if (table == null)
            return;

        if (table.TryGetValue("base", out object rootBase))
            AddBase(nodeKey, rootBase, "inherits", graph, visited);

        foreach (var pair in table)
        {
            if (pair.Value is TomlTable section && section.TryGetValue("base", out object sectionBase))
                AddBase(nodeKey, sectionBase, $"section [{pair.Key}]", graph, visited);
        }

        AddValueReferences(nodeKey, null, table, graph);
    }

    private void AddBase(
        string from, object rawPath, string label, ContentGraphData graph, HashSet<string> visited)
    {
        if (rawPath is not string text
            || !TomlResolver.TryCanonicalPath(text, out string target, out _)
            || !_texts.ContainsKey(target))
        {
            return;
        }

        AddNodeForPath(graph, target);
        AddEdge(graph, from, target, label);
        AddDependencies(target, target, graph, visited);
    }

    /// <summary>
    /// Ссылки из значений ищутся во всех ключах, включая вложенные секции: и ссылка на файл
    /// переменных вида <c>max_health = "res://…/hull.vars.toml:heavy"</c>, и ссылка на ресурс
    /// движка вида <c>model = "res://…/dox.tscn"</c> может стоять где угодно.
    ///
    /// РЕСУРСЫ ПОКАЗЫВАЮТСЯ НАРАВНЕ С ФАЙЛАМИ СОДЕРЖИМОГО. Граф отвечает на вопрос, из чего
    /// собрана сущность, и спрайт с моделью входят в ответ не меньше, чем предок по
    /// <c>base</c>: без них по графу не видно, чем сущность вообще нарисована. Отсюда же
    /// следует, что отсутствующий файл ресурса узел не отменяет, а помечает, — иначе опечатка
    /// в пути выглядела бы как отсутствие ссылки.
    /// </summary>
    private void AddValueReferences(
        string from, string section, TomlTable table, ContentGraphData graph)
    {
        foreach (var pair in table)
        {
            if (pair.Value is TomlTable nested)
            {
                string nestedSection = string.IsNullOrEmpty(section)
                    ? pair.Key
                    : $"{section}.{pair.Key}";
                AddValueReferences(from, nestedSection, nested, graph);
                continue;
            }

            if (pair.Value is not string value)
                continue;

            string field = string.IsNullOrEmpty(section) ? pair.Key : $"{section}.{pair.Key}";

            if (TomlResolver.TryCanonicalPath(value, out string target, out string property)
                && TomlResolver.IsVars(target)
                && _texts.ContainsKey(target))
            {
                AddNodeForPath(graph, target);
                AddEdge(graph, from, target,
                    string.IsNullOrEmpty(property) ? field : $"{field} → {property}");
                continue;
            }

            AddAssetReference(from, field, value, graph);
        }
    }

    /// <summary>
    /// Ресурс движка, на который ссылается ключ: спрайт, сцена модели, шейдер. Признаком
    /// служит приставка <c>res://</c>; файлы содержимого исключены, поскольку они уже разобраны
    /// выше как предки и переменные.
    /// </summary>
    private void AddAssetReference(
        string from, string field, string value, ContentGraphData graph)
    {
        if (!value.StartsWith("res://", StringComparison.Ordinal)
            || value.EndsWith(".toml", StringComparison.Ordinal))
        {
            return;
        }

        bool exists = Godot.FileAccess.FileExists(value);
        string extension = System.IO.Path.GetExtension(value).TrimStart('.');

        AddNode(graph, value, exists ? extension : "нет файла", null,
            ContentGraphNodeKind.Asset);
        AddEdge(graph, from, value, field);
    }

    private void AddNodeForPath(ContentGraphData graph, string path)
    {
        var entry = _index.ByPath(path);
        string id = entry?.Id ?? TomlText.FileName(path);
        AddNode(graph, path, id, entry?.DisplayName ?? TomlText.FileName(path),
            KindOf(entry?.Kind, id, path, entry?.Abstract == true));
    }

    private void AddNode(
        ContentGraphData graph, string key, string id, string title, ContentGraphNodeKind kind)
    {
        if (!_nodeKeys.Add(key))
            return;

        graph.Nodes.Add(new ContentGraphNode
        {
            Key = key,
            Title = TomlText.FileName(key),
            Detail = string.IsNullOrEmpty(title) || string.Equals(title, id, StringComparison.Ordinal)
                ? id
                : $"{id} · {title}",
            Path = key,
            Kind = kind,
        });
    }

    private void AddEdge(ContentGraphData graph, string from, string to, string label)
    {
        if (!_edgeKeys.Add($"{from}\n{to}\n{label}"))
            return;

        graph.Edges.Add(new ContentGraphEdge { From = from, To = to, Label = label });
    }

    /// <summary>Вид узла: файл переменных и шаблон различаются по цвету от обычных записей.</summary>
    private ContentGraphNodeKind KindOf(
        ContentEntityKind? kind, string id, string path = null, bool abstractEntry = false)
    {
        if (!string.IsNullOrEmpty(path) && TomlResolver.IsVars(path))
            return ContentGraphNodeKind.Vars;
        if (abstractEntry)
            return ContentGraphNodeKind.Base;

        return kind switch
        {
            ContentEntityKind.Building => ContentGraphNodeKind.Building,
            ContentEntityKind.Weapon => ContentGraphNodeKind.Weapon,
            ContentEntityKind.WorkTool => ContentGraphNodeKind.WorkTool,
            ContentEntityKind.Unit => ContentGraphNodeKind.Unit,
            _ when _catalog?.Tool(id) is WeaponDefinition => ContentGraphNodeKind.Weapon,
            _ when _catalog?.Tool(id) is WorkToolDefinition => ContentGraphNodeKind.WorkTool,
            _ => ContentGraphNodeKind.Base,
        };
    }
}
