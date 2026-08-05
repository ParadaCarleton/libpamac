namespace Pamac
{
    /// <summary>Abstract base for all package representations (equivalent of package.vala).</summary>
    public abstract class Package
    {
        public abstract string Name { get; internal set; }
        public abstract string Id { get; internal set; }
        public abstract string? AppName { get; }
        public abstract string? AppId { get; }
        public abstract string Version { get; internal set; }
        public abstract string? InstalledVersion { get; internal set; }
        public abstract string? Desc { get; internal set; }
        public abstract string? LongDesc { get; }
        public abstract string? Repo { get; internal set; }
        public abstract string? Launchable { get; }
        public abstract string? License { get; }
        public abstract string? Url { get; }
        public abstract string? Icon { get; }
        public abstract ulong InstalledSize { get; }
        public abstract ulong DownloadSize { get; }
        public abstract DateTimeOffset? InstallDate { get; }
        public abstract List<string> Screenshots { get; }

        internal Package() { }
    }

    /// <summary>Summary of a pending transaction (equivalent of TransactionSummary in alpm_package.vala).</summary>
    public class TransactionSummary
    {
        public List<Package> ToInstall { get; internal set; } = new();
        public List<Package> ToUpgrade { get; internal set; } = new();
        public List<Package> ToDowngrade { get; internal set; } = new();
        public List<Package> ToReinstall { get; internal set; } = new();
        public List<Package> ToRemove { get; internal set; } = new();
        public List<Package> ConflictsToRemove { get; internal set; } = new();
        public List<Package> ToBuild { get; internal set; } = new();
        public List<string> AurPkgbasesToBuild { get; internal set; } = new();
        public List<string> ToLoad { get; internal set; } = new();

        internal TransactionSummary() { }
    }

    /// <summary>Aggregate of available updates (equivalent of Updates in alpm_package.vala).</summary>
    public class Updates
    {
        public List<AlpmPackage> ReposUpdates { get; internal set; } = new();
        public List<AlpmPackage> IgnoredReposUpdates { get; internal set; } = new();
        public List<AURPackage> AurUpdates { get; internal set; } = new();
        public List<AURPackage> IgnoredAurUpdates { get; internal set; } = new();
        public List<AURPackage> Outofdate { get; internal set; } = new();
        public List<FlatpakPackage> FlatpakUpdates { get; internal set; } = new();

        internal Updates() { }
    }
}
