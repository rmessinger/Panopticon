namespace Panopticon.FileFormats.Casc;

public sealed class CascBuildInfo
{
    public CascBuildInfo(
        CascKey rootContentKey,
        CascKey encodingContentKey,
        CascKey encodingEncodingKey,
        string buildUid)
    {
        RootContentKey = rootContentKey;
        EncodingContentKey = encodingContentKey;
        EncodingEncodingKey = encodingEncodingKey;
        BuildUid = buildUid;
    }

    public CascKey RootContentKey { get; }

    public CascKey EncodingContentKey { get; }

    public CascKey EncodingEncodingKey { get; }

    public string BuildUid { get; }

    public static CascBuildInfo Read(string installRoot)
    {
        var buildInfoPath = Path.Combine(installRoot, ".build.info");
        if (!File.Exists(buildInfoPath))
        {
            throw new FileNotFoundException("CASC build info was not found.", buildInfoPath);
        }

        var rows = ReadBuildInfoRows(buildInfoPath);
        var activeRow = rows.FirstOrDefault(r => r.TryGetValue("Active", out var active) && active == "1");
        if (activeRow is null)
        {
            throw new FileNotFoundException("CASC build info has no active row.", buildInfoPath);
        }

        if (!activeRow.TryGetValue("Build Key", out var buildKey) || string.IsNullOrWhiteSpace(buildKey))
        {
            throw new FileNotFoundException("CASC build info active row has no build key.", buildInfoPath);
        }

        var configPath = Path.Combine(
            installRoot,
            "SC2Data",
            "config",
            buildKey[..2],
            buildKey[2..4],
            buildKey);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("CASC build config was not found.", configPath);
        }

        var config = ReadConfig(configPath);
        var root = ReadConfigValues(config, "root", configPath);
        var encoding = ReadConfigValues(config, "encoding", configPath);
        if (encoding.Length < 2)
        {
            throw new InvalidDataException($"CASC build config '{configPath}' has an invalid encoding row.");
        }

        var buildUid = ReadConfigValues(config, "build-uid", configPath);
        return new CascBuildInfo(
            CascKey.Parse(root[0]),
            CascKey.Parse(encoding[0]),
            CascKey.Parse(encoding[1]),
            buildUid[0]);
    }

    private static List<Dictionary<string, string>> ReadBuildInfoRows(string path)
    {
        var lines = File.ReadAllLines(path)
            .Where(static l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        if (lines.Length == 0)
        {
            throw new InvalidDataException($"CASC build info '{path}' is empty.");
        }

        var columns = lines[0]
            .Split('|')
            .Select(static c => c.Split('!')[0])
            .ToArray();

        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split('|');
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var columnIndex = 0; columnIndex < columns.Length && columnIndex < values.Length; columnIndex++)
            {
                row[columns[columnIndex]] = values[columnIndex];
            }

            rows.Add(row);
        }

        return rows;
    }

    private static Dictionary<string, string[]> ReadConfig(string path)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            result[key] = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return result;
    }

    private static string[] ReadConfigValues(Dictionary<string, string[]> config, string key, string path)
    {
        if (!config.TryGetValue(key, out var values) || values.Length == 0)
        {
            throw new InvalidDataException($"CASC build config '{path}' is missing '{key}'.");
        }

        return values;
    }
}
