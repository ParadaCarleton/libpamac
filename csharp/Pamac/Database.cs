using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Pamac;

/// <summary>
/// Package database and search service. Native libalpm and GObject are replaced
/// with pacman database parsing plus the pacman command line interface; this
/// keeps the C# library portable and avoids unsafe ABI bindings.
/// </summary>
public sealed class Database : IDisposable
{
    private static readonly Regex PacmanSearchLine = new(
        @"^(?<repo>[^/\s]+)/(?<name>\S+)\s+(?<version>\S+)(?:\s+\[(?<state>[^]]+)\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PacmanQueryLine = new(
        @"^(?<name>\S+)\s+(?<old>\S+)\s+->\s+(?<new>\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PackageNamePattern = new("^[A-Za-z0-9@._+:-]+$", RegexOptions.Compiled);
    private readonly object sync = new();
    private readonly Dictionary<string, PackageData> localPackages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackageData> syncPackages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PacmanPackage> packageCache = new(StringComparer.Ordinal);
    private readonly HttpClient httpClient;
    private DateTimeOffset? lastRefresh;
    private bool disposed;

    public Database(Config config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Utils.GetUserAgent());
        Reload();
        if (Config.EnableAppstream)
            Appstream.Load(GetRepositoryNames());
        if (Config.EnableAur)
        {
            Aur = new AURClient(httpClient);
            Aur.SetRealBuildDirectory(Config.AURBuildDirectory);
        }
        Snap = new SnapClient();
        Flatpak = new FlatpakClient();
    }

    public Config Config { get; }
    public AURClient? Aur { get; private set; }
    public AppstreamClient Appstream { get; } = new();
    public SnapClient Snap { get; }
    public FlatpakClient Flatpak { get; }
    public IReadOnlyList<string> RepositoryNames => GetRepositoryNames();
    public event EventHandler<ProgressEventArgs>? GetUpdatesProgress;
    public event EventHandler<ErrorEventArgs>? Warning;

    // Compatibility aliases.
    public Config config => Config;
    public event EventHandler<ProgressEventArgs>? get_updates_progress { add => GetUpdatesProgress += value; remove => GetUpdatesProgress -= value; }
    public event EventHandler<ErrorEventArgs>? emit_warning { add => Warning += value; remove => Warning -= value; }

    public void Reload()
    {
        lock (sync)
        {
            localPackages.Clear();
            syncPackages.Clear();
            packageCache.Clear();
            LoadLocalPackages();
            lastRefresh = GetLastRefreshTime();
        }
    }

    public void Refresh() => Reload();

    public IReadOnlyList<string> GetMirrorsCountries()
    {
        var result = ProcessRunner.Run("pacman-mirrors", new[] { "-l" }, environment: Config.EnvironmentVariables);
        return result.Succeeded ? Lines(result.StandardOutput) : Array.Empty<string>();
    }

    public Task<IReadOnlyList<string>> GetMirrorsCountriesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetMirrorsCountries, cancellationToken);

    public string GetMirrorsChosenCountry()
    {
        var result = ProcessRunner.Run("pacman-mirrors", new[] { "-lc" }, environment: Config.EnvironmentVariables);
        return result.Succeeded ? Lines(result.StandardOutput).FirstOrDefault() ?? string.Empty : string.Empty;
    }

    public Task<string> GetMirrorsChosenCountryAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetMirrorsChosenCountry, cancellationToken);
    public Task<string> GetMirrorsChoosenCountryAsync(CancellationToken cancellationToken = default) => GetMirrorsChosenCountryAsync(cancellationToken);

    public string GetAlpmDependencyName(string dependency) => AlpmVersionComparer.GetName(dependency);
    public string get_alpm_dep_name(string dependency) => GetAlpmDependencyName(dependency);
    public int CompareVersions(string left, string right) => AlpmVersionComparer.Compare(left, right);

    public bool IsInstalledPackage(string packageName)
    {
        lock (sync) return localPackages.ContainsKey(packageName);
    }
    public bool is_installed_pkg(string packageName) => IsInstalledPackage(packageName);

    public AlpmPackage? GetInstalledPackage(string packageName)
    {
        lock (sync)
        {
            return localPackages.TryGetValue(packageName, out var local) ? BuildPackage(packageName, local, null) : null;
        }
    }
    public AlpmPackage? get_installed_pkg(string packageName) => GetInstalledPackage(packageName);

    public bool HasInstalledSatisfier(string dependency)
        => GetInstalledSatisfier(dependency) is not null;

    public AlpmPackage? GetInstalledSatisfier(string dependency)
    {
        var name = GetAlpmDependencyName(dependency);
        lock (sync)
        {
            foreach (var package in localPackages.Values)
            {
                if (Satisfies(package, dependency, name))
                    return BuildPackage(package.Name, package, null);
            }
        }
        return null;
    }

    public IReadOnlyList<AlpmPackage> GetInstalledPackagesByGlob(string glob)
    {
        var regex = GlobToRegex(glob);
        lock (sync)
            return localPackages.Values.Where(package => regex.IsMatch(package.Name))
                .Select(package => BuildPackage(package.Name, package, null)!).ToArray();
    }

    public bool ShouldHold(string packageName) => Config.AlpmConfig.HoldPackages.Contains(packageName);

    public IReadOnlyList<AlpmPackage> GetInstalledPackages()
    {
        lock (sync) return localPackages.Values.Select(package => BuildPackage(package.Name, package, null)!).ToArray();
    }
    public Task<IReadOnlyList<AlpmPackage>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetInstalledPackages, cancellationToken);

    public async Task<IReadOnlyList<AlpmPackage>> GetInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        if (Config.EnableAppstream && Appstream.Apps.Count == 0)
            Appstream.Load(GetRepositoryNames());
        return await Task.Run<IReadOnlyList<AlpmPackage>>(() => GetInstalledPackages()
            .Where(package => package.AppId is not null)
            .Cast<AlpmPackage>()
            .ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<AlpmPackage> GetExplicitlyInstalledPackages()
    {
        lock (sync) return localPackages.Values.Where(package => package.ExplicitlyInstalled)
            .Select(package => BuildPackage(package.Name, package, null)!).ToArray();
    }
    public Task<IReadOnlyList<AlpmPackage>> GetExplicitlyInstalledPackagesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetExplicitlyInstalledPackages, cancellationToken);

    public IReadOnlyList<AlpmPackage> GetForeignPackages()
    {
        var foreign = new HashSet<string>(StringComparer.Ordinal);
        if (ProcessRunner.IsAvailable("pacman"))
        {
            var result = RunPacman(new[] { "-Qm" });
            if (result.Succeeded)
                foreach (var line in Lines(result.StandardOutput))
                    foreign.Add(line.Split(' ', 2)[0]);
        }
        lock (sync)
        {
            return localPackages.Values.Where(package => foreign.Contains(package.Name) ||
                    (!syncPackages.ContainsKey(package.Name) && !foreign.Any()))
                .Select(package => BuildPackage(package.Name, package, null)!).ToArray();
        }
    }
    public Task<IReadOnlyList<AlpmPackage>> GetForeignPackagesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetForeignPackages, cancellationToken);

    public IReadOnlyList<AlpmPackage> GetOrphans()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = RunPacman(new[] { "-Qdtq" });
        if (result.Succeeded)
            foreach (var line in Lines(result.StandardOutput)) names.Add(line);
        lock (sync)
        {
            var values = names.Count > 0
                ? localPackages.Values.Where(package => names.Contains(package.Name))
                : localPackages.Values.Where(package => !package.ExplicitlyInstalled && GetRequiredBy(package.Name).Count == 0);
            return values.Select(package => BuildPackage(package.Name, package, null)!).ToArray();
        }
    }
    public Task<IReadOnlyList<AlpmPackage>> GetOrphansAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetOrphans, cancellationToken);

    public bool IsSyncPackage(string packageName)
    {
        var package = GetSyncPackage(packageName);
        return package is not null;
    }

    public AlpmPackage? GetSyncPackage(string packageName)
    {
        lock (sync)
        {
            if (syncPackages.TryGetValue(packageName, out var cached))
                return BuildPackage(packageName, localPackages.GetValueOrDefault(packageName), cached);
        }

        var value = QuerySyncPackage(packageName);
        if (value is null) return null;
        lock (sync)
        {
            syncPackages[packageName] = value;
            return BuildPackage(packageName, localPackages.GetValueOrDefault(packageName), value);
        }
    }

    public bool HasSyncSatisfier(string dependency) => GetSyncSatisfier(dependency) is not null;

    public AlpmPackage? GetSyncSatisfier(string dependency)
    {
        var name = GetAlpmDependencyName(dependency);
        var exact = GetSyncPackage(name);
        if (exact is not null && Satisfies(exact, dependency, name)) return exact;

        var result = RunPacman(new[] { "-Ssq", name });
        foreach (var candidate in Lines(result.StandardOutput))
        {
            var packageName = candidate.Contains('/') ? candidate[(candidate.LastIndexOf('/') + 1)..] : candidate;
            var package = GetSyncPackage(packageName);
            if (package is not null && Satisfies(package, dependency, name)) return package;
        }
        return null;
    }

    public IReadOnlyList<AlpmPackage> GetSyncPackagesByGlob(string glob)
    {
        var names = Lines(RunPacman(new[] { "-Ssq", glob }).StandardOutput);
        var result = new List<AlpmPackage>();
        foreach (var name in names)
        {
            var packageName = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            var package = GetSyncPackage(packageName);
            if (package is not null) result.Add(package);
        }
        return result;
    }

    public Package? GetAppById(string appId)
    {
        var app = Appstream.GetById(appId);
        if (app?.PackageName is not null)
            return GetPackage(app.PackageName);
        Package? flatpakPackage = Flatpak.GetByAppId(appId);
        return flatpakPackage ?? Snap.GetByAppId(appId);
    }

    public async Task<Stream> GetUrlStreamAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    public IReadOnlyList<AlpmPackage> SearchInstalledPackages(string searchString)
    {
        var tokens = Tokens(searchString);
        lock (sync)
        {
            return localPackages.Values.Where(package => Matches(package, tokens))
                .Select(package => BuildPackage(package.Name, package, null)!).ToArray();
        }
    }
    public Task<IReadOnlyList<AlpmPackage>> SearchInstalledPackagesAsync(string searchString, CancellationToken cancellationToken = default)
        => Task.Run(() => SearchInstalledPackages(searchString), cancellationToken);

    public IReadOnlyList<AlpmPackage> SearchRepositoryPackages(string searchString)
    {
        var result = new Dictionary<string, PackageData>(StringComparer.Ordinal);
        foreach (var package in SearchPacman(searchString)) result[package.Name] = package;
        lock (sync)
        {
            foreach (var package in localPackages.Values.Where(package => Matches(package, Tokens(searchString))))
                result.TryAdd(package.Name, package);
            return result.Values.Select(package =>
            {
                var syncPackage = syncPackages.GetValueOrDefault(package.Name) ?? (package.Repository is not null ? package : null);
                if (syncPackage is not null) syncPackages[package.Name] = syncPackage;
                return BuildPackage(package.Name, localPackages.GetValueOrDefault(package.Name), syncPackage)!;
            }).ToArray();
        }
    }
    public Task<IReadOnlyList<AlpmPackage>> SearchRepositoryPackagesAsync(string searchString, CancellationToken cancellationToken = default)
        => Task.Run(() => SearchRepositoryPackages(searchString), cancellationToken);

    public IReadOnlyList<AlpmPackage> SearchUninstalledApps(IEnumerable<string> searchTerms)
    {
        var result = new List<AlpmPackage>();
        foreach (var app in Appstream.Search(searchTerms))
        {
            if (app.PackageName is null || IsInstalledPackage(app.PackageName)) continue;
            var package = GetSyncPackage(app.PackageName);
            if (package is not null) result.Add(package);
        }
        return result;
    }

    public IReadOnlyList<AlpmPackage> SearchPackages(string searchString)
    {
        var result = SearchRepositoryPackages(searchString).ToList();
        var names = result.Select(package => package.Name).ToHashSet(StringComparer.Ordinal);
        if (Config.EnableAppstream)
        {
            foreach (var app in Appstream.Search(Tokens(searchString)))
            {
                if (app.PackageName is null || names.Contains(app.PackageName)) continue;
                var package = GetPackage(app.PackageName);
                if (package is not null) { result.Add(package); names.Add(package.Name); }
            }
        }
        return result;
    }
    public Task<IReadOnlyList<AlpmPackage>> SearchPackagesAsync(string searchString, CancellationToken cancellationToken = default)
        => Task.Run(() => SearchPackages(searchString), cancellationToken);

    public IReadOnlyList<AURPackage> SearchAURPackages(string searchString)
    {
        EnsureAur();
        return Aur!.Search(searchString).Select(info => MakeAurPackage(info)).ToArray();
    }
    public Task<IReadOnlyList<AURPackage>> SearchAURPackagesAsync(string searchString, CancellationToken cancellationToken = default)
        => SearchAurAsyncCore(searchString, cancellationToken);
    public IReadOnlyList<AURPackage> SearchAurPkgs(string searchString) => SearchAURPackages(searchString);
    private async Task<IReadOnlyList<AURPackage>> SearchAurAsyncCore(string searchString, CancellationToken cancellationToken)
    {
        EnsureAur();
        var values = await Aur!.SearchAsync(searchString, cancellationToken).ConfigureAwait(false);
        return values.Select(info => MakeAurPackage(info)).ToArray();
    }

    public Dictionary<string, IReadOnlyList<string>> SearchFiles(IEnumerable<string> files)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var query = RunPacman(new[] { "-Fq", file });
            result[file] = query.Succeeded ? Lines(query.StandardOutput) : Array.Empty<string>();
        }
        return result;
    }

    public IReadOnlyList<string> GetCategoryNames() => Appstream.Apps.Count == 0 ? Array.Empty<string>() :
        new[] { "Photo & Video", "Music & Audio", "Productivity", "Communication & News", "Education & Science", "Games", "Utilities", "Development" };
    public Task<IReadOnlyList<AlpmPackage>> GetCategoryPackagesAsync(string category, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<AlpmPackage>>(() => Appstream.GetCategoryApps(category)
            .Where(app => app.PackageName is not null)
            .Select(app => GetPackage(app.PackageName!))
            .Where(package => package is not null)
            .Cast<AlpmPackage>()
            .ToArray(), cancellationToken);
    public IReadOnlyList<string> GetRepositoryNames()
        => Config.AlpmConfig.Repositories.Select(repository => repository.Name).ToArray();
    public IReadOnlyList<AlpmPackage> GetRepositoryPackages(string repository)
    {
        var result = RunPacman(new[] { "-Sl", repository });
        var packages = new List<AlpmPackage>();
        foreach (var line in Lines(result.StandardOutput))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3) continue;
            var name = fields[1];
            var data = new PackageData { Name = name, Version = fields[2], Repository = fields[0] };
            lock (sync) syncPackages[name] = data;
            packages.Add(BuildPackage(name, localPackages.GetValueOrDefault(name), data)!);
        }
        return packages;
    }
    public IReadOnlyList<string> GetGroupNames()
    {
        var result = RunPacman(new[] { "-Sg" });
        return result.Succeeded ? Lines(result.StandardOutput).Select(line => line.Split(' ', 2)[0]).Distinct().ToArray() : Array.Empty<string>();
    }
    public IReadOnlyList<AlpmPackage> GetGroupPackages(string groupName)
    {
        var result = RunPacman(new[] { "-Sg", groupName });
        return Lines(result.StandardOutput).Select(line => line.Split(' ', 2).ElementAtOrDefault(1))
            .Where(name => !string.IsNullOrEmpty(name)).Select(name => GetPackage(name!) ?? GetSyncPackage(name!)!)
            .Where(package => package is not null).ToArray()!;
    }
    public Task<IReadOnlyList<AlpmPackage>> GetGroupPackagesAsync(string groupName, CancellationToken cancellationToken = default)
        => Task.Run(() => GetGroupPackages(groupName), cancellationToken);

    public AlpmPackage? GetPackage(string packageName)
    {
        lock (sync)
        {
            var local = localPackages.GetValueOrDefault(packageName);
            var syncPackage = syncPackages.GetValueOrDefault(packageName);
            if (local is null && syncPackage is null)
            {
                syncPackage = QuerySyncPackage(packageName);
                if (syncPackage is not null) syncPackages[packageName] = syncPackage;
            }
            return local is null && syncPackage is null ? null : BuildPackage(packageName, local, syncPackage);
        }
    }
    public AlpmPackage? get_pkg(string packageName) => GetPackage(packageName);

    public IReadOnlyList<string> GetPackageFiles(string packageName)
    {
        lock (sync)
        {
            if (localPackages.TryGetValue(packageName, out var package) && package.Files.Count > 0)
                return package.Files;
        }
        var path = FindLocalPackageDirectory(packageName);
        return path is null ? Array.Empty<string>() : ReadFilesFile(Path.Combine(path, "files"));
    }
    public Task<IReadOnlyList<string>> GetPackageFilesAsync(string packageName, CancellationToken cancellationToken = default)
        => Task.Run(() => GetPackageFiles(packageName), cancellationToken);

    public string GetRealAURBuildDirectory()
    {
        EnsureAur();
        return Aur!.RealBuildDirectory;
    }

    public IReadOnlyDictionary<string, ulong> GetCleanCacheDetails()
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var directory in Config.AlpmConfig.CacheDirectories)
        {
            var path = Config.AlpmConfig.ResolvePath(directory);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (!Path.GetFileName(file).EndsWith(".pkg.tar", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(file).Contains(".pkg.tar.", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileName(file);
                var packageName = name[..Math.Max(0, name.LastIndexOf('-'))];
                if (Config.CleanRemoveOnlyUninstalled && IsInstalledPackage(packageName)) continue;
                result[file] = (ulong)new FileInfo(file).Length;
            }
        }
        return result;
    }
    public Task<IReadOnlyDictionary<string, ulong>> GetCleanCacheDetailsAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetCleanCacheDetails, cancellationToken);

    public IReadOnlyDictionary<string, ulong> GetBuildFilesDetails()
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var root = GetRealAURBuildDirectory();
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            result[file] = (ulong)new FileInfo(file).Length;
        return result;
    }
    public Task<IReadOnlyDictionary<string, ulong>> GetBuildFilesDetailsAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetBuildFilesDetails, cancellationToken);

    public bool ShouldRefresh() => Config.RefreshPeriod != 0 &&
        (!lastRefresh.HasValue || DateTimeOffset.UtcNow - lastRefresh.Value >= TimeSpan.FromHours(Config.RefreshPeriod));
    public bool NeedRefresh() => ShouldRefresh();
    public DateTimeOffset? GetLastRefreshTime() {
        var syncDirectory = Config.AlpmConfig.SyncDatabasePath;
        if (!Directory.Exists(syncDirectory)) return null;
        var latest = Directory.EnumerateFiles(syncDirectory).Select(path => new FileInfo(path).LastWriteTimeUtc).DefaultIfEmpty().Max();
        return latest == default ? null : new DateTimeOffset(latest, TimeSpan.Zero);
    }

    public Updates GetUpdates()
    {
        var updates = new Updates();
        GetUpdatesProgress?.Invoke(this, new ProgressEventArgs("repository", 0.2));
        foreach (var package in GetInstalledPackages())
        {
            var candidate = GetSyncPackage(package.Name);
            if (candidate is null || AlpmVersionComparer.Compare(candidate.Version, package.InstalledVersion ?? package.Version) <= 0) continue;
            if (Config.IgnorePackages.Contains(package.Name)) updates.IgnoredRepositoryUpdates.Add(candidate);
            else updates.RepositoryUpdates.Add(candidate);
        }

        GetUpdatesProgress?.Invoke(this, new ProgressEventArgs("aur", 0.6));
        if (Config.EnableAur && Config.CheckAurUpdates)
        {
            foreach (var package in GetForeignPackages())
            {
                var info = Aur?.GetInfos(package.Name);
                if (info is null || AlpmVersionComparer.Compare(info.Version, package.InstalledVersion ?? package.Version) <= 0) continue;
                var aurPackage = MakeAurPackage(info);
                if (Config.IgnorePackages.Contains(package.Name)) updates.IgnoredAURUpdates.Add(aurPackage);
                else updates.AURUpdates.Add(aurPackage);
                if (info.OutOfDate is not null) updates.OutOfDate.Add(aurPackage);
            }
        }

        GetUpdatesProgress?.Invoke(this, new ProgressEventArgs("flatpak", 0.9));
        if (Config.EnableFlatpak && Config.CheckFlatpakUpdates)
            updates.FlatpakUpdates.AddRange(Flatpak.GetUpdates());
        GetUpdatesProgress?.Invoke(this, new ProgressEventArgs("done", 1));
        return updates;
    }
    public Task<Updates> GetUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetUpdates, cancellationToken);

    public IReadOnlyList<SnapPackage> SearchSnaps(string searchString) => Snap.Search(searchString);
    public Task<IReadOnlyList<SnapPackage>> SearchSnapsAsync(string searchString, CancellationToken cancellationToken = default)
        => Task.Run(() => Snap.Search(searchString), cancellationToken);
    public bool IsInstalledSnap(string name) => Snap.IsInstalled(name);
    public Task<SnapPackage?> GetSnapAsync(string name, CancellationToken cancellationToken = default)
        => Task.Run(() => Snap.Get(name), cancellationToken);
    public Task<IReadOnlyList<SnapPackage>> GetInstalledSnapsAsync(CancellationToken cancellationToken = default)
        => Task.Run(Snap.GetInstalled, cancellationToken);
    public Task<string> GetInstalledSnapIconAsync(string name, CancellationToken cancellationToken = default)
        => Task.Run(() => Snap.GetInstalledIcon(name), cancellationToken);
    public Task<IReadOnlyList<SnapPackage>> GetCategorySnapsAsync(string category, CancellationToken cancellationToken = default)
        => Task.Run(() => Snap.GetCategory(category), cancellationToken);
    public void RefreshFlatpakAppstreamData() => Flatpak.Refresh();
    public Task RefreshFlatpakAppstreamDataAsync(CancellationToken cancellationToken = default) => Task.Run(Flatpak.Refresh, cancellationToken);
    public IReadOnlyList<string> GetFlatpakRemotesNames() => Flatpak.GetRemoteNames();
    public Task<IReadOnlyList<FlatpakPackage>> GetInstalledFlatpaksAsync(CancellationToken cancellationToken = default)
        => Task.Run(Flatpak.GetInstalled, cancellationToken);
    public Task<IReadOnlyList<FlatpakPackage>> SearchFlatpaksAsync(string searchString, CancellationToken cancellationToken = default)
        => Task.Run(() => Flatpak.Search(searchString), cancellationToken);
    public bool IsInstalledFlatpak(string name) => Flatpak.IsInstalled(name);
    public Task<FlatpakPackage?> GetFlatpakAsync(string id, CancellationToken cancellationToken = default)
        => Task.Run(() => Flatpak.Get(id), cancellationToken);
    public Task<IReadOnlyList<FlatpakPackage>> GetCategoryFlatpaksAsync(string category, CancellationToken cancellationToken = default)
        => Task.Run(() => Flatpak.GetCategory(category), cancellationToken);

    public bool CloneBuildFiles(string packageName, bool overwriteFiles, CancellationToken cancellationToken = default)
    {
        EnsureAur();
        ValidatePackageName(packageName);
        var destination = Path.Combine(GetRealAURBuildDirectory(), packageName);
        if (Directory.Exists(destination))
        {
            if (!overwriteFiles) return true;
            Directory.Delete(destination, recursive: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var result = ProcessRunner.Run("git", new[] { "clone", "--depth=1", $"https://aur.archlinux.org/{packageName}.git", destination }, cancellationToken: cancellationToken);
        if (!result.Succeeded) Warning?.Invoke(this, new ErrorEventArgs(result.StandardError));
        return result.Succeeded;
    }
    public Task<bool> CloneBuildFilesAsync(string packageName, bool overwriteFiles, CancellationToken cancellationToken = default)
        => Task.Run(() => CloneBuildFiles(packageName, overwriteFiles, cancellationToken), cancellationToken);

    public bool RegenerateSrcInfo(string packageName, CancellationToken cancellationToken = default)
    {
        EnsureAur();
        ValidatePackageName(packageName);
        var directory = Path.Combine(GetRealAURBuildDirectory(), packageName);
        if (!Directory.Exists(directory)) return false;
        var result = ProcessRunner.Run("makepkg", new[] { "--printsrcinfo" }, directory, cancellationToken: cancellationToken);
        if (!result.Succeeded) return false;
        File.WriteAllText(Path.Combine(directory, ".SRCINFO"), result.StandardOutput);
        return true;
    }
    public Task<bool> RegenerateSrcInfoAsync(string packageName, CancellationToken cancellationToken = default)
        => Task.Run(() => RegenerateSrcInfo(packageName, cancellationToken), cancellationToken);

    public AURPackage? GetAurPackage(string packageName)
    {
        EnsureAur();
        var info = Aur!.GetInfos(packageName);
        return info is null ? null : MakeAurPackage(info);
    }
    public Task<AURPackage?> GetAurPackageAsync(string packageName, CancellationToken cancellationToken = default)
        => Task.Run(() => GetAurPackage(packageName), cancellationToken);
    public IReadOnlyDictionary<string, AURPackage?> GetAurPackages(IEnumerable<string> packageNames)
    {
        EnsureAur();
        var result = new Dictionary<string, AURPackage?>(StringComparer.Ordinal);
        foreach (var info in Aur!.GetMultiInfos(packageNames)) result[info.Name] = MakeAurPackage(info);
        return result;
    }
    public Task<IReadOnlyDictionary<string, AURPackage?>> GetAurPackagesAsync(IEnumerable<string> packageNames, CancellationToken cancellationToken = default)
        => Task.Run(() => GetAurPackages(packageNames), cancellationToken);

    public bool CleanCache(IEnumerable<string> files)
    {
        var success = true;
        foreach (var file in files)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { success = false; }
            catch (UnauthorizedAccessException) { success = false; }
        }
        return success;
    }
    public bool CleanBuildFiles(string buildDirectory)
    {
        try
        {
            if (Directory.Exists(buildDirectory)) Directory.Delete(buildDirectory, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void RefreshTemporaryFilesDatabases() { }
    public Task RefreshTemporaryFilesDatabasesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IReadOnlyList<string> GetRequiredBy(string packageName)
    {
        lock (sync)
        {
            return localPackages.Values.Where(package => package.Name != packageName &&
                package.Depends.Concat(package.OptionalDependencies).Any(dependency =>
                    AlpmVersionComparer.GetName(dependency) == packageName))
                .Select(package => package.Name).ToArray();
        }
    }
    public IReadOnlyList<string> GetOptionalFor(string packageName)
    {
        lock (sync)
        {
            return localPackages.Values.Where(package => package.Name != packageName &&
                package.OptionalDependencies.Any(dependency => AlpmVersionComparer.GetName(dependency) == packageName))
                .Select(package => package.Name).ToArray();
        }
    }

    // Lower-case compatibility methods mirror the names emitted by the
    // original GObject introspection API.
    public IReadOnlyList<AlpmPackage> get_installed_pkgs() => GetInstalledPackages();
    public Task<IReadOnlyList<AlpmPackage>> get_installed_pkgs_async(CancellationToken cancellationToken = default) => GetInstalledPackagesAsync(cancellationToken);
    public IReadOnlyList<AlpmPackage> search_installed_pkgs(string value) => SearchInstalledPackages(value);
    public Task<IReadOnlyList<AlpmPackage>> search_installed_pkgs_async(string value, CancellationToken cancellationToken = default) => SearchInstalledPackagesAsync(value, cancellationToken);
    public IReadOnlyList<AlpmPackage> search_repos_pkgs(string value) => SearchRepositoryPackages(value);
    public Task<IReadOnlyList<AlpmPackage>> search_repos_pkgs_async(string value, CancellationToken cancellationToken = default) => SearchRepositoryPackagesAsync(value, cancellationToken);
    public IReadOnlyList<AlpmPackage> search_pkgs(string value) => SearchPackages(value);
    public Task<IReadOnlyList<AlpmPackage>> search_pkgs_async(string value, CancellationToken cancellationToken = default) => SearchPackagesAsync(value, cancellationToken);
    public IReadOnlyList<AURPackage> search_in_aur(string value) => SearchAURPackages(value);
    public Task<IReadOnlyList<AURPackage>> search_in_aur_async(string value, CancellationToken cancellationToken = default) => SearchAURPackagesAsync(value, cancellationToken);
    public Updates get_updates() => GetUpdates();
    public Task<Updates> get_updates_async(CancellationToken cancellationToken = default) => GetUpdatesAsync(cancellationToken);
    public bool need_refresh() => NeedRefresh();
    public IReadOnlyList<string> get_repos_names() => GetRepositoryNames();
    public IReadOnlyList<string> get_pkg_files(string value) => GetPackageFiles(value);
    public Task<IReadOnlyList<string>> get_pkg_files_async(string value, CancellationToken cancellationToken = default) => GetPackageFilesAsync(value, cancellationToken);
    public string get_real_aur_build_dir() => GetRealAURBuildDirectory();
    public IReadOnlyDictionary<string, ulong> get_clean_cache_details() => GetCleanCacheDetails();
    public Task<IReadOnlyDictionary<string, ulong>> get_clean_cache_details_async(CancellationToken cancellationToken = default) => GetCleanCacheDetailsAsync(cancellationToken);
    public IReadOnlyDictionary<string, ulong> get_build_files_details() => GetBuildFilesDetails();
    public Task<IReadOnlyDictionary<string, ulong>> get_build_files_details_async(CancellationToken cancellationToken = default) => GetBuildFilesDetailsAsync(cancellationToken);
    public IReadOnlyList<string> get_groups_names() => GetGroupNames();
    public IReadOnlyList<string> get_categories_names() => GetCategoryNames();
    public AURPackage? get_aur_pkg(string value) => GetAurPackage(value);
    public Task<AURPackage?> get_aur_pkg_async(string value, CancellationToken cancellationToken = default) => GetAurPackageAsync(value, cancellationToken);
    public IReadOnlyList<SnapPackage> search_snaps(string value) => SearchSnaps(value);
    public Task<IReadOnlyList<SnapPackage>> search_snaps_async(string value, CancellationToken cancellationToken = default) => SearchSnapsAsync(value, cancellationToken);
    public bool is_installed_snap(string value) => IsInstalledSnap(value);
    public Task<IReadOnlyList<SnapPackage>> get_installed_snaps_async(CancellationToken cancellationToken = default) => GetInstalledSnapsAsync(cancellationToken);
    public Task<IReadOnlyList<FlatpakPackage>> search_flatpaks_async(string value, CancellationToken cancellationToken = default) => SearchFlatpaksAsync(value, cancellationToken);
    public bool is_installed_flatpak(string value) => IsInstalledFlatpak(value);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Aur?.Dispose();
        httpClient.Dispose();
    }

    private void LoadLocalPackages()
    {
        var localDirectory = Path.Combine(Config.AlpmConfig.LocalDatabasePath, "local");
        if (!Directory.Exists(localDirectory)) return;
        foreach (var directory in Directory.EnumerateDirectories(localDirectory))
        {
            var data = ParsePackageFile(Path.Combine(directory, "desc"), installed: true);
            if (data is null || string.IsNullOrEmpty(data.Name)) continue;
            data.Files.AddRange(ReadFilesFile(Path.Combine(directory, "files")));
            localPackages[data.Name] = data;
        }
    }

    private PackageData? QuerySyncPackage(string packageName)
    {
        if (!PackageNamePattern.IsMatch(packageName)) return null;
        var result = RunPacman(new[] { "-Si", packageName });
        if (!result.Succeeded) return null;
        var data = ParseKeyValueOutput(result.StandardOutput);
        if (data is null) return null;
        data.Name = data.Name.Length == 0 ? packageName : data.Name;
        return data;
    }

    private IEnumerable<PackageData> SearchPacman(string searchString)
    {
        var result = RunPacman(new[] { "-Ss", searchString });
        if (!result.Succeeded) return Array.Empty<PackageData>();
        var values = new List<PackageData>();
        string? previousDescription = null;
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var match = PacmanSearchLine.Match(line.TrimEnd());
            if (match.Success)
            {
                var data = new PackageData
                {
                    Name = match.Groups["name"].Value,
                    Version = match.Groups["version"].Value,
                    Repository = match.Groups["repo"].Value,
                    Description = null,
                    IsInstalled = match.Groups["state"].Value.Contains("installed", StringComparison.OrdinalIgnoreCase)
                };
                values.Add(data);
                syncPackages[data.Name] = data;
                previousDescription = null;
            }
            else if (values.Count > 0 && line.StartsWith("    ", StringComparison.Ordinal))
            {
                previousDescription = line.Trim();
                values[^1].Description = previousDescription;
            }
        }
        return values;
    }

    private PackageData? ParseKeyValueOutput(string output)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) { key = null; continue; }
            var separator = line.IndexOf(':');
            if (separator >= 0)
            {
                key = line[..separator].Trim();
                values[key] = new List<string> { line[(separator + 1)..].Trim() };
            }
            else if (key is not null) values[key].Add(line.Trim());
        }
        if (!values.ContainsKey("Name")) return null;
        var data = new PackageData
        {
            Name = First(values, "Name"),
            Version = First(values, "Version"),
            Description = First(values, "Description"),
            Repository = First(values, "Repository"),
            Url = First(values, "URL"),
            License = Join(values, "Licenses"),
            Packager = First(values, "Packager"),
            InstalledSize = ParseSize(First(values, "Installed Size")),
            DownloadSize = ParseSize(First(values, "Download Size")),
            BuildDate = ParseDate(First(values, "Build Date"))
        };
        Add(values, "Groups", data.Groups);
        Add(values, "Depends On", data.Depends);
        Add(values, "Optional Deps", data.OptionalDependencies);
        Add(values, "Make Deps", data.MakeDependencies);
        Add(values, "Check Deps", data.CheckDependencies);
        Add(values, "Provides", data.Provides);
        Add(values, "Conflicts With", data.Conflicts);
        Add(values, "Replaces", data.Replaces);
        return data;
    }

    private static PackageData? ParsePackageFile(string path, bool installed)
    {
        if (!File.Exists(path)) return null;
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('%') && line.EndsWith('%'))
            {
                key = line.Trim('%');
                values[key] = new List<string>();
            }
            else if (key is not null && line.Length > 0) values[key].Add(line);
        }
        if (!values.ContainsKey("NAME")) return null;
        var data = new PackageData
        {
            Name = First(values, "NAME"),
            Version = First(values, "VERSION"),
            Description = First(values, "DESC"),
            Url = First(values, "URL"),
            License = Join(values, "LICENSE"),
            Packager = First(values, "PACKAGER"),
            InstalledSize = ParseUInt64(First(values, "ISIZE")),
            DownloadSize = ParseUInt64(First(values, "CSIZE")),
            BuildDate = UnixDate(First(values, "BUILDDATE")),
            InstallDate = UnixDate(First(values, "INSTALLDATE")),
            IsInstalled = installed,
            ExplicitlyInstalled = !values.TryGetValue("REASON", out var reason) || First(values, "REASON") != "1"
        };
        Add(values, "GROUPS", data.Groups);
        Add(values, "DEPENDS", data.Depends);
        Add(values, "OPTDEPENDS", data.OptionalDependencies);
        Add(values, "MAKEDEPENDS", data.MakeDependencies);
        Add(values, "CHECKDEPENDS", data.CheckDependencies);
        Add(values, "PROVIDES", data.Provides);
        Add(values, "REPLACES", data.Replaces);
        Add(values, "CONFLICTS", data.Conflicts);
        Add(values, "BACKUP", data.Backups, prefix: "/");
        Add(values, "VALIDATION", data.Validations);
        return data;
    }

    private static IReadOnlyList<string> ReadFilesFile(string path)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        var values = new List<string>();
        var inFiles = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line == "%FILES%") { inFiles = true; continue; }
            if (inFiles && line.Length == 0) break;
            if (inFiles && !line.EndsWith('/')) values.Add(line.StartsWith('/') ? line : "/" + line);
        }
        return values;
    }

    private PacmanPackage BuildPackage(string name, PackageData? local, PackageData? syncData)
    {
        if (packageCache.TryGetValue(name, out var cached) && (syncData is null || cached.Version == syncData.Version)) return cached;
        var data = syncData ?? local ?? new PackageData { Name = name };
        AppInfo? app = null;
        if (Config.EnableAppstream)
            app = Appstream.GetPackageNameApps(name).FirstOrDefault();
        var package = new PacmanPackage(data, local, syncData, this, app);
        packageCache[name] = package;
        return package;
    }

    private AurPackage MakeAurPackage(AURInfos info)
    {
        lock (sync)
        {
            var local = localPackages.GetValueOrDefault(info.Name);
            return new AurPackage(info, local, this);
        }
    }

    private void EnsureAur()
    {
        if (!Config.SupportAur) throw new InvalidOperationException("AUR support is not available.");
        if (Aur is not null) return;
        Aur = new AURClient(httpClient);
        Aur.SetRealBuildDirectory(Config.AURBuildDirectory);
    }

    internal CommandResult ExecutePacman(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
        => RunPacman(arguments, cancellationToken);

    internal Task<int> ExecutePacmanStreamingAsync(IEnumerable<string> arguments, Action<string>? output, Action<string>? error, CancellationToken cancellationToken = default)
    {
        var values = new List<string>();
        if (!string.Equals(Config.AlpmConfig.ConfigurationPath, "/etc/pacman.conf", StringComparison.Ordinal))
        {
            values.Add("--config");
            values.Add(Config.AlpmConfig.ConfigurationPath);
        }
        values.AddRange(arguments);
        var environment = new Dictionary<string, string?>(Config.EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
        {
            ["HTTP_USER_AGENT"] = Utils.GetUserAgent()
        };
        return ProcessRunner.RunStreamingAsync("pacman", values, output, error, environment: environment, cancellationToken: cancellationToken);
    }

    private CommandResult RunPacman(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var values = new List<string>();
        if (!string.Equals(Config.AlpmConfig.ConfigurationPath, "/etc/pacman.conf", StringComparison.Ordinal))
        {
            values.Add("--config");
            values.Add(Config.AlpmConfig.ConfigurationPath);
        }
        values.AddRange(arguments);
        var environment = new Dictionary<string, string?>(Config.EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
        {
            ["HTTP_USER_AGENT"] = Utils.GetUserAgent()
        };
        return ProcessRunner.Run("pacman", values, environment: environment, cancellationToken: cancellationToken);
    }

    private static bool Matches(PackageData package, IReadOnlyList<string> tokens)
        => tokens.All(token => package.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            (package.Description?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false) ||
            package.Provides.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            package.Groups.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)));

    private static bool Satisfies(PackageData package, string dependency, string dependencyName)
    {
        var provided = package.Name == dependencyName || package.Provides.Any(provide => AlpmVersionComparer.GetName(provide) == dependencyName);
        return provided && (dependency.IndexOfAny("<>=".ToCharArray()) < 0 || AlpmVersionComparer.Satisfies(package.Version, dependency));
    }

    private static bool Satisfies(PacmanPackage extract, string dependency, string dependencyName)
    {
        var provided = extract.Name == dependencyName || extract.Provides.Any(provide => AlpmVersionComparer.GetName(provide) == dependencyName);
        return provided && (dependency.IndexOfAny("<>=".ToCharArray()) < 0 || AlpmVersionComparer.Satisfies(extract.Version, dependency));
    }

    private static Regex GlobToRegex(string glob)
        => new("^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<string> Tokens(string value)
        => value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static IEnumerable<string> Lines(string value)
        => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string First(Dictionary<string, List<string>> values, string key)
        => values.TryGetValue(key, out var value) ? value.FirstOrDefault() ?? string.Empty : string.Empty;
    private static string Join(Dictionary<string, List<string>> values, string key)
        => values.TryGetValue(key, out var value) ? string.Join(' ', value) : string.Empty;
    private static void Add(Dictionary<string, List<string>> values, string key, ICollection<string> target, string prefix = "")
    {
        if (!values.TryGetValue(key, out var value)) return;
        foreach (var item in value.Where(item => item != "None")) target.Add(prefix + item);
    }
    private static ulong ParseUInt64(string value)
        => ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static ulong ParseSize(string value)
    {
        var number = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return 0;
        if (value.Contains("MiB", StringComparison.OrdinalIgnoreCase)) parsed *= 1024 * 1024;
        else if (value.Contains("KiB", StringComparison.OrdinalIgnoreCase)) parsed *= 1024;
        return (ulong)Math.Max(0, parsed);
    }
    private static DateTimeOffset? ParseDate(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return date;
        return UnixDate(value);
    }
    private static DateTimeOffset? UnixDate(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) && epoch > 0
            ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
    private static void ValidatePackageName(string value)
    {
        if (value is "." or ".." || !PackageNamePattern.IsMatch(value))
            throw new ArgumentException("Invalid package name.", nameof(value));
    }
    private string? FindLocalPackageDirectory(string packageName)
    {
        var direct = Path.Combine(Config.AlpmConfig.LocalDatabasePath, "local", packageName);
        if (Directory.Exists(direct)) return direct;
        return Directory.Exists(Path.Combine(Config.AlpmConfig.LocalDatabasePath, "local"))
            ? Directory.EnumerateDirectories(Path.Combine(Config.AlpmConfig.LocalDatabasePath, "local"))
                .FirstOrDefault(path => Path.GetFileName(path).StartsWith(packageName + "-", StringComparison.Ordinal))
            : null;
    }
}
