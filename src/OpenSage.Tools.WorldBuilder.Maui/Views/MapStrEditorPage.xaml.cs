using System.Collections.ObjectModel;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public sealed class StringEntry
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public partial class MapStrEditorPage : ContentPage
{
    private readonly EditorMap? _map;
    private readonly ObservableCollection<StringEntry> _entries = [];

    public MapStrEditorPage(EditorMap? map)
    {
        InitializeComponent();
        _map = map;
        StringsView.ItemsSource = _entries;
        LoadEntries();
    }

    private void LoadEntries()
    {
        _entries.Clear();

        // Parse map.str content: format is KEY "value" END
        var content = _map?.MapStrContent ?? string.Empty;
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? pendingKey = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                pendingKey = null;
                continue;
            }

            if (pendingKey is not null && line.StartsWith('"'))
            {
                var value = line.Trim('"');
                _entries.Add(new StringEntry { Key = pendingKey, Value = value });
                pendingKey = null;
                continue;
            }

            if (!line.StartsWith("//") && !line.StartsWith(';') && line.Length > 0)
            {
                pendingKey = line;
            }
        }
    }

    private void OnAddEntry(object? sender, EventArgs e)
    {
        var key = NewKeyEntry.Text?.Trim();
        var value = NewValueEntry.Text?.Trim();

        if (string.IsNullOrEmpty(key) || value is null)
        {
            return;
        }

        var existing = _entries.FirstOrDefault(en => en.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            _entries.Add(new StringEntry { Key = key, Value = value });
        }

        NewKeyEntry.Text = string.Empty;
        NewValueEntry.Text = string.Empty;
    }

    private void OnDeleteEntry(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string key)
        {
            var toRemove = _entries.FirstOrDefault(en => en.Key == key);
            if (toRemove is not null)
            {
                _entries.Remove(toRemove);
            }
        }
    }

    private async void OnCancel(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private async void OnApply(object? sender, EventArgs e)
    {
        if (_map is not null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var entry in _entries)
            {
                sb.AppendLine(entry.Key);
                sb.AppendLine($"\"{entry.Value}\"");
                sb.AppendLine("END");
                sb.AppendLine();
            }

            _map.MapStrContent = sb.ToString();
            _map.MarkDirty();
        }

        await Navigation.PopModalAsync();
    }
}
