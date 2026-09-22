using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI map string-table editor page.</summary>
public sealed class MapStrEditorWindow : WbDialog
{
    public sealed record StringEntry(string Key, string Value);

    private readonly EditorMap? _map;
    private readonly ObservableCollection<StringEntry> _entries = [];
    private readonly TextBox _newKey;
    private readonly TextBox _newValue;

    public MapStrEditorWindow(EditorMap? map)
        : base("Map String Table", 720, 560)
    {
        _map = map;
        _newKey = Entry();
        _newValue = Entry();
        LoadEntries();

        var add = SecondaryButton("Add / Update");
        add.Click += OnAddEntry;

        var cancel = SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close();
        var apply = PrimaryButton("Apply");
        apply.Click += OnApply;

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    Margin = new Thickness(24, 24, 24, 8),
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            Heading("Map Strings"),
                            new ItemsControl
                            {
                                ItemsSource = _entries,
                                ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>((_, _) => new EntryRow(this)),
                            },
                        },
                    },
                },
            },
        };

        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(24, 8, 24, 8),
            Children =
            {
                _newKey,
                _newValue,
                add,
            },
        };
        Grid.SetRow(addRow, 1);
        ((Grid)Content).Children.Add(addRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 8, 24, 24),
            Children = { cancel, apply },
        };
        Grid.SetRow(buttons, 2);
        ((Grid)Content).Children.Add(buttons);
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

            if (line.Equals("END", System.StringComparison.OrdinalIgnoreCase))
            {
                pendingKey = null;
                continue;
            }

            if (pendingKey is not null && line.StartsWith('"'))
            {
                var value = line.Trim('"');
                _entries.Add(new StringEntry(pendingKey, value));
                pendingKey = null;
                continue;
            }

            if (!line.StartsWith("//") && !line.StartsWith(';') && line.Length > 0)
            {
                pendingKey = line;
            }
        }
    }

    private void OnAddEntry(object? sender, System.EventArgs e)
    {
        var key = _newKey.Text?.Trim();
        var value = _newValue.Text?.Trim();

        if (string.IsNullOrEmpty(key) || value is null)
        {
            return;
        }

        var existing = _entries.FirstOrDefault(en => en.Key == key);
        if (existing is not null)
        {
            var index = _entries.IndexOf(existing);
            _entries[index] = new StringEntry(key, value);
        }
        else
        {
            _entries.Add(new StringEntry(key, value));
        }

        _newKey.Text = string.Empty;
        _newValue.Text = string.Empty;
    }

    internal void DeleteEntry(string key)
    {
        var toRemove = _entries.FirstOrDefault(en => en.Key == key);
        if (toRemove is not null)
        {
            _entries.Remove(toRemove);
        }
    }

    private void OnApply(object? sender, System.EventArgs e)
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

        Close();
    }

    private sealed class EntryRow : StackPanel
    {
        public EntryRow(MapStrEditorWindow owner)
        {
            Orientation = Orientation.Horizontal;
            Spacing = 8;

            var key = new TextBlock { Width = 220, Foreground = new SolidColorBrush(WbTheme.Text) };
            var value = new TextBlock
            {
                Width = 300,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(WbTheme.Text),
            };
            var delete = new Button { Content = "Delete" };
            key.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Key"));
            value.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Value"));
            delete.Click += (_, _) =>
            {
                if (key.Text is { } text)
                {
                    owner.DeleteEntry(text);
                }
            };

            Children.Add(key);
            Children.Add(value);
            Children.Add(delete);
        }
    }
}
