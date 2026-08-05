namespace Pamac;

public interface IFlatpakPlugin
{
    bool IsAvailable { get; }
    void Refresh();
    IReadOnlyList<string> GetRemoteNames();
    IReadOnlyList<FlatpakPackage> Search(string searchString);
    IReadOnlyList<FlatpakPackage> SearchUninstalled(IEnumerable<string> searchTerms);
    bool IsInstalled(string idOrName);
    FlatpakPackage? GetByAppId(string appId);
    FlatpakPackage? Get(string id);
    IReadOnlyList<FlatpakPackage> GetInstalled();
    IReadOnlyList<FlatpakPackage> GetCategory(string category);
    IReadOnlyList<FlatpakPackage> GetUpdates();
    Task<bool> InstallAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);
    Task<bool> UpgradeAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);
}

public class FlatpakClient : IFlatpakPlugin
{
    private readonly Dictionary<string, FlatpakPackageRecord> cache = new(StringComparer.Ordinal);
    public bool IsAvailable => ProcessRunner.IsAvailable("flatpak");

    public void Refresh() => cache.Clear();

    public IReadOnlyList<string> GetRemoteNames()
    {
        if (!IsAvailable) return Array.Empty<string>();
        var result = ProcessRunner.Run("flatpak", new[] { "remotes", "--columns=name" });
        return result.Succeeded
            ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
    }

    public IReadOnlyList<FlatpakPackage> Search(string searchString)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(searchString)) return Array.Empty<FlatpakPackage>();
        var result = ProcessRunner.Run("flatpak", new[] { "search", "--columns=application,name,description,version,origin", searchString });
        if (!result.Succeeded) return Array.Empty<FlatpakPackage>();
        var packages = new List<FlatpakPackage>();
        foreach (var line in Lines(result.StandardOutput))
        {
            var columns = Columns(line);
            if (columns.Count == 0) continue;
            var id = columns[0];
            if (id is "Application ID" or "application") continue;
            var data = new FlatpakData
            {
                Name = id,
                Id = id,
                Version = Get(columns, 3) ?? string.Empty,
                Description = Get(columns, 2),
                Repository = Get(columns, 4),
                AppId = id
            };
            packages.Add(GetOrCreate(data));
        }
        return packages;
    }

    public IReadOnlyList<FlatpakPackage> SearchUninstalled(IEnumerable<string> searchTerms)
    {
        var installed = GetInstalled().Select(package => package.Name).ToHashSet(StringComparer.Ordinal);
        return Search(string.Join(' ', searchTerms)).Where(package => !installed.Contains(package.Name)).ToArray();
    }

    public bool IsInstalled(string idOrName)
        => GetInstalled().Any(package => package.Name == idOrName || package.Id == idOrName || package.AppId == idOrName);

    public FlatpakPackage? GetByAppId(string appId)
        => GetInstalled().Concat(Search(appId)).FirstOrDefault(package => package.AppId == appId || package.Name == appId);

    public FlatpakPackage? Get(string id)
        => GetInstalled().Concat(Search(id)).FirstOrDefault(package => package.Id == id || package.Name == id);

    public IReadOnlyList<FlatpakPackage> GetInstalled()
    {
        if (!IsAvailable) return Array.Empty<FlatpakPackage>();
        var result = ProcessRunner.Run("flatpak", new[] { "list", "--app", "--columns=application,version,origin,name,comment" });
        if (!result.Succeeded) return Array.Empty<FlatpakPackage>();
        var packages = new List<FlatpakPackage>();
        foreach (var line in Lines(result.StandardOutput))
        {
            var columns = Columns(line);
            if (columns.Count == 0) continue;
            var id = columns[0];
            if (id.Equals("Application", StringComparison.OrdinalIgnoreCase)) continue;
            var data = new FlatpakData
            {
                Name = id,
                Id = $"{Get(columns, 2) ?? ""}/{id}",
                AppId = id,
                Version = Get(columns, 1) ?? string.Empty,
                Repository = Get(columns, 2),
                Description = Get(columns, 4),
                InstalledVersion = Get(columns, 1),
                IsInstalled = true
            };
            packages.Add(GetOrCreate(data));
        }
        return packages;
    }

    public IReadOnlyList<FlatpakPackage> GetCategory(string category)
    {
        // Flatpak's command line client does not expose AppStream categories
        // consistently across releases. Search still provides useful results;
        // callers can filter by the description or AppId.
        return Search(category);
    }

    public IReadOnlyList<FlatpakPackage> GetUpdates()
    {
        if (!IsAvailable) return Array.Empty<FlatpakPackage>();
        var result = ProcessRunner.Run("flatpak", new[] { "remote-ls", "--updates", "--app", "--columns=application,version,origin,name,comment" });
        return result.Succeeded ? ParseRemoteLines(result.StandardOutput) : Array.Empty<FlatpakPackage>();
    }

    public async Task<bool> InstallAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
        => await RunTransactionAsync("install", packages, cancellationToken).ConfigureAwait(false);

    public async Task<bool> RemoveAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
        => await RunTransactionAsync("uninstall", packages, cancellationToken).ConfigureAwait(false);

    public async Task<bool> UpgradeAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
        => await RunTransactionAsync("update", packages, cancellationToken).ConfigureAwait(false);

    private async Task<bool> RunTransactionAsync(string operation, IEnumerable<string> packages, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return false;
        var values = packages.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0) return true;
        var arguments = new List<string> { operation, "--noninteractive" };
        arguments.AddRange(values);
        var result = await ProcessRunner.RunAsync("flatpak", arguments, environment: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) Refresh();
        return result.Succeeded;
    }

    private IReadOnlyList<FlatpakPackage> ParseRemoteLines(string text)
    {
        var packages = new List<FlatpakPackage>();
        foreach (var line in Lines(text))
        {
            var columns = Columns(line);
            if (columns.Count == 0) continue;
            var id = columns[0];
            if (id.Equals("Application", StringComparison.OrdinalIgnoreCase)) continue;
            packages.Add(GetOrCreate(new FlatpakData
            {
                Name = id,
                Id = $"{Get(columns, 2) ?? ""}/{id}",
                AppId = id,
                Version = Get(columns, 1) ?? string.Empty,
                Repository = Get(columns, 2),
                Description = Get(columns, 4)
            }));
        }
        return packages;
    }

    private FlatpakPackageRecord GetOrCreate(FlatpakData data)
    {
        if (cache.TryGetValue(data.Id, out var package))
        {
            package.Merge(data);
            return package;
        }
        package = new FlatpakPackageRecord(data);
        cache[data.Id] = package;
        return package;
    }

    private static IEnumerable<string> Lines(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string> Columns(string line)
        => line.Contains('\t')
            ? line.Split('\t').Select(value => value.Trim()).ToList()
            : line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? Get(IReadOnlyList<string> values, int index) => index < values.Count ? values[index] : null;
}

public sealed class FlatpakPackageRecord : FlatpakPackage
{
    private readonly FlatpakData data;
    internal FlatpakPackageRecord(FlatpakData value) { data = value; }
    internal void Merge(FlatpakData value)
    {
        if (!string.IsNullOrEmpty(value.Version)) data.Version = value.Version;
        data.InstalledVersion ??= value.InstalledVersion;
        data.Description ??= value.Description;
        data.Repository ??= value.Repository;
        data.IsInstalled |= value.IsInstalled;
    }

    public override string Name => data.Name;
    public override string Id => data.Id;
    public override string? AppName => data.Name;
    public override string? AppId => data.AppId;
    public override string Version => data.Version;
    public override string? InstalledVersion => data.InstalledVersion;
    public override string? Description => data.Description;
    public override string? LongDescription => data.LongDescription;
    public override string? Repository => data.Repository;
    public override string? Launchable => data.Launchable;
    public override string? License => data.License;
    public override string? Url => data.Url;
    public override string? Icon => data.Icon;
    public override ulong InstalledSize => data.InstalledSize;
    public override ulong DownloadSize => data.DownloadSize;
    public override DateTimeOffset? InstallDate => data.InstallDate;
    public override IReadOnlyList<string> Screenshots => data.Screenshots;
}

internal sealed class FlatpakData
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? InstalledVersion { get; set; }
    public string? Description { get; set; }
    public string? LongDescription { get; set; }
    public string? Repository { get; set; }
    public string? Launchable { get; set; }
    public string? License { get; set; }
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public ulong InstalledSize { get; set; }
    public ulong DownloadSize { get; set; }
    public DateTimeOffset? InstallDate { get; set; }
    public IReadOnlyList<string> Screenshots { get; set; } = Array.Empty<string>();
    public bool IsInstalled { get; set; }
}
