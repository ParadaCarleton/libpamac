namespace Pamac;

/// <summary>Reads pamac.conf and owns the managed package backends.</summary>
public sealed class Config
{
    private bool supportAur;
    private bool supportAppstream;
    private bool supportSnap;
    private bool supportFlatpak;
    private bool enableAur;
    private bool enableAppstream;
    private bool enableSnap;
    private bool enableFlatpak;
    private bool checkAurUpdates;
    private bool downloadUpdates;

    public Config(string confPath = "/etc/pamac.conf", string? pacmanConfPath = null)
    {
        ConfPath = confPath;
        EnvironmentVariables = ReadProxyEnvironment();
        AlpmConfig = new AlpmConfig(pacmanConfPath ?? "/etc/pacman.conf");

        // These are managed implementations, so AUR and AppStream do not need
        // a GObject module to be installed. Snap/Flatpak remain optional and
        // are enabled only when their native command is available.
        supportAur = true;
        supportAppstream = true;
        supportSnap = ProcessRunner.IsAvailable("snap");
        supportFlatpak = ProcessRunner.IsAvailable("flatpak");
        Reload();
    }

    public string ConfPath { get; }
    public AlpmConfig AlpmConfig { get; }
    public IDictionary<string, string> EnvironmentVariables { get; }
    public bool Recurse { get; set; }
    public bool KeepBuiltPackages { get; set; }
    public bool EnableDowngrade { get; set; }
    public bool SimpleInstall { get; set; }
    public ulong RefreshPeriod { get; set; }
    public bool NoUpdateHideIcon { get; set; }

    public bool SupportAur => supportAur;
    public bool EnableAur
    {
        get => enableAur;
        set
        {
            enableAur = supportAur && value;
            if (!enableAur) CheckAurUpdates = false;
        }
    }

    public bool SupportAppstream => supportAppstream;
    public bool EnableAppstream
    {
        get => enableAppstream;
        set => enableAppstream = supportAppstream && value;
    }

    public bool SupportSnap => supportSnap;
    public bool EnableSnap
    {
        get => enableSnap;
        set => enableSnap = supportSnap && value;
    }

    public bool SupportFlatpak => supportFlatpak;
    public bool EnableFlatpak
    {
        get => enableFlatpak;
        set
        {
            enableFlatpak = supportFlatpak && value;
            if (!enableFlatpak) CheckFlatpakUpdates = false;
        }
    }

    public bool CheckFlatpakUpdates { get; set; }
    public string AURBuildDirectory { get; set; } = "/var/tmp";
    public bool CheckAurUpdates
    {
        get => checkAurUpdates;
        set
        {
            checkAurUpdates = value;
            if (!checkAurUpdates) CheckAurVcsUpdates = false;
        }
    }

    public bool CheckAurVcsUpdates { get; set; }
    public bool DownloadUpdates
    {
        get => downloadUpdates;
        set
        {
            downloadUpdates = value;
            if (!downloadUpdates) OfflineUpgrade = false;
        }
    }

    public bool OfflineUpgrade { get; set; }
    public ulong MaxParallelDownloads { get; set; }
    public ulong CleanKeepNumberOfPackages { get; set; }
    public bool CleanRemoveOnlyUninstalled { get; set; }
    public bool CheckSpace
    {
        get => AlpmConfig.CheckSpace;
        set => AlpmConfig.CheckSpace = value;
    }
    public ISet<string> IgnorePackages => AlpmConfig.IgnorePackages;

    // Lower-case aliases make mechanical ports of the original Vala examples
    // less surprising while the primary API follows .NET naming conventions.
    public string conf_path => ConfPath;
    public bool recurse { get => Recurse; set => Recurse = value; }
    public bool keep_built_pkgs { get => KeepBuiltPackages; set => KeepBuiltPackages = value; }
    public bool enable_downgrade { get => EnableDowngrade; set => EnableDowngrade = value; }
    public bool simple_install { get => SimpleInstall; set => SimpleInstall = value; }
    public ulong refresh_period { get => RefreshPeriod; set => RefreshPeriod = value; }
    public bool no_update_hide_icon { get => NoUpdateHideIcon; set => NoUpdateHideIcon = value; }
    public bool support_aur => SupportAur;
    public bool enable_aur { get => EnableAur; set => EnableAur = value; }
    public bool support_appstream => SupportAppstream;
    public bool enable_appstream { get => EnableAppstream; set => EnableAppstream = value; }
    public bool support_snap => SupportSnap;
    public bool enable_snap { get => EnableSnap; set => EnableSnap = value; }
    public bool support_flatpak => SupportFlatpak;
    public bool enable_flatpak { get => EnableFlatpak; set => EnableFlatpak = value; }
    public string aur_build_dir { get => AURBuildDirectory; set => AURBuildDirectory = value; }
    public bool check_aur_updates { get => CheckAurUpdates; set => CheckAurUpdates = value; }
    public bool check_aur_vcs_updates { get => CheckAurVcsUpdates; set => CheckAurVcsUpdates = value; }
    public bool download_updates { get => DownloadUpdates; set => DownloadUpdates = value; }
    public bool offline_upgrade { get => OfflineUpgrade; set => OfflineUpgrade = value; }
    public ulong max_parallel_downloads { get => MaxParallelDownloads; set => MaxParallelDownloads = value; }
    public ulong clean_keep_num_pkgs { get => CleanKeepNumberOfPackages; set => CleanKeepNumberOfPackages = value; }
    public bool clean_rm_only_uninstalled { get => CleanRemoveOnlyUninstalled; set => CleanRemoveOnlyUninstalled = value; }
    public string db_path => AlpmConfig.DbPath ?? string.Empty;
    public bool checkspace { get => CheckSpace; set => CheckSpace = value; }
    public ISet<string> ignorepkgs => IgnorePackages;

    public void AddIgnorePackage(string packageName) => AlpmConfig.IgnorePackages.Add(packageName);
    public void RemoveIgnorePackage(string packageName) => AlpmConfig.IgnorePackages.Remove(packageName);
    public void add_ignorepkg(string packageName) => AddIgnorePackage(packageName);
    public void remove_ignorepkg(string packageName) => RemoveIgnorePackage(packageName);

    public void Reload()
    {
        AlpmConfig.Reload();
        Recurse = false;
        KeepBuiltPackages = false;
        EnableDowngrade = false;
        SimpleInstall = false;
        RefreshPeriod = 6;
        NoUpdateHideIcon = false;
        EnableAur = false;
        EnableAppstream = true;
        EnableSnap = false;
        EnableFlatpak = false;
        CheckFlatpakUpdates = false;
        AURBuildDirectory = "/var/tmp";
        CheckAurUpdates = false;
        CheckAurVcsUpdates = false;
        DownloadUpdates = false;
        OfflineUpgrade = false;
        MaxParallelDownloads = 1;
        CleanKeepNumberOfPackages = 3;
        CleanRemoveOnlyUninstalled = false;
        ParseFile(ConfPath);

        if (MaxParallelDownloads > 10) MaxParallelDownloads = 10;
        if (MaxParallelDownloads == 0) MaxParallelDownloads = 1;
        if (RefreshPeriod > 168) RefreshPeriod = 168;
    }

    public void reload() => Reload();
    public void save() => Save();

    public void Save()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(ConfPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var lines = File.Exists(ConfPath) ? File.ReadAllLines(ConfPath).ToList() : new List<string>();
        Rewrite(lines, "RemoveUnrequiredDeps", Recurse);
        Rewrite(lines, "EnableDowngrade", EnableDowngrade);
        Rewrite(lines, "SimpleInstall", SimpleInstall);
        Rewrite(lines, "NoUpdateHideIcon", NoUpdateHideIcon);
        Rewrite(lines, "EnableAUR", EnableAur);
        Rewrite(lines, "KeepBuiltPkgs", KeepBuiltPackages);
        Rewrite(lines, "EnableSnap", EnableSnap);
        Rewrite(lines, "EnableFlatpak", EnableFlatpak);
        Rewrite(lines, "CheckFlatpakUpdates", CheckFlatpakUpdates);
        Rewrite(lines, "CheckAURUpdates", CheckAurUpdates);
        Rewrite(lines, "CheckAURVCSUpdates", CheckAurVcsUpdates);
        Rewrite(lines, "DownloadUpdates", DownloadUpdates);
        Rewrite(lines, "OfflineUpgrade", OfflineUpgrade);
        Rewrite(lines, "OnlyRmUninstalled", CleanRemoveOnlyUninstalled);
        Rewrite(lines, "RefreshPeriod", RefreshPeriod);
        Rewrite(lines, "KeepNumPackages", CleanKeepNumberOfPackages);
        Rewrite(lines, "MaxParallelDownloads", MaxParallelDownloads);
        Rewrite(lines, "BuildDirectory", AURBuildDirectory);

        var temporary = ConfPath + ".tmp." + Environment.ProcessId;
        File.WriteAllLines(temporary, lines);
        File.Move(temporary, ConfPath, overwrite: true);
    }

    private void ParseFile(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var original in File.ReadLines(path))
        {
            var comment = original.IndexOf('#');
            var line = (comment >= 0 ? original[..comment] : original).Trim();
            if (line.Length == 0) continue;
            var split = line.IndexOf('=');
            var key = split >= 0 ? line[..split].Trim() : line;
            var value = split >= 0 ? line[(split + 1)..].Trim() : string.Empty;
            switch (key)
            {
                case "RemoveUnrequiredDeps": Recurse = true; break;
                case "EnableDowngrade": EnableDowngrade = true; break;
                case "SimpleInstall": SimpleInstall = true; break;
                case "RefreshPeriod": if (ulong.TryParse(value, out var refresh)) RefreshPeriod = refresh; break;
                case "KeepNumPackages": if (ulong.TryParse(value, out var keep)) CleanKeepNumberOfPackages = keep; break;
                case "OnlyRmUninstalled": CleanRemoveOnlyUninstalled = true; break;
                case "NoUpdateHideIcon": NoUpdateHideIcon = true; break;
                case "EnableAUR": EnableAur = true; break;
                case "KeepBuiltPkgs": KeepBuiltPackages = true; break;
                case "EnableSnap": EnableSnap = true; break;
                case "EnableFlatpak": EnableFlatpak = true; break;
                case "CheckFlatpakUpdates": CheckFlatpakUpdates = true; break;
                case "BuildDirectory": if (!string.IsNullOrWhiteSpace(value)) AURBuildDirectory = value; break;
                case "CheckAURUpdates": CheckAurUpdates = true; break;
                case "CheckAURVCSUpdates": CheckAurVcsUpdates = true; break;
                case "DownloadUpdates": DownloadUpdates = true; break;
                case "OfflineUpgrade": OfflineUpgrade = true; break;
                case "MaxParallelDownloads": if (ulong.TryParse(value, out var parallel)) MaxParallelDownloads = parallel; break;
            }
        }
    }

    private static IDictionary<string, string> ReadProxyEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in new[] { "http_proxy", "https_proxy", "ftp_proxy", "socks_proxy", "no_proxy" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value)) values[variable] = value;
        }
        return values;
    }

    private static void Rewrite(IList<string> lines, string key, object value)
    {
        var replacement = value switch
        {
            bool flag => flag ? key : "#" + key,
            _ => $"{key} = {value}"
        };
        for (var i = 0; i < lines.Count; i++)
        {
            var comment = lines[i].IndexOf('#');
            var stripped = (comment >= 0 ? lines[i][..comment] : lines[i]).Trim();
            if (stripped.StartsWith(key, StringComparison.Ordinal))
            {
                lines[i] = replacement;
                return;
            }
        }
        lines.Add(replacement);
    }
}
