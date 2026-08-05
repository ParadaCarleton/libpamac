using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pamac.LibAlpm;

namespace Pamac
{
    /// <summary>One repository block parsed from pacman.conf.</summary>
    public class AlpmRepo
    {
        public string Name;
        public SignatureLevel Siglevel;
        public SignatureLevel SiglevelMask;
        public DBUsage Usage;
        public List<string> Urls = new();

        public AlpmRepo(string name)
        {
            Name = name;
            Siglevel = SignatureLevel.USE_DEFAULT;
            Usage = 0;
        }

        public static bool EqualName(AlpmRepo a, AlpmRepo b) => a.Name == b.Name;
    }

    /// <summary>Parses and applies /etc/pacman.conf to an alpm handle (equivalent of alpm_config.vala).</summary>
    public class AlpmConfig
    {
        private string _confPath;
        private string? _rootdir;
        public string? Dbpath;
        private string? _logfile;
        private string? _gpgdir;
        private string _downloadUser = "alpm";
        private bool _disableSandbox;
        private int _usesyslog;
        public bool Checkspace;
        private List<string> _architectures = new();
        private List<string> _cachedirs = new();
        private List<string> _hookdirs = new();
        private List<string> _ignoregroups = new();
        public HashSet<string> Ignorepkgs = new();
        private List<string> _noextracts = new();
        private List<string> _noupgrades = new();
        public HashSet<string> Holdpkgs = new();
        public HashSet<string> Syncfirsts = new();
        private SignatureLevel _siglevel;
        private SignatureLevel _localfilesiglevel;
        private SignatureLevel _remotefilesiglevel;
        private SignatureLevel _siglevelMask;
        private SignatureLevel _localfilesiglevelMask;
        private SignatureLevel _remotefilesiglevelMask;
        private List<AlpmRepo> _repoOrder = new();

        public AlpmConfig(string path)
        {
            _confPath = path;
            Reload();
        }

        public void Reload()
        {
            _architectures = new List<string>();
            _cachedirs = new List<string>();
            _hookdirs = new List<string>();
            _ignoregroups = new List<string>();
            Ignorepkgs.Clear();
            _noextracts = new List<string>();
            _noupgrades = new List<string>();
            Holdpkgs.Clear();
            Syncfirsts.Clear();
            _usesyslog = 0;
            Checkspace = false;
            _disableSandbox = false;
            _downloadUser = "alpm";
            _siglevel = SignatureLevel.PACKAGE | SignatureLevel.PACKAGE_OPTIONAL | SignatureLevel.DATABASE | SignatureLevel.DATABASE_OPTIONAL;
            _localfilesiglevel = SignatureLevel.USE_DEFAULT;
            _remotefilesiglevel = SignatureLevel.USE_DEFAULT;
            _repoOrder = new List<AlpmRepo>();
            ParseFile(_confPath);
            if (_rootdir != null)
            {
                if (Dbpath == null) Dbpath = Path.Combine(_rootdir, "var/lib/pacman/");
                if (_logfile == null) _logfile = Path.Combine(_rootdir, "var/log/pacman.log");
            }
            else
            {
                _rootdir = "/";
                if (Dbpath == null) Dbpath = "/var/lib/pacman/";
                if (_logfile == null) _logfile = "/var/log/pacman.log";
            }
            if (_cachedirs.Count == 0) _cachedirs.Add("/var/cache/pacman/pkg/");
            if (_hookdirs.Count == 0) _hookdirs.Add("/etc/pacman.d/hooks/");
            if (_gpgdir == null) _gpgdir = "/etc/pacman.d/gnupg/";
            if (_architectures.Count == 0) _architectures.Add(Utils.GetMachineArch());
            // add archlinux-keyring and manjaro-keyring to syncfirsts
            Syncfirsts.Add("archlinux-keyring");
            Syncfirsts.Add("manjaro-keyring");
        }

        public AlpmHandle? GetHandle(bool filesDb = false, bool tmpDb = false, bool copyDbs = true)
        {
            Errno error = Errno.OK;
            AlpmHandle? handle = null;
            if (tmpDb)
            {
                string tmpPath = $"/tmp/pamac-{Environment.UserName}";
                string tmpDbpath = $"{tmpPath}/dbs";
                string localdbPath = Path.Combine(Dbpath!, "local");
                string syncdbPath = Path.Combine(Dbpath!, "sync");
                Utils.RunCommandSync($"mkdir -p {tmpPath}", out _);
                Utils.RunCommandSync($"mkdir -p {tmpDbpath}", out _);
                Utils.RunCommandSync($"ln -sf {localdbPath} {tmpDbpath}", out _);
                if (copyDbs)
                {
                    Utils.RunCommandSync($"cp --preserve=timestamps -ru {syncdbPath} {tmpDbpath}", out _);
                }
                Utils.RunCommandSync($"rm -f {tmpDbpath}/sync/pamac_aur.db", out _);
                handle = AlpmHandle.Initialize(_rootdir!, tmpDbpath, out error);
                if (error == Errno.DB_VERSION)
                {
                    Utils.RunCommandSync("pacman-db-upgrade", out _);
                    handle = AlpmHandle.Initialize(_rootdir!, tmpDbpath, out error);
                }
            }
            else
            {
                handle = AlpmHandle.Initialize(_rootdir!, Dbpath!, out error);
                if (error == Errno.DB_VERSION)
                {
                    Utils.RunCommandSync("pacman-db-upgrade", out _);
                    handle = AlpmHandle.Initialize(_rootdir!, Dbpath!, out error);
                }
            }
            if (handle == null)
            {
                Console.Error.WriteLine($"Failed to initialize alpm library ({AlpmApi.Strerror(error)})");
                return null;
            }
            // define options
            if (filesDb) handle.Dbext = ".files";
            if (!tmpDb) handle.Logfile = _logfile;
            handle.Gpgdir = _gpgdir;
            handle.Usesyslog = _usesyslog;
            handle.Checkspace = Checkspace ? 1 : 0;
            handle.Defaultsiglevel = (int)_siglevel;
            _localfilesiglevel = MergeSiglevel(_siglevel, _localfilesiglevel, _localfilesiglevelMask);
            _remotefilesiglevel = MergeSiglevel(_siglevel, _remotefilesiglevel, _remotefilesiglevelMask);
            handle.Localfilesiglevel = (int)_localfilesiglevel;
            handle.Remotefilesiglevel = (int)_remotefilesiglevel;
            foreach (string arch in _architectures) handle.AddArchitecture(arch);
            foreach (string cachedir in _cachedirs) handle.AddCachedir(cachedir);
            foreach (string hookdir in _hookdirs) handle.AddHookdir(hookdir);
            foreach (string ignoregroup in _ignoregroups) handle.AddIgnoregroup(ignoregroup);
            foreach (string noextract in _noextracts) handle.AddNoextract(noextract);
            foreach (string noupgrade in _noupgrades) handle.AddNoupgrade(noupgrade);
            handle.Sandboxuser = _downloadUser;
            handle.DisableSandboxFilesystem = _disableSandbox ? 1 : 0;
            handle.DisableSandboxSyscalls = _disableSandbox ? 1 : 0;
            return handle;
        }

        public void RegisterSyncdbs(AlpmHandle handle)
        {
            foreach (AlpmRepo repo in _repoOrder)
            {
                repo.Siglevel = MergeSiglevel(_siglevel, repo.Siglevel, repo.SiglevelMask);
                AlpmDB db = handle.RegisterSyncdb(repo.Name, repo.Siglevel);
                foreach (string url in repo.Urls)
                {
                    db.AddServer(url.Replace("$repo", repo.Name).Replace("$arch", _architectures[0]));
                }
                db.Usage = repo.Usage == 0 ? DBUsage.ALL : repo.Usage;
            }
        }

        private void ParseFile(string path, string? section = null)
        {
            string? currentSection = section;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File '{path}' doesn't exist");
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
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        currentSection = line.Substring(1, line.Length - 2);
                        if (currentSection == null) continue;
                        if (currentSection != "options")
                        {
                            var repo = new AlpmRepo(currentSection);
                            if (!_repoOrder.Any(r => AlpmRepo.EqualName(r, repo)))
                                _repoOrder.Add(repo);
                        }
                        continue;
                    }
                    splitted = line.Split(new[] { "=" }, 2, StringSplitOptions.None);
                    string key = splitted[0].Trim();
                    string? val = null;
                    if (splitted.Length == 2) val = splitted[1].Trim();
                    if (key == "Include")
                    {
                        ParseFile(val!, currentSection);
                    }
                    if (currentSection == "options")
                    {
                        if (key == "RootDir") _rootdir = val;
                        else if (key == "DBPath") Dbpath = val;
                        else if (key == "CacheDir") foreach (string dir in val!.Split(' ')) _cachedirs.Add(dir);
                        else if (key == "HookDir") foreach (string dir in val!.Split(' ')) _hookdirs.Add(dir);
                        else if (key == "LogFile") _logfile = val;
                        else if (key == "GPGDir") _gpgdir = val;
                        else if (key == "Architecture")
                        {
                            foreach (string arch in val!.Split(' '))
                            {
                                if (arch == "auto") _architectures.Add(Utils.GetMachineArch());
                                else _architectures.Add(arch);
                            }
                        }
                        else if (key == "UseSysLog") _usesyslog = 1;
                        else if (key == "CheckSpace") Checkspace = true;
                        else if (key == "SigLevel") ProcessSiglevel(val!, ref _siglevel, ref _siglevelMask);
                        else if (key == "LocalFileSigLevel") ProcessSiglevel(val!, ref _localfilesiglevel, ref _localfilesiglevelMask);
                        else if (key == "RemoteFileSigLevel") ProcessSiglevel(val!, ref _remotefilesiglevel, ref _remotefilesiglevelMask);
                        else if (key == "HoldPkg") foreach (string name in val!.Split(' ')) Holdpkgs.Add(name);
                        else if (key == "SyncFirst") foreach (string name in val!.Split(' ')) Syncfirsts.Add(name);
                        else if (key == "IgnoreGroup") foreach (string name in val!.Split(' ')) _ignoregroups.Add(name);
                        else if (key == "IgnorePkg") foreach (string name in val!.Split(' ')) Ignorepkgs.Add(name);
                        else if (key == "NoExtract") foreach (string name in val!.Split(' ')) _noextracts.Add(name);
                        else if (key == "NoUpgrade") foreach (string name in val!.Split(' ')) _noupgrades.Add(name);
                        else if (key == "DownloadUser") _downloadUser = val!;
                    }
                    else
                    {
                        foreach (AlpmRepo repo in _repoOrder)
                        {
                            if (repo.Name == currentSection)
                            {
                                if (key == "Server") repo.Urls.Add(val!);
                                else if (key == "SigLevel") ProcessSiglevel(val!, ref repo.Siglevel, ref repo.SiglevelMask);
                                else if (key == "Usage") repo.Usage = DefineUsage(val!);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        public void Write(Dictionary<string, object> newConf)
        {
            if (!File.Exists(_confPath))
            {
                Console.Error.WriteLine($"File '{_confPath}' doesn't exist.");
                return;
            }
            try
            {
                var data = new System.Text.StringBuilder();
                foreach (string line in File.ReadLines(_confPath))
                {
                    if (line.Length == 0)
                    {
                        data.Append("\n");
                        continue;
                    }
                    if (line.Contains("IgnorePkg"))
                    {
                        if (newConf.ContainsKey("IgnorePkg"))
                        {
                            string val = newConf["IgnorePkg"] as string ?? "";
                            if (val == "") data.Append("#IgnorePkg   =\n");
                            else data.Append($"IgnorePkg   = {val}\n");
                            newConf["IgnorePkg"] = "";
                        }
                        else
                        {
                            data.Append(line); data.Append("\n");
                        }
                    }
                    else if (line.Contains("CheckSpace"))
                    {
                        if (newConf.ContainsKey("CheckSpace"))
                        {
                            bool val = (bool)newConf["CheckSpace"];
                            data.Append(val ? "CheckSpace\n" : "#CheckSpace\n");
                            newConf.Remove("CheckSpace");
                        }
                        else
                        {
                            data.Append(line); data.Append("\n");
                        }
                    }
                    else
                    {
                        data.Append(line); data.Append("\n");
                    }
                }
                File.WriteAllText(_confPath, data.ToString());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        private DBUsage DefineUsage(string confString)
        {
            DBUsage usage = 0;
            foreach (string directive in confString.Split(' '))
            {
                if (directive == "Sync") usage |= DBUsage.SYNC;
                else if (directive == "Search") usage |= DBUsage.SEARCH;
                else if (directive == "Install") usage |= DBUsage.INSTALL;
                else if (directive == "Upgrade") usage |= DBUsage.UPGRADE;
                else if (directive == "All") usage |= DBUsage.ALL;
            }
            return usage;
        }

        private void ProcessSiglevel(string confString, ref SignatureLevel siglevel, ref SignatureLevel siglevelMask)
        {
            foreach (string directive in confString.Split(' '))
            {
                bool affectPackage = false;
                bool affectDatabase = false;
                if (directive.Contains("Package")) affectPackage = true;
                else if (directive.Contains("Database")) affectDatabase = true;
                else { affectPackage = true; affectDatabase = true; }

                if (directive.Contains("Never"))
                {
                    if (affectPackage) { siglevel &= ~SignatureLevel.PACKAGE; siglevelMask |= SignatureLevel.PACKAGE; }
                    if (affectDatabase) { siglevel &= ~SignatureLevel.DATABASE; siglevelMask |= SignatureLevel.DATABASE; }
                }
                else if (directive.Contains("Optional"))
                {
                    if (affectPackage) { siglevel |= SignatureLevel.PACKAGE | SignatureLevel.PACKAGE_OPTIONAL; siglevelMask |= SignatureLevel.PACKAGE | SignatureLevel.PACKAGE_OPTIONAL; }
                    if (affectDatabase) { siglevel |= SignatureLevel.DATABASE | SignatureLevel.DATABASE_OPTIONAL; siglevelMask |= SignatureLevel.DATABASE | SignatureLevel.DATABASE_OPTIONAL; }
                }
                else if (directive.Contains("Required"))
                {
                    if (affectPackage) { siglevel |= SignatureLevel.PACKAGE; siglevelMask |= SignatureLevel.PACKAGE; siglevel &= ~SignatureLevel.PACKAGE_OPTIONAL; siglevelMask |= SignatureLevel.PACKAGE_OPTIONAL; }
                    if (affectDatabase) { siglevel |= SignatureLevel.DATABASE; siglevelMask |= SignatureLevel.DATABASE; siglevel &= ~SignatureLevel.DATABASE_OPTIONAL; siglevelMask |= SignatureLevel.DATABASE_OPTIONAL; }
                }
                else if (directive.Contains("TrustedOnly"))
                {
                    if (affectPackage) { siglevel &= ~(SignatureLevel.PACKAGE_MARGINAL_OK | SignatureLevel.PACKAGE_UNKNOWN_OK); siglevelMask |= SignatureLevel.PACKAGE_MARGINAL_OK | SignatureLevel.PACKAGE_UNKNOWN_OK; }
                    if (affectDatabase) { siglevel &= ~(SignatureLevel.DATABASE_MARGINAL_OK | SignatureLevel.DATABASE_UNKNOWN_OK); siglevelMask |= SignatureLevel.DATABASE_MARGINAL_OK | SignatureLevel.DATABASE_UNKNOWN_OK; }
                }
                else if (directive.Contains("TrustAll"))
                {
                    if (affectPackage) { siglevel |= SignatureLevel.PACKAGE_MARGINAL_OK | SignatureLevel.PACKAGE_UNKNOWN_OK; siglevelMask |= SignatureLevel.PACKAGE_MARGINAL_OK | SignatureLevel.PACKAGE_UNKNOWN_OK; }
                    if (affectDatabase) { siglevel |= SignatureLevel.DATABASE_MARGINAL_OK | SignatureLevel.DATABASE_UNKNOWN_OK; siglevelMask |= SignatureLevel.DATABASE_MARGINAL_OK | SignatureLevel.DATABASE_UNKNOWN_OK; }
                }
                else
                {
                    Console.Error.WriteLine($"unrecognized siglevel: {confString}");
                }
            }
            siglevel &= ~SignatureLevel.USE_DEFAULT;
        }

        private static SignatureLevel MergeSiglevel(SignatureLevel sigbase, SignatureLevel sigover, SignatureLevel sigmask)
        {
            return sigmask != 0 ? (sigover & sigmask) | (sigbase & ~sigmask) : sigover;
        }
    }
}
