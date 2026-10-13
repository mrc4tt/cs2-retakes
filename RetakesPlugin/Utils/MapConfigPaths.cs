namespace RetakesPlugin.Utils;

/// <summary>
/// Map configs (spawns) come from two places:
///   - shipped:  plugins/RetakesPlugin/map_config/&lt;name&gt;.json, part of the release and replaced on every update.
///   - custom:   configs/plugins/RetakesPlugin/map_config/&lt;name&gt;.json, written by the spawn editor and never
///               touched by an update.
/// A custom file wins over the shipped file with the same name. Edits always go to the custom directory,
/// so a server's own spawns survive plugin updates while untouched maps keep getting the shipped spawns.
/// </summary>
public static class MapConfigPaths
{
    private const string DirectoryName = "map_config";
    private const string PluginName = "RetakesPlugin";

    public static string ShippedDirectory(string moduleDirectory) => Path.Combine(moduleDirectory, DirectoryName);

    public static string CustomDirectory(string moduleDirectory)
    {
        // Walk up to the counterstrikesharp root instead of assuming plugins/RetakesPlugin, so this also
        // works from plugins/disabled/RetakesPlugin.
        var dir = new DirectoryInfo(Path.GetFullPath(moduleDirectory));
        while (dir is not null && !string.Equals(dir.Name, "counterstrikesharp", StringComparison.OrdinalIgnoreCase))
        {
            dir = dir.Parent;
        }

        var cssRoot = dir?.FullName ?? Path.GetFullPath(Path.Combine(moduleDirectory, "..", ".."));
        return Path.Combine(cssRoot, "configs", "plugins", PluginName, DirectoryName);
    }

    public static string CustomPath(string moduleDirectory, string name) =>
        Path.Combine(CustomDirectory(moduleDirectory), $"{name}.json");

    /// <summary>The file to read for a map config: the custom one if present, otherwise the shipped one.</summary>
    public static string ResolveReadPath(string moduleDirectory, string name)
    {
        var customPath = CustomPath(moduleDirectory, name);
        return File.Exists(customPath)
            ? customPath
            : Path.Combine(ShippedDirectory(moduleDirectory), $"{name}.json");
    }

    public static bool Exists(string moduleDirectory, string name) =>
        File.Exists(ResolveReadPath(moduleDirectory, name));

    /// <summary>All map config names from both directories, sorted, with "(custom)" for server-edited ones.</summary>
    public static List<(string Name, bool IsCustom)> List(string moduleDirectory)
    {
        static IEnumerable<string> Names(string directory) =>
            Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                : [];

        var custom = Names(CustomDirectory(moduleDirectory)).ToHashSet();
        return Names(ShippedDirectory(moduleDirectory))
            .Concat(custom)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(name => (name, custom.Contains(name)))
            .ToList();
    }
}
