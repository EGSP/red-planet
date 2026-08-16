using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Закрываемый набор контекстных панелей. Контекст задаётся внешним идентификатором:
/// при его смене все страницы удаляются, поэтому содержимое предыдущей сущности
/// невозможно показать для новой.
/// </summary>
[Tool]
public partial class EditorContextTabs : PanelContainer
{
    private readonly TabBar _tabs;
    private readonly Label _caption;
    private readonly PanelContainer _frame;
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private string _contextId;

    public EditorContextTabs()
    {
        CustomMinimumSize = new Vector2(0, 210);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        // Собственная рамка отделяет контекстные страницы от измерительного поля над ними:
        // без неё обе области читались как одна поверхность.
        AddThemeStyleboxOverride("panel", ContentEditorTheme.PanelStyle());

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(column);

        // Страницы принадлежат одной сущности, и без подписи их содержимое читалось
        // как относящееся к редактору целиком.
        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(header);

        _caption = new Label
        {
            Text = "",
            Modulate = new Color(1f, 1f, 1f, 0.55f),
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(120, 0),
        };
        _caption.AddThemeFontSizeOverride("font_size", 11);
        header.AddChild(_caption);

        _tabs = new TabBar
        {
            TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways,
            ScrollingEnabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _tabs.TabChanged += ShowTab;
        _tabs.TabClosePressed += CloseTab;
        header.AddChild(_tabs);

        _frame = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(_frame);
        Visible = false;
    }

    public string ContextId => _contextId;
    public IEnumerable<KeyValuePair<string, Control>> Pages => _pages;

    public void SetContext(string contextId, bool preservePages = false)
    {
        if (string.Equals(_contextId, contextId, StringComparison.Ordinal))
            return;

        _contextId = contextId;
        _caption.Text = string.IsNullOrEmpty(contextId) ? "" : $"context: {contextId}";
        _caption.TooltipText = _caption.Text;
        if (!preservePages)
            ClearPages();
    }

    public Control Page(string key) =>
        key != null && _pages.TryGetValue(key, out var page) ? page : null;

    public bool Select(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
            return false;

        SelectPage(page);
        return true;
    }

    public void Open(string key, string title, Control page)
    {
        if (string.IsNullOrEmpty(key) || page == null)
            return;

        if (_pages.TryGetValue(key, out var existing))
        {
            SelectPage(existing);
            page.QueueFree();
            return;
        }

        page.Visible = false;
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.SizeFlagsVertical = SizeFlags.ExpandFill;
        _frame.AddChild(page);
        _pages[key] = page;

        int index = _tabs.TabCount;
        _tabs.AddTab(title);
        _tabs.SetTabMetadata(index, key);
        _tabs.CurrentTab = index;
        Visible = true;
        ShowTab(index);
    }

    public void ClearPages()
    {
        foreach (var page in _pages.Values)
        {
            _frame.RemoveChild(page);
            page.QueueFree();
        }

        _pages.Clear();
        _tabs.ClearTabs();
        Visible = false;
    }

    public bool Close(string key)
    {
        for (int i = 0; i < _tabs.TabCount; i++)
        {
            if (_tabs.GetTabMetadata(i).AsString() != key)
                continue;

            CloseTab(i);
            return true;
        }

        return false;
    }

    public void RevealPages()
    {
        Visible = _pages.Count > 0;
    }

    private void SelectPage(Control page)
    {
        for (int i = 0; i < _tabs.TabCount; i++)
        {
            string key = _tabs.GetTabMetadata(i).AsString();
            if (_pages.TryGetValue(key, out var candidate) && candidate == page)
            {
                _tabs.CurrentTab = i;
                ShowTab(i);
                return;
            }
        }
    }

    private void ShowTab(long index)
    {
        if (index < 0 || index >= _tabs.TabCount)
            return;

        string selected = _tabs.GetTabMetadata((int)index).AsString();
        foreach (var pair in _pages)
            pair.Value.Visible = pair.Key == selected;
    }

    private void CloseTab(long index)
    {
        if (index < 0 || index >= _tabs.TabCount)
            return;

        string key = _tabs.GetTabMetadata((int)index).AsString();
        if (_pages.Remove(key, out var page))
        {
            _frame.RemoveChild(page);
            page.QueueFree();
        }

        _tabs.RemoveTab((int)index);
        if (_tabs.TabCount == 0)
        {
            Visible = false;
            return;
        }

        int next = Mathf.Clamp((int)index, 0, _tabs.TabCount - 1);
        _tabs.CurrentTab = next;
        ShowTab(next);
    }
}
