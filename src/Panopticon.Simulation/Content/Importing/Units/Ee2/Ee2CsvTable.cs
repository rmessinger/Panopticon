#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// EE2 tech-tree CSV such as <c>upgrade_unittypes.csv</c>: <c>//</c> lines are comments, the last
/// comment line before data names the columns, and quotes may be doubled. Rows are keyed by the
/// first column.
/// </summary>
internal sealed class Ee2CsvTable
{
    private readonly Dictionary<string, (string[] Cells, int Line)> _rows = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Columns { get; }

    public string File { get; }

    private Ee2CsvTable(string file, IReadOnlyList<string> columns)
    {
        File = file;
        Columns = columns;
    }

    public static Ee2CsvTable Read(string text, string file)
    {
        string[] columns = [];
        Ee2CsvTable? table = null;
        var lineNumber = 0;

        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                if (table is null)
                {
                    columns = SplitRow(trimmed[2..]).Select(c => c.Trim()).ToArray();
                }

                continue;
            }

            table ??= new Ee2CsvTable(file, columns);
            var cells = SplitRow(trimmed);
            if (!table._rows.TryAdd(cells[0], (cells, lineNumber)))
            {
                throw new InvalidDataException($"{file}:{lineNumber}: duplicate row '{cells[0]}'.");
            }
        }

        return table ?? new Ee2CsvTable(file, columns);
    }

    public bool TryGetRow(string key, out IReadOnlyDictionary<string, string> row, out int line)
    {
        if (!_rows.TryGetValue(key, out var entry))
        {
            row = new Dictionary<string, string>();
            line = 0;
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Columns.Count && i < entry.Cells.Length; i++)
        {
            values.TryAdd(Columns[i], entry.Cells[i]);
        }

        row = values;
        line = entry.Line;
        return true;
    }

    private static string[] SplitRow(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells.ToArray();
    }
}
