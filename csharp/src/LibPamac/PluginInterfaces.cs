namespace Pamac
{
    /// <summary>Contract implemented by the pamac-aur plugin (equivalent of aur_interface.vala).</summary>
    public interface AURPlugin
    {
        void SetRealBuildDir(string configAurBuildDir);
        string GetRealBuildDir();
        AURInfos? GetInfos(string pkgname);
        List<AURInfos> GetMultiInfos(List<string> pkgnames);
        List<AURInfos> GetProviders(string depend);
        List<AURInfos> Search(string searchString);

        event Action<string, double>? EmitDownloadProgress;
        event Action<string>? EmitDownloadError;
    }

    /// <summary>Static metadata about an AUR package (equivalent of AURInfos in aur_interface.vala).</summary>
    public abstract class AURInfos
    {
        public abstract string Name { get; }
        public abstract string Version { get; }
        public abstract string? Desc { get; }
        public abstract string? License { get; }
        public abstract string? Url { get; }
        public abstract List<string> Groups { get; }
        public abstract List<string> Depends { get; }
        public abstract List<string> Optdepends { get; }
        public abstract List<string> Makedepends { get; }
        public abstract List<string> Checkdepends { get; }
        public abstract List<string> Provides { get; }
        public abstract List<string> Replaces { get; }
        public abstract List<string> Conflicts { get; }
        public abstract string? Packagebase { get; }
        public abstract string? Maintainer { get; }
        public abstract double Popularity { get; }
        public abstract DateTimeOffset? Lastmodified { get; }
        public abstract DateTimeOffset? Outofdate { get; }
        public abstract DateTimeOffset? Firstsubmitted { get; }
        public abstract ulong Numvotes { get; }
    }

    /// <summary>Contract implemented by the pamac-appstream plugin (equivalent of appstream_interface.vala).</summary>
    public interface AppstreamPlugin
    {
        void Load(List<string> reposNames);
        List<App> Search(string[] searchTokens);
        List<App> GetPkgnameApps(string pkgname);
        Dictionary<string, App> GetCategoryApps(string category);
    }

    /// <summary>One AppStream application (equivalent of App in appstream_interface.vala).</summary>
    public abstract class App
    {
        public abstract string? Name { get; }
        public abstract string? Id { get; }
        public abstract string? Pkgname { get; }
        public abstract string? Desc { get; }
        public abstract string? LongDesc { get; }
        public abstract string? Repo { get; }
        public abstract string? Launchable { get; }
        public abstract string? Icon { get; }
        public abstract List<string> Screenshots { get; }
    }

    /// <summary>Contract implemented by the pamac-snap plugin (equivalent of snap_interface.vala).</summary>
    public interface SnapPlugin
    {
        event Action<string, string, string, double>? EmitActionProgress;
        event Action<string, string, string, double>? EmitDownloadProgress;
        event Action<string, string>? EmitScriptOutput;
        event Action<string, string, string[]>? EmitError;
        event Action<string>? StartDownloading;
        event Action<string>? StopDownloading;

        void SearchSnaps(string searchString, ref List<SnapPackage> pkgs);
        bool IsInstalledSnap(string name);
        SnapPackage? GetSnap(string name);
        SnapPackage? GetSnapByAppId(string appId);
        void GetInstalledSnaps(ref List<SnapPackage> pkgs);
        string GetInstalledSnapIcon(string name);
        void GetCategorySnaps(string category, ref List<SnapPackage> pkgs);
        bool TransRun(string sender, List<string> toInstall, List<string> toRemove);
        bool SwitchChannel(string sender, string name, string channel);
        void TransCancel(string sender);
        void Refresh();
    }

    /// <summary>Base class for snaps (equivalent of SnapPackage in snap_interface.vala).</summary>
    public abstract class SnapPackage : Package
    {
        public abstract string? Channel { get; }
        public abstract string? Publisher { get; }
        public abstract string? Confined { get; }
        public abstract List<string> Channels { get; }

        internal SnapPackage() { }
    }

    /// <summary>Contract implemented by the pamac-flatpak plugin (equivalent of flatpak_interface.vala).</summary>
    public interface FlatpakPlugin
    {
        ulong RefreshPeriod { get; set; }

        event Action<string, string, string, double>? EmitActionProgress;
        event Action<string, string>? EmitScriptOutput;
        event Action<string, string, string[]>? EmitError;

        bool RefreshAppstreamData();
        void LoadAppstreamData();
        void GetRemotesNames(ref List<string> remotesNames);
        void SearchFlatpaks(string searchString, ref List<FlatpakPackage> pkgs);
        bool IsInstalledFlatpak(string name);
        FlatpakPackage? GetFlatpakByAppId(string appId);
        FlatpakPackage? GetFlatpak(string id);
        void GetInstalledFlatpaks(ref List<FlatpakPackage> pkgs);
        void GetCategoryFlatpaks(string category, ref List<FlatpakPackage> pkgs);
        void GetFlatpakUpdates(ref List<FlatpakPackage> pkgs);
        bool TransRun(string sender, List<string> toInstall, List<string> toRemove, List<string> toUpgrade);
        void TransCancel(string sender);
        void Refresh();
    }

    /// <summary>Base class for flatpaks (equivalent of FlatpakPackage in flatpak_interface.vala).</summary>
    public abstract class FlatpakPackage : Package
    {
        internal FlatpakPackage() { }
    }
}
