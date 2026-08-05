using SigLevel = global::Pamac.SignatureLevel;
using System.Runtime.InteropServices;
using System.Text;

namespace Pamac;

[Flags]
public enum SignatureLevel
{
    None = 0,
    Package = 1 << 0,
    PackageOptional = 1 << 1,
    Database = 1 << 2,
    DatabaseOptional = 1 << 3,
    PackageMarginalOk = 1 << 4,
    PackageUnknownOk = 1 << 5,
    DatabaseMarginalOk = 1 << 6,
    DatabaseUnknownOk = 1 << 7,
    UseDefault = 1 << 30
}

[Flags]
public enum DatabaseUsage
{
    None = 0,
    Sync = 1 << 0,
    Search = 1 << 1,
    Install = 1 << 2,
    Upgrade = 1 << 3,
    All = Sync | Search | Install | Upgrade
}

/// <summary>A pacman repository as represented by pacman.conf.</summary>
public sealed class AlpmRepo
{
    public AlpmRepo(string name)
    {
        Name = name;
        SignatureLevel = SigLevel.UseDefault;
        Urls = new List<string>();
    }

    public string Name { get; }
    public SigLevel SignatureLevel { get; set; }
    public SigLevel SignatureLevelMask { get; set; }
    public DatabaseUsage Usage { get; set; }
    public IList<string> Urls { get; }

    public static bool EqualName(AlpmRepo left, AlpmRepo right)
        => string.Equals(left.Name, right.Name, StringComparison.Ordinal);
}

/// <summary>
/// Managed equivalent of libalpm's configuration object. It deliberately does
/// not expose a native handle: the C# port uses pacman for operations, while
/// retaining the same configuration semantics and paths.
/// </summary>
public sealed class AlpmConfig
{
    private readonly object sync = new();
    private string? rootDirectory;
    private string? logFile;
    private string? gpgDirectory;
    private readonly List<AlpmRepo> repositories = new();

    public AlpmConfig(string path = "/etc/pacman.conf")
    {
        ConfigurationPath = path;
        Reload();
    }

    public string ConfigurationPath { get; }
    public string? RootDirectory => rootDirectory;
    public string? DbPath { get; private set; }
    public string? LogFile => logFile;
    public string? GpgDirectory => gpgDirectory;
    public string DownloadUser { get; private set; } = "alpm";
    public bool DisableSandbox { get; private set; }
    public int UseSysLog { get; private set; }
    public bool CheckSpace { get; set; }
    public IList<string> Architectures { get; } = new List<string>();
    public IList<string> CacheDirectories { get; } = new List<string>();
    public IList<string> HookDirectories { get; } = new List<string>();
    public IList<string> IgnoreGroups { get; } = new List<string>();
    public ISet<string> IgnorePackages { get; } = new HashSet<string>(StringComparer.Ordinal);
    public IList<string> NoExtracts { get; } = new List<string>();
    public IList<string> NoUpgrades { get; } = new List<string>();
    public ISet<string> HoldPackages { get; } = new HashSet<string>(StringComparer.Ordinal);
    public ISet<string> SyncFirst { get; } = new HashSet<string>(StringComparer.Ordinal);
    public SigLevel SignatureLevel { get; private set; }
    public SigLevel LocalFileSignatureLevel { get; private set; }
    public SigLevel RemoteFileSignatureLevel { get; private set; }
    public IReadOnlyList<AlpmRepo> Repositories => repositories;

    // Compatibility aliases for callers ported mechanically from Vala.
    public string? dbpath => DbPath;
    public bool checkspace { get => CheckSpace; set => CheckSpace = value; }
    public ISet<string> ignorepkgs => IgnorePackages;

    public void Reload()
    {
        lock (sync)
        {
            rootDirectory = null;
            DbPath = null;
            logFile = null;
            gpgDirectory = null;
            DownloadUser = "alpm";
            DisableSandbox = false;
            UseSysLog = 0;
            CheckSpace = false;
            Architectures.Clear();
            CacheDirectories.Clear();
            HookDirectories.Clear();
            IgnoreGroups.Clear();
            IgnorePackages.Clear();
            NoExtracts.Clear();
            NoUpgrades.Clear();
            HoldPackages.Clear();
            SyncFirst.Clear();
            repositories.Clear();
            SignatureLevel = SigLevel.Package | SigLevel.PackageOptional |
                             SigLevel.Database | SigLevel.DatabaseOptional;
            LocalFileSignatureLevel = SigLevel.UseDefault;
            RemoteFileSignatureLevel = SigLevel.UseDefault;

            ParseFile(ConfigurationPath, null, new HashSet<string>(StringComparer.Ordinal));

            rootDirectory ??= "/";
            DbPath ??= "/var/lib/pacman/";
            logFile ??= "/var/log/pacman.log";
            if (CacheDirectories.Count == 0) CacheDirectories.Add("/var/cache/pacman/pkg/");
            if (HookDirectories.Count == 0) HookDirectories.Add("/etc/pacman.d/hooks/");
            gpgDirectory ??= "/etc/pacman.d/gnupg/";
            if (Architectures.Count == 0)
                Architectures.Add(RuntimeInformation.OSArchitecture switch
                {
                    Architecture.Arm64 => "aarch64",
                    Architecture.X86 => "i686",
                    _ => "x86_64"
                });

            // pacman itself supplies these on Arch/Manjaro; preserving them is
            // important for the transaction resolver.
            SyncFirst.Add("archlinux-keyring");
            SyncFirst.Add("manjaro-keyring");
        }
    }

    public string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path) && string.Equals(rootDirectory, "/", StringComparison.Ordinal))
            return path;
        if (Path.IsPathRooted(path) && rootDirectory is not null && rootDirectory != "/")
            return Path.Combine(rootDirectory, path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(rootDirectory ?? "/", path);
    }

    public string LocalDatabasePath => ResolvePath(DbPath ?? "/var/lib/pacman/");
    public string SyncDatabasePath => Path.Combine(LocalDatabasePath, "sync");

    public string ExpandServer(string url, string? repository = null)
    {
        var result = url.Replace("$repo", repository ?? string.Empty, StringComparison.Ordinal);
        result = result.Replace("$arch", Architectures.FirstOrDefault() ?? "x86_64", StringComparison.Ordinal);
        return result;
    }

    /// <summary>Updates the small subset of pacman.conf that Pamac changes.</summary>
    public void Write(IReadOnlyDictionary<string, object?> values)
    {
        if (!File.Exists(ConfigurationPath))
            throw new FileNotFoundException("pacman.conf was not found.", ConfigurationPath);

        var lines = File.ReadAllLines(ConfigurationPath).ToList();
        if (values.TryGetValue("CheckSpace", out var checkSpace) && checkSpace is bool check)
            CheckSpace = check;
        if (values.TryGetValue("IgnorePkg", out var ignore) && ignore is string ignoreValue)
        {
            IgnorePackages.Clear();
            foreach (var package in SplitWords(ignoreValue)) IgnorePackages.Add(package);
        }

        RewriteOption(lines, "CheckSpace", CheckSpace);
        RewriteOption(lines, "IgnorePkg", IgnorePackages.Count == 0 ? null : string.Join(' ', IgnorePackages));
        File.WriteAllLines(ConfigurationPath, lines);
        Reload();
    }

    public void RegisterSyncDatabases()
    {
        // Kept as an explicit operation to match the Vala call sites. The
        // process backend reads Repositories directly when it invokes pacman.
    }

    private void ParseFile(string path, string? section, ISet<string> included)
    {
        if (!File.Exists(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (!included.Add(fullPath)) return;

        var currentSection = section;
        foreach (var original in File.ReadLines(fullPath))
        {
            var line = StripComment(original).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!string.Equals(currentSection, "options", StringComparison.OrdinalIgnoreCase) &&
                    !repositories.Any(repository => repository.Name == currentSection))
                    repositories.Add(new AlpmRepo(currentSection));
                continue;
            }

            var split = SplitKeyValue(line);
            var key = split.Key;
            var value = split.Value;
            if (key.Equals("Include", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var include in ExpandInclude(value, Path.GetDirectoryName(fullPath) ?? "."))
                    ParseFile(include, currentSection, included);
                continue;
            }

            if (string.Equals(currentSection, "options", StringComparison.OrdinalIgnoreCase) || currentSection is null)
                ParseOption(key, value);
            else
                ParseRepositoryOption(currentSection, key, value);
        }
    }

    private void ParseOption(string key, string value)
    {
        var words = SplitWords(value);
        if (key.Equals("RootDir", StringComparison.OrdinalIgnoreCase)) rootDirectory = First(value);
        else if (key.Equals("DBPath", StringComparison.OrdinalIgnoreCase)) DbPath = First(value);
        else if (key.Equals("LogFile", StringComparison.OrdinalIgnoreCase)) logFile = First(value);
        else if (key.Equals("GPGDir", StringComparison.OrdinalIgnoreCase)) gpgDirectory = First(value);
        else if (key.Equals("DownloadUser", StringComparison.OrdinalIgnoreCase)) DownloadUser = First(value);
        else if (key.Equals("DisableSandbox", StringComparison.OrdinalIgnoreCase)) DisableSandbox = true;
        else if (key.Equals("UseSyslog", StringComparison.OrdinalIgnoreCase)) UseSysLog = 1;
        else if (key.Equals("CheckSpace", StringComparison.OrdinalIgnoreCase)) CheckSpace = true;
        else if (key.Equals("Architecture", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var word in words)
                if (word.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
                else if (!Architectures.Contains(word, StringComparer.Ordinal)) Architectures.Add(word);
        }
        else if (key.Equals("CacheDir", StringComparison.OrdinalIgnoreCase)) AddUnique(CacheDirectories, words);
        else if (key.Equals("HookDir", StringComparison.OrdinalIgnoreCase)) AddUnique(HookDirectories, words);
        else if (key.Equals("IgnoreGroup", StringComparison.OrdinalIgnoreCase)) AddUnique(IgnoreGroups, words);
        else if (key.Equals("IgnorePkg", StringComparison.OrdinalIgnoreCase)) AddUnique(IgnorePackages, words);
        else if (key.Equals("NoExtract", StringComparison.OrdinalIgnoreCase)) AddUnique(NoExtracts, words);
        else if (key.Equals("NoUpgrade", StringComparison.OrdinalIgnoreCase)) AddUnique(NoUpgrades, words);
        else if (key.Equals("HoldPkg", StringComparison.OrdinalIgnoreCase)) AddUnique(HoldPackages, words);
        else if (key.Equals("SyncFirst", StringComparison.OrdinalIgnoreCase)) AddUnique(SyncFirst, words);
        else if (key.Equals("SigLevel", StringComparison.OrdinalIgnoreCase)) SignatureLevel = ParseSigLevel(words, SignatureLevel);
        else if (key.Equals("LocalFileSigLevel", StringComparison.OrdinalIgnoreCase)) LocalFileSignatureLevel = ParseSigLevel(words, LocalFileSignatureLevel);
        else if (key.Equals("RemoteFileSigLevel", StringComparison.OrdinalIgnoreCase)) RemoteFileSignatureLevel = ParseSigLevel(words, RemoteFileSignatureLevel);
    }

    private void ParseRepositoryOption(string section, string key, string value)
    {
        var repository = repositories.FirstOrDefault(item => item.Name == section);
        if (repository is null)
        {
            repository = new AlpmRepo(section);
            repositories.Add(repository);
        }

        var words = SplitWords(value);
        if (key.Equals("Server", StringComparison.OrdinalIgnoreCase))
            foreach (var word in words) repository.Urls.Add(word);
        else if (key.Equals("SigLevel", StringComparison.OrdinalIgnoreCase))
            repository.SignatureLevel = ParseSigLevel(words, repository.SignatureLevel);
        else if (key.Equals("Usage", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var word in words)
            {
                repository.Usage |= word.ToLowerInvariant() switch
                {
                    "sync" => DatabaseUsage.Sync,
                    "search" => DatabaseUsage.Search,
                    "install" => DatabaseUsage.Install,
                    "upgrade" => DatabaseUsage.Upgrade,
                    "all" => DatabaseUsage.All,
                    _ => DatabaseUsage.None
                };
            }
        }
    }

    private static SigLevel ParseSigLevel(IEnumerable<string> words, SigLevel fallback)
    {
        var result = SigLevel.None;
        foreach (var word in words)
        {
            result |= word.ToLowerInvariant() switch
            {
                "required" => SigLevel.Package | SigLevel.Database,
                "optional" => SigLevel.PackageOptional | SigLevel.DatabaseOptional,
                "package" => SigLevel.Package,
                "packageoptional" => SigLevel.PackageOptional,
                "database" => SigLevel.Database,
                "databaseoptional" => SigLevel.DatabaseOptional,
                "trustedonly" => SigLevel.PackageMarginalOk | SigLevel.DatabaseMarginalOk,
                "trustall" => SigLevel.PackageUnknownOk | SigLevel.DatabaseUnknownOk,
                "never" => SigLevel.None,
                "use-default" => SigLevel.UseDefault,
                _ => SigLevel.None
            };
        }
        return result == SigLevel.None && words.Any() ? SigLevel.None : result == SigLevel.None ? fallback : result;
    }

    private static IEnumerable<string> ExpandInclude(string pattern, string baseDirectory)
    {
        pattern = First(pattern);
        if (!Path.IsPathRooted(pattern)) pattern = Path.Combine(baseDirectory, pattern);
        if (!pattern.Contains('*') && !pattern.Contains('?')) return new[] { pattern };
        var directory = Path.GetDirectoryName(pattern);
        var filePattern = Path.GetFileName(pattern);
        return directory is not null && Directory.Exists(directory)
            ? Directory.GetFiles(directory, filePattern).OrderBy(path => path, StringComparer.Ordinal)
            : Array.Empty<string>();
    }

    private static (string Key, string Value) SplitKeyValue(string line)
    {
        var equals = line.IndexOf('=');
        var whitespace = line.IndexOfAny(new[] { ' ', '\t' });
        var splitAt = equals >= 0 && (whitespace < 0 || equals < whitespace) ? equals : whitespace;
        if (splitAt < 0) return (line.Trim(), string.Empty);
        return (line[..splitAt].Trim(), line[(splitAt + 1)..].Trim());
    }

    private static string StripComment(string value)
    {
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"') quoted = !quoted;
            else if (value[i] == '#' && !quoted) return value[..i];
        }
        return value;
    }

    private static List<string> SplitWords(string value)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        foreach (var character in value)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (builder.Length > 0) { result.Add(builder.ToString()); builder.Clear(); }
            }
            else builder.Append(character);
        }
        if (builder.Length > 0) result.Add(builder.ToString());
        return result;
    }

    private static string First(string value) => SplitWords(value).FirstOrDefault() ?? string.Empty;

    private static void AddUnique(ICollection<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
            if (!target.Contains(value)) target.Add(value);
    }

    private static void RewriteOption(IList<string> lines, string key, object? value)
    {
        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var stripped = StripComment(lines[i]).Trim();
            if (!stripped.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = value switch
            {
                null => $"#{key}",
                bool boolean => boolean ? key : $"#{key}",
                _ => $"{key} = {value}"
            };
            found = true;
            break;
        }
        if (!found && value is not null)
            lines.Add(value is bool boolean && boolean ? key : $"{key} = {value}");
    }
}
