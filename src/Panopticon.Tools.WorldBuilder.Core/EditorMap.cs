using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Holds the live in-memory map state for the editor session.
/// Wraps MapFile and tracks dirty state / file path.
/// </summary>
public sealed class EditorMap
{
    public MapFile MapFile { get; private set; }

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>Content of the companion map.ini file; written on save.</summary>
    public string MapIniContent { get; set; } = string.Empty;

    /// <summary>Content of the companion map.str string table file; written on save.</summary>
    public string MapStrContent { get; set; } = string.Empty;

    private readonly UndoRedoStack<IMapEdit> _undoStack = new();

    public UndoRedoStack<IMapEdit> UndoStack => _undoStack;

    private EditorMap(MapFile mapFile, string? filePath)
    {
        MapFile = mapFile;
        FilePath = filePath;
    }

    /// <summary>Creates a new blank map from a freshly-constructed MapFile.</summary>
    public static EditorMap CreateNew(MapFile mapFile)
    {
        return new EditorMap(mapFile, filePath: null)
        {
            IsDirty = true,
        };
    }

    /// <summary>Opens an existing .map file from disk.</summary>
    public static EditorMap Open(MapFile mapFile, string filePath)
    {
        return new EditorMap(mapFile, filePath)
        {
            IsDirty = false,
            MapIniContent = ReadCompanionIfExists(filePath + MapIniExtension),
            MapStrContent = ReadCompanionIfExists(filePath + MapStrExtension),
        };
    }

    /// <summary>Saves the map to disk at the current FilePath.</summary>
    public void Save()
    {
        if (FilePath is null)
        {
            throw new InvalidOperationException("Cannot save without a file path — use SaveAs.");
        }

        using (var stream = File.Create(FilePath))
        {
            MapFile.WriteTo(stream);
        }

        // WI-A5-34: companions are written only when non-empty; the .map stream stays
        // authoritative for map data. SaveAs re-targets them through FilePath; a stale
        // companion at the previous path is deliberately left in place.
        WriteCompanion(FilePath + MapIniExtension, MapIniContent);
        WriteCompanion(FilePath + MapStrExtension, MapStrContent);
        IsDirty = false;
    }

    private const string MapIniExtension = ".ini";
    private const string MapStrExtension = ".str";

    private static string ReadCompanionIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static void WriteCompanion(string path, string content)
    {
        if (content.Length > 0)
        {
            File.WriteAllText(path, content);
        }
    }

    /// <summary>Saves the map to a new path and updates FilePath.</summary>
    public void SaveAs(string filePath)
    {
        FilePath = filePath;
        Save();
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }
}
