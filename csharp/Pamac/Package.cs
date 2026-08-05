namespace Pamac;

/// <summary>Common package information exposed by all Pamac backends.</summary>
public abstract class Package
{
    public abstract string Name { get; }
    public abstract string Id { get; }
    public abstract string? AppName { get; }
    public abstract string? AppId { get; }
    public abstract string Version { get; }
    public abstract string? InstalledVersion { get; }
    public abstract string? Description { get; }
    public abstract string? LongDescription { get; }
    public abstract string? Repository { get; }
    public abstract string? Launchable { get; }
    public abstract string? License { get; }
    public abstract string? Url { get; }
    public abstract string? Icon { get; }
    public abstract ulong InstalledSize { get; }
    public abstract ulong DownloadSize { get; }
    public abstract DateTimeOffset? InstallDate { get; }
    public abstract IReadOnlyList<string> Screenshots { get; }

    // Names used by the original GObject API, useful when porting a client.
    public string name => Name;
    public string id => Id;
    public string? app_name => AppName;
    public string? app_id => AppId;
    public string version => Version;
    public string? installed_version => InstalledVersion;
    public string? DescriptionText => Description;
    public string? Desc => Description;
    public string? desc => Description;
    public string? long_desc => LongDescription;
    public string? Repo => Repository;
    public string? repo => Repository;
    public string? launchable => Launchable;
    public string? license => License;
    public string? url => Url;
    public string? icon => Icon;
    public ulong installed_size => InstalledSize;
    public ulong download_size => DownloadSize;
    public DateTimeOffset? install_date => InstallDate;
    public IReadOnlyList<string> screenshots => Screenshots;
}

public abstract class AlpmPackage : Package
{
    public abstract DateTimeOffset? BuildDate { get; }
    public abstract string? Packager { get; }
    public abstract string? Reason { get; }
    public abstract IReadOnlyList<string> Validations { get; }
    public abstract IReadOnlyList<string> Groups { get; }
    public abstract IReadOnlyList<string> Depends { get; }
    public abstract IReadOnlyList<string> OptionalDependencies { get; }
    public abstract IReadOnlyList<string> MakeDependencies { get; }
    public abstract IReadOnlyList<string> CheckDependencies { get; }
    public abstract IReadOnlyList<string> RequiredBy { get; }
    public abstract IReadOnlyList<string> OptionalFor { get; }
    public abstract IReadOnlyList<string> Provides { get; }
    public abstract IReadOnlyList<string> Replaces { get; }
    public abstract IReadOnlyList<string> Conflicts { get; }
    public abstract IReadOnlyList<string> Backups { get; }
    public abstract IReadOnlyList<string> GetFiles();
    public abstract Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default);

    public DateTimeOffset? build_date => BuildDate;
    public string? packager => Packager;
    public string? reason => Reason;
    public IReadOnlyList<string> validations => Validations;
    public IReadOnlyList<string> groups => Groups;
    public IReadOnlyList<string> DependsOn => Depends;
    public IReadOnlyList<string> OptDepends => OptionalDependencies;
    public IReadOnlyList<string> MakeDepends => MakeDependencies;
    public IReadOnlyList<string> CheckDepends => CheckDependencies;
    public IReadOnlyList<string> depends => Depends;
    public IReadOnlyList<string> optdepends => OptionalDependencies;
    public IReadOnlyList<string> makedepends => MakeDependencies;
    public IReadOnlyList<string> checkdepends => CheckDependencies;
    public IReadOnlyList<string> requiredby => RequiredBy;
    public IReadOnlyList<string> optionalfor => OptionalFor;
    public IReadOnlyList<string> provides => Provides;
    public IReadOnlyList<string> replaces => Replaces;
    public IReadOnlyList<string> conflicts => Conflicts;
    public IReadOnlyList<string> backups => Backups;
    public IReadOnlyList<string> get_files() => GetFiles();
    public Task<IReadOnlyList<string>> get_files_async(CancellationToken cancellationToken = default) => GetFilesAsync(cancellationToken);
}

public abstract class AURPackage : AlpmPackage
{
    public abstract string? PackageBase { get; }
    public abstract string? Maintainer { get; }
    public abstract double Popularity { get; }
    public abstract DateTimeOffset? LastModified { get; }
    public abstract DateTimeOffset? OutOfDate { get; }
    public abstract DateTimeOffset? FirstSubmitted { get; }
    public abstract ulong NumVotes { get; }
    public AURInfos? Info { get; internal set; }

    public string? packagebase => PackageBase;
    public string? maintainer => Maintainer;
    public double popularity => Popularity;
    public DateTimeOffset? lastmodified => LastModified;
    public DateTimeOffset? outofdate => OutOfDate;
    public DateTimeOffset? firstsubmitted => FirstSubmitted;
    public ulong numvotes => NumVotes;
}

public abstract class FlatpakPackage : Package
{
}

public abstract class SnapPackage : Package
{
    public abstract string? Channel { get; }
    public abstract string? Publisher { get; }
    public abstract string? Confined { get; }
    public abstract IReadOnlyList<string> Channels { get; }

    public string? channel => Channel;
    public string? publisher => Publisher;
    public string? confined => Confined;
    public IReadOnlyList<string> channels => Channels;
}

/// <summary>Application metadata read from AppStream.</summary>
public sealed class AppInfo
{
    public string? Name { get; init; }
    public string? Id { get; init; }
    public string? PackageName { get; init; }
    public string? Summary { get; init; }
    public string? LongDescription { get; init; }
    public string? Repository { get; init; }
    public string? Launchable { get; init; }
    public string? Icon { get; init; }
    public IReadOnlyList<string> Screenshots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    public string? name => Name;
    public string? id => Id;
    public string? pkgname => PackageName;
    public string? desc => Summary;
    public string? long_desc => LongDescription;
    public string? repo => Repository;
    public string? launchable => Launchable;
    public string? icon => Icon;
    public IReadOnlyList<string> screenshots => Screenshots;
}

/// <summary>Metadata returned by the AUR RPC service.</summary>
public class AURInfos
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? License { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Depends { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OptionalDependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MakeDependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CheckDependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Provides { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Replaces { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();
    public string? PackageBase { get; init; }
    public string? Maintainer { get; init; }
    public double Popularity { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public DateTimeOffset? OutOfDate { get; init; }
    public DateTimeOffset? FirstSubmitted { get; init; }
    public ulong NumVotes { get; init; }

    public string name => Name;
    public string version => Version;
    public string? desc => Description;
    public string? license => License;
    public string? url => Url;
    public IReadOnlyList<string> groups => Groups;
    public IReadOnlyList<string> depends => Depends;
    public IReadOnlyList<string> optdepends => OptionalDependencies;
    public IReadOnlyList<string> makedepends => MakeDependencies;
    public IReadOnlyList<string> checkdepends => CheckDependencies;
    public IReadOnlyList<string> provides => Provides;
    public IReadOnlyList<string> replaces => Replaces;
    public IReadOnlyList<string> conflicts => Conflicts;
    public string? packagebase => PackageBase;
    public string? maintainer => Maintainer;
    public double popularity => Popularity;
    public DateTimeOffset? lastmodified => LastModified;
    public DateTimeOffset? outofdate => OutOfDate;
    public DateTimeOffset? firstsubmitted => FirstSubmitted;
    public ulong numvotes => NumVotes;
}

// A more idiomatic spelling for new C# callers.
public sealed class AURInfo : AURInfos
{
}

public sealed class TransactionSummary
{
    public List<Package> ToInstall { get; } = new();
    public List<Package> ToUpgrade { get; } = new();
    public List<Package> ToDowngrade { get; } = new();
    public List<Package> ToReinstall { get; } = new();
    public List<Package> ToRemove { get; } = new();
    public List<Package> ConflictsToRemove { get; } = new();
    public List<Package> ToBuild { get; } = new();
    public List<string> AURPackageBasesToBuild { get; } = new();
    public List<string> ToLoad { get; } = new();

    public IReadOnlyList<Package> to_install => ToInstall;
    public IReadOnlyList<Package> to_upgrade => ToUpgrade;
    public IReadOnlyList<Package> to_downgrade => ToDowngrade;
    public IReadOnlyList<Package> to_reinstall => ToReinstall;
    public IReadOnlyList<Package> to_remove => ToRemove;
    public IReadOnlyList<Package> conflicts_to_remove => ConflictsToRemove;
    public IReadOnlyList<Package> to_build => ToBuild;
}

public sealed class Updates
{
    public List<AlpmPackage> RepositoryUpdates { get; } = new();
    public List<AlpmPackage> IgnoredRepositoryUpdates { get; } = new();
    public List<AURPackage> AURUpdates { get; } = new();
    public List<AURPackage> IgnoredAURUpdates { get; } = new();
    public List<AURPackage> OutOfDate { get; } = new();
    public List<FlatpakPackage> FlatpakUpdates { get; } = new();

    public IReadOnlyList<AlpmPackage> repos_updates => RepositoryUpdates;
    public IReadOnlyList<AlpmPackage> ignored_repos_updates => IgnoredRepositoryUpdates;
    public IReadOnlyList<AURPackage> aur_updates => AURUpdates;
    public IReadOnlyList<AURPackage> ignored_aur_updates => IgnoredAURUpdates;
    public IReadOnlyList<AURPackage> outofdate => OutOfDate;
    public IReadOnlyList<FlatpakPackage> flatpak_updates => FlatpakUpdates;
}

internal sealed class PackageData
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? License { get; set; }
    public string? Packager { get; set; }
    public string? Repository { get; set; }
    public ulong InstalledSize { get; set; }
    public ulong DownloadSize { get; set; }
    public DateTimeOffset? BuildDate { get; set; }
    public DateTimeOffset? InstallDate { get; set; }
    public bool IsInstalled { get; set; }
    public bool ExplicitlyInstalled { get; set; }
    public List<string> Groups { get; } = new();
    public List<string> Depends { get; } = new();
    public List<string> OptionalDependencies { get; } = new();
    public List<string> MakeDependencies { get; } = new();
    public List<string> CheckDependencies { get; } = new();
    public List<string> Provides { get; } = new();
    public List<string> Replaces { get; } = new();
    public List<string> Conflicts { get; } = new();
    public List<string> Backups { get; } = new();
    public List<string> Validations { get; } = new();
    public List<string> Files { get; } = new();
}

public sealed class PacmanPackage : AlpmPackage
{
    private readonly PackageData data;
    private readonly PackageData? local;
    private readonly PackageData? sync;
    private readonly Database? database;
    private readonly AppInfo? app;
    private IReadOnlyList<string>? files;
    private IReadOnlyList<string>? requiredBy;
    private IReadOnlyList<string>? optionalFor;

    internal PacmanPackage(PackageData value, PackageData? localData, PackageData? syncData, Database? owner, AppInfo? appInfo)
    {
        data = value;
        local = localData;
        sync = syncData;
        database = owner;
        app = appInfo;
    }

    public override string Name => data.Name;
    public override string Id => app?.Id ?? data.Name;
    public override string? AppName => app?.Name;
    public override string? AppId => app?.Id;
    public override string Version => sync?.Version ?? data.Version;
    public override string? InstalledVersion => local?.Version;
    public override string? Description => app?.Summary ?? sync?.Description ?? data.Description;
    public override string? LongDescription => app?.LongDescription;
    public override string? Repository => sync?.Repository ?? data.Repository;
    public override string? Launchable => app?.Launchable;
    public override string? License => sync?.License ?? data.License;
    public override string? Url => sync?.Url ?? data.Url;
    public override string? Icon => app?.Icon;
    public override ulong InstalledSize => local?.InstalledSize ?? data.InstalledSize;
    public override ulong DownloadSize => sync?.DownloadSize ?? data.DownloadSize;
    public override DateTimeOffset? InstallDate => local?.InstallDate;
    public override IReadOnlyList<string> Screenshots => app?.Screenshots ?? Array.Empty<string>();
    public override DateTimeOffset? BuildDate => sync?.BuildDate ?? data.BuildDate;
    public override string? Packager => sync?.Packager ?? data.Packager;
    public override string? Reason => local is null ? null : local.ExplicitlyInstalled ? "Explicitly installed" : "Installed as a dependency for another package";
    public override IReadOnlyList<string> Validations => sync is not null && sync.Validations.Count > 0 ? sync.Validations : data.Validations;
    public override IReadOnlyList<string> Groups => sync is not null && sync.Groups.Count > 0 ? sync.Groups : data.Groups;
    public override IReadOnlyList<string> Depends => sync is not null && sync.Depends.Count > 0 ? sync.Depends : data.Depends;
    public override IReadOnlyList<string> OptionalDependencies => sync is not null && sync.OptionalDependencies.Count > 0 ? sync.OptionalDependencies : data.OptionalDependencies;
    public override IReadOnlyList<string> MakeDependencies => sync is not null && sync.MakeDependencies.Count > 0 ? sync.MakeDependencies : data.MakeDependencies;
    public override IReadOnlyList<string> CheckDependencies => sync is not null && sync.CheckDependencies.Count > 0 ? sync.CheckDependencies : data.CheckDependencies;
    public override IReadOnlyList<string> RequiredBy => requiredBy ??= database?.GetRequiredBy(Name) ?? Array.Empty<string>();
    public override IReadOnlyList<string> OptionalFor => optionalFor ??= database?.GetOptionalFor(Name) ?? Array.Empty<string>();
    public override IReadOnlyList<string> Provides => sync is not null && sync.Provides.Count > 0 ? sync.Provides : data.Provides;
    public override IReadOnlyList<string> Replaces => sync is not null && sync.Replaces.Count > 0 ? sync.Replaces : data.Replaces;
    public override IReadOnlyList<string> Conflicts => sync is not null && sync.Conflicts.Count > 0 ? sync.Conflicts : data.Conflicts;
    public override IReadOnlyList<string> Backups => local is not null && local.Backups.Count > 0 ? local.Backups : data.Backups;

    public override IReadOnlyList<string> GetFiles()
        => files ??= local is not null ? database?.GetPackageFiles(Name) ?? local.Files : Array.Empty<string>();

    public override async Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        if (files is not null) return files;
        files = local is not null
            ? await (database?.GetPackageFilesAsync(Name, cancellationToken) ?? Task.FromResult<IReadOnlyList<string>>(local.Files)).ConfigureAwait(false)
            : Array.Empty<string>();
        return files;
    }

    internal PackageData Data => data;
    internal PackageData? LocalData => local;
    internal PackageData? SyncData => sync;
}

public sealed class AurPackage : AURPackage
{
    private readonly AURInfos info;
    private readonly PackageData? local;
    private readonly Database? database;
    private IReadOnlyList<string>? files;
    private IReadOnlyList<string>? requiredBy;
    private IReadOnlyList<string>? optionalFor;

    internal AurPackage(AURInfos aurInfo, PackageData? localData, Database? owner)
    {
        info = aurInfo;
        local = localData;
        database = owner;
        Info = aurInfo;
    }

    public override string Name => info.Name;
    public override string Id => info.Name;
    public override string? AppName => null;
    public override string? AppId => null;
    public override string Version => info.Version;
    public override string? InstalledVersion => local?.Version;
    public override string? Description => info.Description ?? local?.Description;
    public override string? LongDescription => null;
    public override string? Repository => "AUR";
    public override string? Launchable => null;
    public override string? License => info.License ?? local?.License;
    public override string? Url => info.Url ?? local?.Url;
    public override string? Icon => null;
    public override ulong InstalledSize => local?.InstalledSize ?? 0;
    public override ulong DownloadSize => local?.DownloadSize ?? 0;
    public override DateTimeOffset? InstallDate => local?.InstallDate;
    public override IReadOnlyList<string> Screenshots => Array.Empty<string>();
    public override DateTimeOffset? BuildDate => local?.BuildDate;
    public override string? Packager => local?.Packager;
    public override string? Reason => local is null ? null : local.ExplicitlyInstalled ? "Explicitly installed" : "Installed as a dependency for another package";
    public override IReadOnlyList<string> Validations => local?.Validations ?? Array.Empty<string>();
    public override IReadOnlyList<string> Groups => info.Groups;
    public override IReadOnlyList<string> Depends => info.Depends;
    public override IReadOnlyList<string> OptionalDependencies => info.OptionalDependencies;
    public override IReadOnlyList<string> MakeDependencies => info.MakeDependencies;
    public override IReadOnlyList<string> CheckDependencies => info.CheckDependencies;
    public override IReadOnlyList<string> RequiredBy => requiredBy ??= database?.GetRequiredBy(Name) ?? Array.Empty<string>();
    public override IReadOnlyList<string> OptionalFor => optionalFor ??= database?.GetOptionalFor(Name) ?? Array.Empty<string>();
    public override IReadOnlyList<string> Provides => info.Provides;
    public override IReadOnlyList<string> Replaces => info.Replaces;
    public override IReadOnlyList<string> Conflicts => info.Conflicts;
    public override IReadOnlyList<string> Backups => local?.Backups ?? Array.Empty<string>();
    public override string? PackageBase => info.PackageBase;
    public override string? Maintainer => info.Maintainer;
    public override double Popularity => info.Popularity;
    public override DateTimeOffset? LastModified => info.LastModified;
    public override DateTimeOffset? OutOfDate => info.OutOfDate;
    public override DateTimeOffset? FirstSubmitted => info.FirstSubmitted;
    public override ulong NumVotes => info.NumVotes;

    public override IReadOnlyList<string> GetFiles()
        => files ??= local is not null ? database?.GetPackageFiles(Name) ?? local.Files : Array.Empty<string>();

    public override async Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        if (files is not null) return files;
        files = local is not null
            ? await (database?.GetPackageFilesAsync(Name, cancellationToken) ?? Task.FromResult<IReadOnlyList<string>>(local.Files)).ConfigureAwait(false)
            : Array.Empty<string>();
        return files;
    }
}
