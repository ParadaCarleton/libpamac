using System.Text;

namespace Pamac;

public static class VersionInfo
{
    public const string Value = "11.7.4";
    public const string VERSION = Value;
    public static string GetVersion() => Value;
    public static string get_version() => Value;
}

public static class Utils
{
    public static string? GetOsId()
    {
        const string path = "/etc/os-release";
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("ID=", StringComparison.Ordinal)) continue;
            var value = line[3..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            return value;
        }
        return null;
    }

    public static string GetUserAgent()
    {
        var id = GetOsId();
        return id is null ? $"Pamac/{VersionInfo.Value}" : $"Pamac/{VersionInfo.Value}_{id}";
    }

    public static string? get_os_id() => GetOsId();
    public static string get_user_agent() => GetUserAgent();

    public static string? FindExecutable(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static string JoinCommandLine(IEnumerable<string> arguments)
    {
        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (builder.Length > 0) builder.Append(' ');
            if (argument.Length == 0) { builder.Append("\"\""); continue; }
            if (argument.Any(char.IsWhiteSpace) || argument.Any(character => "\"'\\$;|&<>".IndexOf(character) >= 0))
                builder.Append('"').Append(argument.Replace("\\", "\\\\").Replace("\"", "\\\"")) .Append('"');
            else builder.Append(argument);
        }
        return builder.ToString();
    }
}
