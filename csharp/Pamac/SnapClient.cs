namespace Pamac;

public interface ISnapPlugin
{
    bool IsAvailable { get; }
    void Refresh();
    IReadOnlyList<SnapPackage> Search(string searchString);
    IReadOnlyList<SnapPackage> SearchUninstalled(string searchString);
    bool IsInstalled(string name);
    SnapPackage? Get(string name);
    SnapPackage? GetByAppId(string appId);
    IReadOnlyList<SnapPackage> GetInstalled();
    string GetInstalledIcon(string name);
    IReadOnlyList<SnapPackage> GetCategory(string category);
    IReadOnlyList<string> GetChannels(string name);
    Task<bool> InstallAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);
    Task<bool> SwitchChannelAsync(string name, string channel, CancellationToken cancellationToken = default);
}

public class SnapClient : ISnapPlugin
{
    private readonly Dictionary<string, SnapPackageRecord> cache = new(StringComparer.Ordinal);
    public bool IsAvailable => ProcessRunner.IsAvailable("snap");

    public void Refresh() => cache.Clear();

    public IReadOnlyList<SnapPackage> Search(string searchString)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(searchString)) return Array.Empty<SnapPackage>();
        var result = ProcessRunner.Run("snap", new[] { "find", searchString });
        if (!result.Succeeded) return Array.Empty<SnapPackage>();
        var packages = new List<SnapPackage>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            var name = fields[0];
            if (name.Any(char.IsPunctuation) && name.Contains("-")) { /* valid snap names may contain hyphens */ }
            var data = new SnapData
            {
                Name = name,
                Id = "Snap/" + name,
                Version = fields.Length > 1 ? fields[1] : string.Empty,
                Publisher = fields.Length > 2 ? fields[2] : null,
                Description = fields.Length > 4 ? string.Join(' ', fields.Skip(4)) : null,
                AppId = name
            };
            packages.Add(GetOrCreate(data));
        }
        return packages;
    }

    public IReadOnlyList<SnapPackage> SearchUninstalled(string searchString)
    {
        var installed = GetInstalled().Select(package => package.Name).ToHashSet(StringComparer.Ordinal);
        return Search(searchString).Where(package => !installed.Contains(package.Name)).ToArray();
    }

    public bool IsInstalled(string name) => GetInstalled().Any(package => package.Name == name);

    public SnapPackage? Get(string name)
    {
        var installed = GetInstalled().FirstOrDefault(package => package.Name == name);
        return installed ?? Search(name).FirstOrDefault(package => package.Name == name);
    }

    public SnapPackage? GetByAppId(string appId)
        => GetInstalled().Concat(Search(appId)).FirstOrDefault(package => package.AppId == appId || package.Name == appId);

    public IReadOnlyList<SnapPackage> GetInstalled()
    {
        if (!IsAvailable) return Array.Empty<SnapPackage>();
        var result = ProcessRunner.Run("snap", new[] { "list" });
        if (!result.Succeeded) return Array.Empty<SnapPackage>();
        var packages = new List<SnapPackage>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            var name = fields[0];
            var data = new SnapData
            {
                Name = name,
                Id = "Snap/" + name,
                Version = fields[1],
                InstalledVersion = fields[1],
                Channel = fields.Length > 4 ? fields[4] : null,
                AppId = name,
                IsInstalled = true
            };
            packages.Add(GetOrCreate(data));
        }
        return packages;
    }

    public string GetInstalledIcon(string name)
    {
        var info = ProcessRunner.Run("snap", new[] { "info", name });
        if (!info.Succeeded) return string.Empty;
        return info.StandardOutput.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith("icon:", StringComparison.OrdinalIgnoreCase))?
                   .Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<SnapPackage> GetCategory(string category) => Search(category);

    public IReadOnlyList<string> GetChannels(string name)
    {
        var package = Get(name);
        return package?.Channels ?? Array.Empty<string>();
    }

    public async Task<bool> InstallAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
        => await RunAsync(new[] { "install", "--yes" }, packages, cancellationToken).ConfigureAwait(false);

    public async Task<bool> RemoveAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default)
        => await RunAsync(new[] { "remove", "--yes" }, packages, cancellationToken).ConfigureAwait(false);

    public async Task<bool> SwitchChannelAsync(string name, string channel, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(channel) || channel.Any(char.IsWhiteSpace)) throw new ArgumentException("Invalid snap channel.", nameof(channel));
        return await RunAsync(new[] { "refresh", name, "--channel", channel }, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RunAsync(IEnumerable<string> prefix, IEnumerable<string> values, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return false;
        var arguments = prefix.Concat(values).ToList();
        var result = await ProcessRunner.RunAsync("snap", arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) Refresh();
        return result.Succeeded;
    }

    private SnapPackageRecord GetOrCreate(SnapData data)
    {
        if (cache.TryGetValue(data.Name, out var package))
        {
            package.Merge(data);
            return package;
        }
        package = new SnapPackageRecord(data);
        cache[data.Name] = package;
        return package;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace) || name.Contains('/'))
            throw new ArgumentException("Invalid snap name.", nameof(name));
    }
}

public sealed class SnapPackageRecord : SnapPackage
{
    private readonly SnapData data;
    internal SnapPackageRecord(SnapData value) { data = value; }
    internal void Merge(SnapData value)
    {
        if (!string.IsNullOrEmpty(value.Version)) data.Version = value.Version;
        data.InstalledVersion ??= value.InstalledVersion;
        data.Channel ??= value.Channel;
        data.Description ??= value.Description;
        data.Publisher ??= value.Publisher;
        data.IsInstalled |= value.IsInstalled;
    }

    public override string Name => data.Name;
    public override string Id => data.Id;
    public override string? AppName => data.Title ?? data.Name;
    public override string? AppId => data.AppId;
    public override string Version => data.Version;
    public override string? InstalledVersion => data.InstalledVersion;
    public override string? Description => data.Description;
    public override string? LongDescription => data.LongDescription;
    public override string? Repository => "Snap";
    public override string? Launchable => data.Launchable;
    public override string? License => data.License;
    public override string? Url => data.Url;
    public override string? Icon => data.Icon;
    public override ulong InstalledSize => data.InstalledSize;
    public override ulong DownloadSize => data.DownloadSize;
    public override DateTimeOffset? InstallDate => data.InstallDate;
    public override IReadOnlyList<string> Screenshots => data.Screenshots;
    public override string? Channel => data.Channel;
    public override string? Publisher => data.Publisher;
    public override string? Confined => data.Confined;
    public override IReadOnlyList<string> Channels => data.Channels;
}

internal sealed class SnapData
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? InstalledVersion { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? LongDescription { get; set; }
    public string? Channel { get; set; }
    public string? Publisher { get; set; }
    public string? Confined { get; set; }
    public string? License { get; set; }
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public string? Launchable { get; set; }
    public ulong InstalledSize { get; set; }
    public ulong DownloadSize { get; set; }
    public DateTimeOffset? InstallDate { get; set; }
    public IReadOnlyList<string> Screenshots { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Channels { get; set; } = Array.Empty<string>();
    public bool IsInstalled { get; set; }
}
