using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pamac
{
    /// <summary>Parses /etc/pamac.conf and manages the plugin backends (equivalent of pamac_config.vala).</summary>
    public class Config
    {
        private readonly Dictionary<string, string> _environmentVariablesPriv = new();
        private DaemonProxy? _systemDaemon;
        private bool _supportAur;
        private bool _supportAppstream;
        private bool _supportSnap;
        private bool _supportFlatpak;
        private bool _enableAur;
        private bool _enableAppstream;
        private bool _enableSnap;
        private bool _enableFlatpak;
        private bool _checkAurUpdates;
        private bool _downloadUpdates;
        private PluginLoader<AURPlugin> _aurPluginLoader = null!;
        private PluginLoader<AppstreamPlugin> _appstreamPluginLoader = null!;
        private PluginLoader<SnapPlugin> _snapPluginLoader = null!;
        private PluginLoader<FlatpakPlugin> _flatpakPluginLoader = null!;

        public string ConfPath { get; }
        public bool Recurse { get; set; }
        public bool KeepBuiltPkgs { get; set; }
        public bool EnableDowngrade { get; set; }
        public bool SimpleInstall { get; set; }
        public ulong RefreshPeriod { get; set; }
        public bool NoUpdateHideIcon { get; set; }

        public bool SupportAur
        {
            get => _supportAur;
            private set
            {
                _supportAur = value;
                if (!_supportAur) EnableAur = false;
            }
        }
        public bool EnableAur
        {
            get => _enableAur;
            set
            {
                _enableAur = _supportAur && value;
                if (!_enableAur) CheckAurUpdates = false;
            }
        }
        public bool SupportAppstream
        {
            get => _supportAppstream;
            private set
            {
                _supportAppstream = value;
                if (!_supportAppstream) EnableAppstream = false;
            }
        }
        public bool EnableAppstream
        {
            get => _enableAppstream;
            set => _enableAppstream = _supportAppstream && value;
        }
        public bool SupportSnap
        {
            get => _supportSnap;
            private set
            {
                _supportSnap = value;
                if (!_supportSnap) EnableSnap = false;
            }
        }
        public bool EnableSnap
        {
            get => _enableSnap;
            set => _enableSnap = _supportSnap && value;
        }
        public bool SupportFlatpak
        {
            get => _supportFlatpak;
            set
            {
                _supportFlatpak = value;
                if (!_supportFlatpak) EnableFlatpak = false;
            }
        }
        public bool EnableFlatpak
        {
            get => _enableFlatpak;
            set
            {
                _enableFlatpak = _supportFlatpak && value;
                if (!_enableFlatpak) CheckFlatpakUpdates = false;
            }
        }
        public bool CheckFlatpakUpdates { get; set; }
        public string AurBuildDir { get; set; } = "/var/tmp";
        public bool CheckAurUpdates
        {
            get => _checkAurUpdates;
            set
            {
                _checkAurUpdates = value;
                if (!_checkAurUpdates) CheckAurVcsUpdates = false;
            }
        }
        public bool CheckAurVcsUpdates { get; set; }
        public bool DownloadUpdates
        {
            get => _downloadUpdates;
            set
            {
                _downloadUpdates = value;
                if (!_downloadUpdates) OfflineUpgrade = false;
            }
        }
        public bool OfflineUpgrade { get; set; }
        public ulong MaxParallelDownloads { get; set; }
        public ulong CleanKeepNumPkgs { get; set; }
        public bool CleanRmOnlyUninstalled { get; set; }
        public Dictionary<string, string> EnvironmentVariables => _environmentVariablesPriv;

        // Alpm config passthrough
        public string DbPath => AlpmConfig.Dbpath!;
        public bool Checkspace
        {
            get => AlpmConfig.Checkspace;
            set => AlpmConfig.Checkspace = value;
        }
        public HashSet<string> Ignorepkgs => AlpmConfig.Ignorepkgs;

        internal AlpmConfig AlpmConfig { get; private set; } = null!;

        public Config(string confPath)
        {
            ConfPath = confPath;
            // get environment variables
            AddEnvIfSet("http_proxy");
            AddEnvIfSet("https_proxy");
            AddEnvIfSet("ftp_proxy");
            AddEnvIfSet("socks_proxy");
            AddEnvIfSet("no_proxy");
            AlpmConfig = new AlpmConfig("/etc/pacman.conf");
            RefreshPeriod = 6;
            // load plugins
            SupportAur = false;
            _aurPluginLoader = new PluginLoader<AURPlugin>("pamac-aur");
            if (_aurPluginLoader.Load()) SupportAur = true;

            SupportAppstream = false;
            _appstreamPluginLoader = new PluginLoader<AppstreamPlugin>("pamac-appstream");
            if (_appstreamPluginLoader.Load()) SupportAppstream = true;

            SupportSnap = false;
            _snapPluginLoader = new PluginLoader<SnapPlugin>("pamac-snap");
            if (_snapPluginLoader.Load()) SupportSnap = true;

            SupportFlatpak = false;
            _flatpakPluginLoader = new PluginLoader<FlatpakPlugin>("pamac-flatpak");
            if (_flatpakPluginLoader.Load()) SupportFlatpak = true;

            Reload();
        }

        private void AddEnvIfSet(string name)
        {
            string? v = Environment.GetEnvironmentVariable(name);
            if (v != null) _environmentVariablesPriv[name] = v;
        }

        public void AddIgnorepkg(string name) => AlpmConfig.Ignorepkgs.Add(name);
        public void RemoveIgnorepkg(string name) => AlpmConfig.Ignorepkgs.Remove(name);

        public void Reload()
        {
            AlpmConfig.Reload();
            Recurse = false;
            KeepBuiltPkgs = false;
            EnableDowngrade = false;
            SimpleInstall = false;
            RefreshPeriod = 6;
            NoUpdateHideIcon = false;
            EnableAur = false;
            EnableAppstream = true;
            EnableSnap = false;
            EnableFlatpak = false;
            AurBuildDir = "/var/tmp";
            DownloadUpdates = false;
            MaxParallelDownloads = 1;
            CleanKeepNumPkgs = 3;
            CleanRmOnlyUninstalled = false;
            ParseFile(ConfPath);
            if (MaxParallelDownloads > 10) MaxParallelDownloads = 10;
            if (RefreshPeriod > 168) RefreshPeriod = 168;
        }

        internal AURPlugin? GetAurPlugin() => SupportAur ? _aurPluginLoader.GetPlugin() : null;
        internal AppstreamPlugin? GetAppstreamPlugin() => SupportAppstream ? _appstreamPluginLoader.GetPlugin() : null;
        internal SnapPlugin? GetSnapPlugin() => SupportSnap ? _snapPluginLoader.GetPlugin() : null;
        internal FlatpakPlugin? GetFlatpakPlugin() => SupportFlatpak ? _flatpakPluginLoader.GetPlugin() : null;

        private void ParseFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File '{path}' doesn't exist.");
                return;
            }
            try
            {
                foreach (string rawLine in File.ReadLines(path))
                {
                    string line = rawLine;
                    if (line.Length == 0) continue;
                    string[] splitted = line.Split(new[] { "#" }, 2, StringSplitOptions.None);
                    line = splitted[0].Trim();
                    if (line.Length == 0) continue;
                    splitted = line.Split(new[] { "=" }, 2, StringSplitOptions.None);
                    string key = splitted[0].Trim();
                    if (key == "RemoveUnrequiredDeps") Recurse = true;
                    else if (key == "EnableDowngrade") EnableDowngrade = true;
                    else if (key == "SimpleInstall") SimpleInstall = true;
                    else if (key == "RefreshPeriod")
                    {
                        if (splitted.Length == 2) RefreshPeriod = ulong.Parse(splitted[1].Trim());
                    }
                    else if (key == "KeepNumPackages")
                    {
                        if (splitted.Length == 2) CleanKeepNumPkgs = ulong.Parse(splitted[1].Trim());
                    }
                    else if (key == "OnlyRmUninstalled") CleanRmOnlyUninstalled = true;
                    else if (key == "NoUpdateHideIcon") NoUpdateHideIcon = true;
                    else if (key == "EnableAUR") EnableAur = true;
                    else if (key == "KeepBuiltPkgs") KeepBuiltPkgs = true;
                    else if (key == "EnableSnap") EnableSnap = true;
                    else if (key == "EnableFlatpak") EnableFlatpak = true;
                    else if (key == "CheckFlatpakUpdates") CheckFlatpakUpdates = true;
                    else if (key == "BuildDirectory")
                    {
                        if (splitted.Length == 2) AurBuildDir = splitted[1].Trim();
                    }
                    else if (key == "CheckAURUpdates") CheckAurUpdates = true;
                    else if (key == "CheckAURVCSUpdates") CheckAurVcsUpdates = true;
                    else if (key == "DownloadUpdates") DownloadUpdates = true;
                    else if (key == "OfflineUpgrade") OfflineUpgrade = true;
                    else if (key == "MaxParallelDownloads")
                    {
                        if (splitted.Length == 2) MaxParallelDownloads = ulong.Parse(splitted[1].Trim());
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        public void Save()
        {
            _systemDaemon ??= DaemonProxy.Connect();
            if (_systemDaemon == null)
            {
                Console.Error.WriteLine("save pamac config error: could not connect to daemon");
                return;
            }
            var newPamacConf = new Dictionary<string, object>
            {
                ["RemoveUnrequiredDeps"] = Recurse,
                ["RefreshPeriod"] = RefreshPeriod,
                ["NoUpdateHideIcon"] = NoUpdateHideIcon,
                ["DownloadUpdates"] = DownloadUpdates,
                ["OfflineUpgrade"] = OfflineUpgrade,
                ["EnableDowngrade"] = EnableDowngrade,
                ["SimpleInstall"] = SimpleInstall,
                ["MaxParallelDownloads"] = MaxParallelDownloads,
                ["KeepNumPackages"] = CleanKeepNumPkgs,
                ["OnlyRmUninstalled"] = CleanRmOnlyUninstalled,
                ["EnableAUR"] = EnableAur,
                ["KeepBuiltPkgs"] = KeepBuiltPkgs,
                ["CheckAURUpdates"] = CheckAurUpdates,
                ["CheckAURVCSUpdates"] = CheckAurVcsUpdates,
                ["BuildDirectory"] = AurBuildDir,
                ["EnableSnap"] = EnableSnap,
                ["EnableFlatpak"] = EnableFlatpak,
                ["CheckFlatpakUpdates"] = CheckFlatpakUpdates,
            };
            _systemDaemon.StartWritePamacConfig(newPamacConf);

            var newAlpmConf = new Dictionary<string, object>
            {
                ["CheckSpace"] = Checkspace,
                ["IgnorePkg"] = string.Join(" ", Ignorepkgs),
            };
            _systemDaemon.StartWriteAlpmConfig(newAlpmConf);
        }
    }
}
