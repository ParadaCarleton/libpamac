using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pamac.LibAlpm;

namespace Pamac
{
    /// <summary>
    /// High-level database / search / update API backed by libalpm
    /// (equivalent of database.vala). Plugin backends (AUR, AppStream, Snap,
    /// Flatpak) are used when the corresponding plugin is available.
    /// </summary>
    public class Database
    {
        private readonly AlpmConfig _alpmConfig;
        private AlpmHandle? _alpmHandle;
        private AlpmHandle? _filesHandle;
        private readonly Dictionary<string, AURPackageLinked> _aurVcsPkgs = new();
        private readonly Dictionary<string, AlpmPackageLinked> _pkgsCache = new();
        private readonly Dictionary<string, AURPackageLinked> _aurPkgsCache = new();
        private List<string>? _mirrorsCountries;
        private string _mirrorsChoosenCountry = "";
        private List<string>? _groupsNames;
        private List<string>? _reposNames;
        private List<string>? _categoriesNames;
        private AURPlugin? _aurPlugin;
        private AppstreamPlugin? _appstreamPlugin;
        private SnapPlugin? _snapPlugin;
        private FlatpakPlugin? _flatpakPlugin;
        private readonly object _configLock = new();

        public Config Config { get; }
        internal bool DbsMissing { get; private set; }

        public event Action<uint>? GetUpdatesProgress;
        public event Action<string>? EmitWarning;

        public Database(Config config)
        {
            Config = config;
            _alpmConfig = config.AlpmConfig;
            Utils.RunCommandSync("true", out _); // ensure Process helpers warmed
            string userAgent = Utils.GetUserAgent();
            Environment.SetEnvironmentVariable("HTTP_USER_AGENT", userAgent);
            lock (_configLock)
            {
                _alpmHandle = _alpmConfig.GetHandle();
                if (_alpmHandle != null)
                {
                    foreach (string name in config.Ignorepkgs)
                        _alpmHandle.AddIgnorepkg(name);
                    _alpmConfig.RegisterSyncdbs(_alpmHandle);
                    _filesHandle = _alpmConfig.GetHandle(filesDb: true);
                    _alpmConfig.RegisterSyncdbs(_filesHandle!);
                }
                _aurVcsPkgs.Clear();
                _pkgsCache.Clear();
                _aurPkgsCache.Clear();
            }
            // load aur plugin
            if (config.SupportAur)
            {
                _aurPlugin = config.GetAurPlugin();
                if (_aurPlugin == null) config.EnableAur = false;
                else _aurPlugin.SetRealBuildDir(config.AurBuildDir);
            }
            // load appstream plugin
            if (config.SupportAppstream)
            {
                _appstreamPlugin = config.GetAppstreamPlugin();
                if (_appstreamPlugin == null) config.EnableAppstream = false;
                else if (config.EnableAppstream) _appstreamPlugin.Load(GetReposNames());
            }
            // load snap plugin
            if (config.SupportSnap)
            {
                _snapPlugin = config.GetSnapPlugin();
                if (_snapPlugin == null) config.EnableSnap = false;
            }
            // load flatpak plugin
            if (config.SupportFlatpak)
            {
                _flatpakPlugin = config.GetFlatpakPlugin();
                if (_flatpakPlugin == null) config.EnableFlatpak = false;
                else
                {
                    _flatpakPlugin.RefreshPeriod = config.RefreshPeriod;
                    if (config.EnableFlatpak) _flatpakPlugin.LoadAppstreamData();
                }
            }
        }

        internal AlpmHandle? GetHandleForDownload() => _alpmHandle;

        internal void Refresh()
        {
            lock (_configLock)
            {
                _alpmConfig.Reload();
                _alpmHandle = _alpmConfig.GetHandle();
                if (_alpmHandle != null)
                {
                    foreach (string name in Config.Ignorepkgs)
                        _alpmHandle.AddIgnorepkg(name);
                    _alpmConfig.RegisterSyncdbs(_alpmHandle);
                    _filesHandle = _alpmConfig.GetHandle(filesDb: true);
                    _alpmConfig.RegisterSyncdbs(_filesHandle!);
                }
                _aurVcsPkgs.Clear();
                _pkgsCache.Clear();
                _aurPkgsCache.Clear();
            }
            if (Config.EnableSnap) _snapPlugin?.Refresh();
            if (Config.EnableFlatpak) _flatpakPlugin?.Refresh();
        }

        internal AlpmHandle? GetTmpHandle()
        {
            lock (_configLock)
            {
                var tmp = _alpmConfig.GetHandle(tmpDb: true);
                if (tmp != null) _alpmConfig.RegisterSyncdbs(tmp);
                return tmp;
            }
        }

        public string GetAlpmDepName(string depString) => AlpmDepend.FromString(depString).Name!;

        public string GetRealAurBuildDir() => _aurPlugin?.GetRealBuildDir() ?? "";

        internal List<AURInfos> GetAurProviders(string pkgname) => _aurPlugin?.GetProviders(pkgname) ?? new List<AURInfos>();

        public bool IsInstalledPkg(string pkgname)
        {
            lock (_configLock) return _alpmHandle!.LocalDb.GetPkg(pkgname) != null;
        }

        internal AlpmPkg? InternGetLocalPkg(string pkgname)
        {
            lock (_configLock) return _alpmHandle!.LocalDb.GetPkg(pkgname);
        }

        public AlpmPackage? GetInstalledPkg(string pkgname)
        {
            lock (_configLock) return InitializePkg(_alpmHandle!.LocalDb.GetPkg(pkgname), null);
        }

        public bool HasInstalledSatisfier(string depstring)
        {
            lock (_configLock) return AlpmPkg.FindSatisfier(_alpmHandle!.LocalDb.Pkgcache, depstring) != null;
        }

        public AlpmPackage? GetInstalledSatisfier(string depstring)
        {
            lock (_configLock) return InitializePkg(AlpmPkg.FindSatisfier(_alpmHandle!.LocalDb.Pkgcache, depstring), null);
        }

        public List<AlpmPackage> GetInstalledPkgsByGlob(string glob)
        {
            var pkgs = new List<AlpmPackage>();
            lock (_configLock)
            {
                foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                {
                    var localPkg = AlpmPkg.FromPointer(p)!;
                    if (MatchesGlob(glob, localPkg.Name))
                        pkgs.Add(InitializePkg(localPkg, null)!);
                }
            }
            return pkgs;
        }

        private static bool MatchesGlob(string pattern, string name)
        {
            // fnmatch-style, supporting '*' and '?'
            try
            {
                var re = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(name, re);
            }
            catch
            {
                return name == pattern;
            }
        }

        public bool ShouldHold(string pkgname)
        {
            lock (_configLock) return _alpmConfig.Holdpkgs.Contains(pkgname);
        }

        public async Task<List<string>> GetUninstalledOptdepsAsync(string pkgname)
        {
            var optdeps = new List<string>();
            lock (_configLock)
            {
                var pkg = GetSyncpkg(_alpmHandle!, pkgname);
                if (pkg != null)
                {
                    foreach (IntPtr d in pkg.Optdepends)
                    {
                        string optdep = AlpmDepend.FromPointer(d).ComputeString();
                        if (AlpmPkg.FindSatisfier(_alpmHandle!.LocalDb.Pkgcache, optdep) == null)
                            optdeps.Add(optdep);
                    }
                }
            }
            return await Task.FromResult(optdeps);
        }

        private AlpmPackageLinked InitializePkgFromCache(string pkgname)
        {
            if (_pkgsCache.TryGetValue(pkgname, out var cached)) return cached;
            return null!;
        }

        private AlpmPackage? InitializePkg(AlpmPkg? alpmPkg, AlpmPkg? syncPkg)
        {
            if (alpmPkg == null) return null;
            string pkgname = alpmPkg.Name;
            if (_pkgsCache.TryGetValue(pkgname, out var pkg)) return pkg;
            var newPkg = new AlpmPackageLinked(alpmPkg, this);
            if (syncPkg != null)
            {
                newPkg.SetSyncPkg(syncPkg);
            }
            else if (Config.EnableAur && alpmPkg.Origin == PackageFrom.LOCALDB)
            {
                var foundSyncPkg = GetSyncpkg(_alpmHandle!, pkgname);
                newPkg.SetSyncPkg(foundSyncPkg);
                newPkg.SetLocalPkg(alpmPkg);
            }
            _pkgsCache[pkgname] = newPkg;
            return newPkg;
        }

        private AlpmPackageStatic InitializePkgData(AlpmPkg localPkg, AlpmPkg syncPkg)
        {
            var pkg = new AlpmPackageStatic(syncPkg, localPkg, syncPkg, this);
            if (Config.EnableAppstream && _appstreamPlugin != null)
            {
                var matchingApps = _appstreamPlugin.GetPkgnameApps(syncPkg.Name);
                if (matchingApps.Count == 1) pkg.SetApp(matchingApps[0]);
            }
            return pkg;
        }

        internal AlpmPkg? InternGetSyncpkg(string pkgname)
        {
            lock (_configLock) return GetSyncpkg(_alpmHandle!, pkgname);
        }

        private static AlpmPkg? GetSyncpkg(AlpmHandle? alpmHandle, string name)
        {
            if (alpmHandle == null) return null;
            foreach (IntPtr dbp in alpmHandle.Syncdbs)
            {
                var db = new AlpmDB(dbp);
                var pkg = db.GetPkg(name);
                if (pkg != null) return pkg;
            }
            return null;
        }

        public bool IsSyncPkg(string pkgname)
        {
            lock (_configLock) return GetSyncpkg(_alpmHandle!, pkgname) != null;
        }

        public AlpmPackage? GetSyncPkg(string pkgname)
        {
            lock (_configLock) return InitializePkg(GetSyncpkg(_alpmHandle!, pkgname), GetSyncpkg(_alpmHandle!, pkgname));
        }

        private AlpmPkg? FindDbsSatisfier(string depstring)
        {
            foreach (IntPtr dbp in _alpmHandle!.Syncdbs)
            {
                var db = new AlpmDB(dbp);
                var pkg = AlpmPkg.FindSatisfier(db.Pkgcache, depstring);
                if (pkg != null) return pkg;
            }
            return null;
        }

        public bool HasSyncSatisfier(string depstring)
        {
            lock (_configLock) return FindDbsSatisfier(depstring) != null;
        }

        public AlpmPackage? GetSyncSatisfier(string depstring)
        {
            lock (_configLock) return InitializePkg(FindDbsSatisfier(depstring), FindDbsSatisfier(depstring));
        }

        public List<AlpmPackage> GetSyncPkgsByGlob(string glob)
        {
            var pkgs = new List<AlpmPackage>();
            lock (_configLock)
            {
                foreach (IntPtr dbp in _alpmHandle!.Syncdbs)
                {
                    var db = new AlpmDB(dbp);
                    foreach (IntPtr p in db.Pkgcache)
                    {
                        var pkg = AlpmPkg.FromPointer(p)!;
                        if (MatchesGlob(glob, pkg.Name))
                            pkgs.Add(InitializePkg(pkg, pkg)!);
                    }
                }
            }
            return pkgs;
        }

        public AlpmPackage? GetPkg(string pkgname)
        {
            if (IsInstalledPkg(pkgname)) return GetInstalledPkg(pkgname);
            return GetSyncPkg(pkgname);
        }

        private static List<string> GetPkgFilesReal(AlpmHandle? alpmHandle, AlpmHandle? filesHandle, string pkgname, AlpmPkg? localPkg, string root)
        {
            var files = new List<string>();
            if (localPkg != null)
            {
                foreach (string name in localPkg.GetFileNames())
                    if (!name.EndsWith("/"))
                        files.Add(root + name);
            }
            else if (filesHandle != null)
            {
                foreach (IntPtr dbp in filesHandle.Syncdbs)
                {
                    var db = new AlpmDB(dbp);
                    var filesPkg = db.GetPkg(pkgname);
                    if (filesPkg != null)
                    {
                        foreach (string name in filesPkg.GetFileNames())
                            if (!name.EndsWith("/"))
                                files.Add(root + name);
                        break;
                    }
                }
            }
            return files;
        }

        internal List<string> GetPkgFiles(string pkgname, AlpmPkg? localPkg)
        {
            lock (_configLock)
            {
                string root = _alpmHandle!.Root;
                return GetPkgFilesReal(_alpmHandle, _filesHandle, pkgname, localPkg, root);
            }
        }

        internal async Task<List<string>> GetPkgFilesAsync(string pkgname, AlpmPkg? localPkg)
        {
            return await Task.Run(() => GetPkgFiles(pkgname, localPkg));
        }

        public List<string> GetReposNames()
        {
            if (_reposNames != null) return _reposNames;
            if (_alpmHandle == null) return _reposNames = new List<string>();
            var repos = new List<string>();
            foreach (IntPtr dbp in _alpmHandle!.Syncdbs)
            {
                var db = new AlpmDB(dbp);
                if (db.Name != null) repos.Add(db.Name);
            }
            _reposNames = repos;
            return repos;
        }

        public List<string> GetGroupsNames()
        {
            if (_groupsNames != null) return _groupsNames;
            var groups = new List<string>();
            foreach (IntPtr dbp in _alpmHandle!.Syncdbs)
            {
                var db = new AlpmDB(dbp);
                foreach (IntPtr g in db.Groupcache)
                {
                    // alpm_group_t { char *name; alpm_list_t *packages; }
                    IntPtr namePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(g);
                    if (namePtr != IntPtr.Zero)
                    {
                        string name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(namePtr)!;
                        if (!groups.Contains(name)) groups.Add(name);
                    }
                }
            }
            _groupsNames = groups;
            return groups;
        }

        public List<string> GetCategoriesNames()
        {
            if (_categoriesNames != null) return _categoriesNames;
            var categories = new List<string>();
            if (Config.EnableSnap && _snapPlugin != null)
            {
                // snap categories are pulled through the plugin in the original;
                // keep a best-effort empty list here.
            }
            _categoriesNames = categories;
            return categories;
        }

        public List<string> GetInstalledPkgs()
        {
            if (_alpmHandle == null) return new List<string>();
            var pkgs = new List<AlpmPackage>();
            lock (_configLock)
            {
                foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                    pkgs.Add(InitializePkg(AlpmPkg.FromPointer(p), null)!);
            }
            var result = new List<string>();
            foreach (var p in pkgs) result.Add(p.Name);
            return result;
        }

        public async Task<List<AlpmPackage>> GetInstalledPkgsAsync()
        {
            if (_alpmHandle == null) return new List<AlpmPackage>();
            return await Task.Run(() =>
            {
                var pkgs = new List<AlpmPackage>();
                lock (_configLock)
                {
                    foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                        pkgs.Add(InitializePkg(AlpmPkg.FromPointer(p), null)!);
                }
                return pkgs;
            });
        }

        public async Task<List<AlpmPackage>> SearchPkgsAsync(string searchString)
        {
            if (_alpmHandle == null) return new List<AlpmPackage>();
            string searchDown = searchString.ToLowerInvariant();
            var pkgs = new List<AlpmPackage>();
            var found = new HashSet<string>();
            lock (_configLock)
            {
                foreach (IntPtr dbp in _alpmHandle!.Syncdbs)
                {
                    var db = new AlpmDB(dbp);
                    foreach (IntPtr p in db.Pkgcache)
                    {
                        var alpmPkg = AlpmPkg.FromPointer(p)!;
                        if (alpmPkg.Name.ToLowerInvariant().Contains(searchDown) ||
                            (alpmPkg.Desc?.ToLowerInvariant().Contains(searchDown) ?? false))
                        {
                            if (found.Add(alpmPkg.Name))
                                pkgs.Add(InitializePkg(alpmPkg, alpmPkg)!);
                        }
                    }
                }
            }
            return await Task.FromResult(pkgs);
        }

        public async Task<List<AURPackage>> SearchAurPkgsAsync(string searchString)
        {
            var pkgs = new List<AURPackage>();
            if (Config.EnableAur && _aurPlugin != null)
            {
                foreach (var infos in _aurPlugin.Search(searchString))
                {
                    var pkg = new AURPackageLinked();
                    pkg.InitialiseFromAurInfos(infos, InternGetLocalPkg(infos.Name), this);
                    pkgs.Add(pkg);
                }
            }
            return await Task.FromResult(pkgs);
        }

        public List<string> GetForeignPkgs()
        {
            var pkgs = new List<string>();
            lock (_configLock)
            {
                foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                {
                    var localPkg = AlpmPkg.FromPointer(p)!;
                    if (GetSyncpkg(_alpmHandle, localPkg.Name) == null)
                        pkgs.Add(localPkg.Name);
                }
            }
            return pkgs;
        }

        public List<string> GetOrphans()
        {
            var pkgs = new List<string>();
            lock (_configLock)
            {
                foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                {
                    var localPkg = AlpmPkg.FromPointer(p)!;
                    if (localPkg.Reason == PackageReason.DEPEND && localPkg.ComputeRequiredby().Count == 0)
                        pkgs.Add(localPkg.Name);
                }
            }
            return pkgs;
        }

        public async Task<Dictionary<string, ulong>> GetCleanCacheDetailsAsync()
        {
            return await Task.Run(GetCleanCacheDetails);
        }

        public Dictionary<string, ulong> GetCleanCacheDetails()
        {
            var filenamesSize = new Dictionary<string, ulong>();
            var pkgVersionFilenames = new Dictionary<string, List<string>>();
            var pkgVersions = new Dictionary<string, List<string>>();
            lock (_configLock)
            {
                var cachedirs = _alpmHandle!.CachedirsList();
                foreach (string cachedirName in cachedirs)
                {
                    if (!Directory.Exists(cachedirName)) continue;
                    foreach (string filename in Directory.GetFiles(cachedirName))
                    {
                        string absoluteFilename = Path.Combine(cachedirName, Path.GetFileName(filename));
                        string? nameVersionRelease = SliceToLastDash(Path.GetFileName(filename));
                        if (nameVersionRelease == null) continue;
                        int releaseIndex = nameVersionRelease.LastIndexOf('-');
                        string? nameVersion = Slice(nameVersionRelease, 0, releaseIndex);
                        if (nameVersion == null) continue;
                        int versionIndex = nameVersion.LastIndexOf('-');
                        string? name = Slice(nameVersion, 0, versionIndex);
                        if (name == null) continue;
                        if (Config.CleanRmOnlyUninstalled && IsInstalledPkg(name)) continue;
                        ulong size = (ulong)new FileInfo(absoluteFilename).Length;
                        filenamesSize[absoluteFilename] = size;
                        if (pkgVersions.ContainsKey(name))
                        {
                            if (pkgVersionFilenames.ContainsKey(nameVersionRelease))
                            {
                                pkgVersionFilenames[nameVersionRelease].Add(absoluteFilename);
                            }
                            else
                            {
                                string? versionRelease = Slice(nameVersionRelease, versionIndex + 1, nameVersionRelease.Length);
                                if (versionRelease == null) continue;
                                pkgVersions[name].Add(versionRelease);
                                pkgVersionFilenames[nameVersionRelease] = new List<string> { absoluteFilename };
                            }
                        }
                        else
                        {
                            string? versionRelease = Slice(nameVersionRelease, versionIndex + 1, nameVersionRelease.Length);
                            if (versionRelease == null) continue;
                            pkgVersions[name] = new List<string> { versionRelease };
                            pkgVersionFilenames[nameVersionRelease] = new List<string> { absoluteFilename };
                        }
                    }
                }
            }
            if (Config.CleanKeepNumPkgs == 0) return filenamesSize;
            foreach (var kv in pkgVersions)
            {
                string name = kv.Key;
                var versions = kv.Value;
                if (versions.Count > (int)Config.CleanKeepNumPkgs)
                {
                    versions.Sort((v1, v2) => AlpmPkg.VerCmp(v2, v1)); // higher first
                }
                for (int i = 0; i < versions.Count; i++)
                {
                    if (i == (int)Config.CleanKeepNumPkgs) break;
                    if (pkgVersionFilenames.TryGetValue($"{name}-{versions[i]}", out var filenames))
                        foreach (string f in filenames) filenamesSize.Remove(f);
                }
            }
            return filenamesSize;
        }

        private static string? SliceToLastDash(string s)
        {
            int i = s.LastIndexOf('-');
            return i < 0 ? null : s.Substring(0, i);
        }
        private static string? Slice(string s, int start, int end)
        {
            if (start < 0 || end > s.Length || end <= start) return null;
            return s.Substring(start, end - start);
        }

        public async Task<Dictionary<string, ulong>> GetBuildFilesDetailsAsync()
        {
            return await Task.Run(() =>
            {
                var filenamesSize = new Dictionary<string, ulong>();
                string realAurBuildDir = GetRealAurBuildDir();
                if (string.IsNullOrEmpty(realAurBuildDir) || !Directory.Exists(realAurBuildDir))
                    return filenamesSize;
                foreach (string entry in Directory.GetDirectories(realAurBuildDir))
                {
                    string name = Path.GetFileName(entry);
                    if (name == "packages-meta-ext-v1.json.gz") continue;
                    ulong size = GetDirSize(entry);
                    filenamesSize[entry] = size;
                }
                return filenamesSize;
            });
        }

        private static ulong GetDirSize(string dir)
        {
            ulong total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    total += (ulong)new FileInfo(f).Length;
            }
            catch { }
            return total;
        }

        public async Task<List<string>> GetMirrorsCountriesAsync()
        {
            if (_mirrorsCountries != null) return _mirrorsCountries;
            _mirrorsCountries = new List<string>();
            string output;
            int status = Utils.RunCommandSync("pacman-mirrors -l", out output);
            if (status == 0)
            {
                foreach (string country in output.Split('\n'))
                    if (country != "") _mirrorsCountries.Add(country);
            }
            return await Task.FromResult(_mirrorsCountries);
        }

        public async Task<string> GetMirrorsChoosenCountryAsync()
        {
            _mirrorsChoosenCountry = "";
            string output;
            int status = Utils.RunCommandSync("pacman-mirrors -lc", out output);
            if (status == 0)
            {
                var split = output.Split(new[] { '\n' }, 2, StringSplitOptions.None);
                if (split.Length > 0) _mirrorsChoosenCountry = split[0];
            }
            return await Task.FromResult(_mirrorsChoosenCountry);
        }

        public DateTime? GetLastRefreshTime()
        {
            if (_alpmHandle == null) return null;
            string timestampPath = Path.Combine(_alpmHandle!.Dbpath, "sync", "refresh_timestamp");
            try
            {
                if (File.Exists(timestampPath))
                    return File.GetLastWriteTimeUtc(timestampPath);
            }
            catch { }
            return null;
        }

        private long GetLastRefreshAge()
        {
            var last = GetLastRefreshTime();
            if (last == null) return long.MaxValue;
            return (long)(DateTimeOffset.UtcNow - last.Value.ToUniversalTime()).TotalSeconds;
        }

        public bool NeedRefresh()
        {
            long elapsedSeconds = GetLastRefreshAge();
            if (elapsedSeconds < 3600) return false;
            long elapsedHours = elapsedSeconds / 3600;
            return elapsedHours >= (long)Config.RefreshPeriod;
        }

        // ---- updates ----
        private void GetUpdatesReal(Updates updates)
        {
            if (_alpmHandle == null) return;
            lock (_configLock)
            {
                var localPkgs = new List<string>();
                var vcsLocalPkgs = new List<string>();
                foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                {
                    var installedPkg = AlpmPkg.FromPointer(p)!;
                    var candidate = installedPkg.GetNewVersion(_alpmHandle.Syncdbs);
                    if (candidate != null)
                    {
                        if (_alpmHandle.ShouldIgnore(installedPkg) == 1 || _alpmHandle.ShouldIgnore(candidate) == 1)
                            updates.IgnoredReposUpdates.Add(InitializePkgData(installedPkg, candidate));
                        else
                            updates.ReposUpdates.Add(InitializePkgData(installedPkg, candidate));
                    }
                    else if (Config.CheckAurUpdates)
                    {
                        if (GetSyncpkg(_alpmHandle, installedPkg.Name) == null)
                        {
                            localPkgs.Add(installedPkg.Name);
                        }
                    }
                }
                if (Config.CheckFlatpakUpdates && _flatpakPlugin != null)
                {
                    var flatpakUpdates = updates.FlatpakUpdates;
                    _flatpakPlugin.GetFlatpakUpdates(ref flatpakUpdates);
                    updates.FlatpakUpdates = flatpakUpdates;
                }
            }
        }

        public Updates GetUpdates()
        {
            var updates = new Updates();
            GetUpdatesReal(updates);
            return updates;
        }

        public async Task<Updates> GetUpdatesAsync()
        {
            var updates = new Updates();
            await Task.Run(() => GetUpdatesReal(updates));
            return updates;
        }

        public uint GetUpdatesNb()
        {
            uint nb = 0;
            nb += (uint)GetUpdates().ReposUpdates.Count;
            nb += (uint)GetUpdates().AurUpdates.Count;
            nb += (uint)GetUpdates().FlatpakUpdates.Count;
            return nb;
        }

        public async Task<Updates> GetAurUpdatesAsync(HashSet<string> ignorepkgs)
        {
            var updates = new Updates();
            if (!Config.EnableAur || _aurPlugin == null)
                return await Task.FromResult(updates);

            return await Task.Run(() =>
            {
                lock (_configLock)
                {
                    foreach (string name in ignorepkgs) _alpmHandle!.AddIgnorepkg(name);
                    var localPkgs = new List<string>();
                    var vcsLocalPkgs = new List<string>();
                    foreach (IntPtr p in _alpmHandle!.LocalDb.Pkgcache)
                    {
                        var installedPkg = AlpmPkg.FromPointer(p)!;
                        if (GetSyncpkg(_alpmHandle, installedPkg.Name) == null)
                        {
                            if (Config.CheckAurVcsUpdates &&
                                (installedPkg.Name.EndsWith("-git") || installedPkg.Name.EndsWith("-svn") ||
                                 installedPkg.Name.EndsWith("-bzr") || installedPkg.Name.EndsWith("-hg")))
                            {
                                if (_alpmHandle.ShouldIgnore(installedPkg) == 0)
                                {
                                    localPkgs.Add(installedPkg.Name);
                                    vcsLocalPkgs.Add(installedPkg.Name);
                                }
                            }
                            else
                            {
                                localPkgs.Add(installedPkg.Name);
                            }
                        }
                    }
                    GetAurUpdatesReal(_aurPlugin.GetMultiInfos(localPkgs), vcsLocalPkgs, ref updates);
                    foreach (string name in ignorepkgs) _alpmHandle.RemoveIgnorepkg(name);
                }
                return updates;
            });
        }

        private void GetAurUpdatesReal(List<AURInfos> aurInfosList, List<string> vcsLocalPkgs, ref Updates updates)
        {
            foreach (var aurInfos in aurInfosList)
            {
                string name = aurInfos.Name;
                var localPkg = _alpmHandle!.LocalDb.GetPkg(name);
                if (localPkg == null) continue;
                string oldVersion = localPkg.Version;
                string newVersion;
                AURPackageLinked aurPkg;
                if (Config.CheckAurVcsUpdates)
                {
                    newVersion = aurInfos.Version;
                    aurPkg = new AURPackageLinked();
                    aurPkg.InitialiseFromAurInfos(aurInfos, localPkg, this, true);
                    aurPkg.Version = newVersion;
                }
                else
                {
                    newVersion = aurInfos.Version;
                    aurPkg = new AURPackageLinked();
                    aurPkg.InitialiseFromAurInfos(aurInfos, localPkg, this, true);
                    aurPkg.Version = newVersion;
                }
                if (AlpmPkg.VerCmp(newVersion, oldVersion) == 1)
                {
                    if (_alpmHandle.ShouldIgnore(localPkg) == 1) updates.IgnoredAurUpdates.Add(aurPkg);
                    else updates.AurUpdates.Add(aurPkg);
                }
                else if (aurInfos.Outofdate != null)
                {
                    updates.Outofdate.Add(aurPkg);
                }
            }
        }

        public async Task<AURPackage?> GetAurPkgAsync(string pkgname)
        {
            if (!Config.EnableAur || _aurPlugin == null) return null;
            var infos = _aurPlugin.GetInfos(pkgname);
            if (infos == null) return null;
            var pkg = new AURPackageLinked();
            pkg.InitialiseFromAurInfos(infos, InternGetLocalPkg(pkgname), this);
            return await Task.FromResult<AURPackage>(pkg);
        }

        // ---- Snap passthroughs ----
        public async Task<List<SnapPackage>> SearchSnapsAsync(string searchString)
        {
            var pkgs = new List<SnapPackage>();
            if (Config.EnableSnap && _snapPlugin != null)
            {
                string s = searchString.ToLowerInvariant();
                await Task.Run(() => _snapPlugin.SearchSnaps(s, ref pkgs));
            }
            return pkgs;
        }
        public bool IsInstalledSnap(string name) => Config.EnableSnap && (_snapPlugin?.IsInstalledSnap(name) ?? false);
        public async Task<SnapPackage?> GetSnapAsync(string name)
        {
            if (!Config.EnableSnap || _snapPlugin == null) return null;
            string copy = name;
            return await Task.Run(() => _snapPlugin.GetSnap(copy));
        }
        public async Task<List<SnapPackage>> GetInstalledSnapsAsync()
        {
            var pkgs = new List<SnapPackage>();
            if (Config.EnableSnap && _snapPlugin != null)
                await Task.Run(() => _snapPlugin.GetInstalledSnaps(ref pkgs));
            return pkgs;
        }
        public async Task<string> GetInstalledSnapIconAsync(string name)
        {
            if (!Config.EnableSnap || _snapPlugin == null) return "";
            string copy = name;
            return await Task.Run(() => _snapPlugin.GetInstalledSnapIcon(copy));
        }
        public async Task<List<SnapPackage>> GetCategorySnapsAsync(string category)
        {
            var pkgs = new List<SnapPackage>();
            if (Config.EnableSnap && _snapPlugin != null)
            {
                string copy = category;
                await Task.Run(() => _snapPlugin.GetCategorySnaps(copy, ref pkgs));
            }
            return pkgs;
        }

        // ---- Flatpak passthroughs ----
        public void RefreshFlatpakAppstreamData() { if (Config.EnableFlatpak) _flatpakPlugin?.RefreshAppstreamData(); }
        public async Task RefreshFlatpakAppstreamDataAsync() { await Task.Run(() => RefreshFlatpakAppstreamData()); }
        public List<string> GetFlatpakRemotesNames()
        {
            var list = new List<string>();
            if (Config.EnableFlatpak) _flatpakPlugin?.GetRemotesNames(ref list);
            return list;
        }
        public async Task<List<FlatpakPackage>> GetInstalledFlatpaksAsync()
        {
            var pkgs = new List<FlatpakPackage>();
            if (Config.EnableFlatpak && _flatpakPlugin != null)
                await Task.Run(() => _flatpakPlugin.GetInstalledFlatpaks(ref pkgs));
            return pkgs;
        }
        public async Task<List<FlatpakPackage>> SearchFlatpaksAsync(string searchString)
        {
            var pkgs = new List<FlatpakPackage>();
            if (Config.EnableFlatpak && _flatpakPlugin != null)
            {
                string s = searchString.ToLowerInvariant();
                await Task.Run(() => _flatpakPlugin.SearchFlatpaks(s, ref pkgs));
            }
            return pkgs;
        }
        public bool IsInstalledFlatpak(string name) => Config.EnableFlatpak && (_flatpakPlugin?.IsInstalledFlatpak(name) ?? false);
        public async Task<FlatpakPackage?> GetFlatpakAsync(string id)
        {
            if (!Config.EnableFlatpak || _flatpakPlugin == null) return null;
            string copy = id;
            return await Task.Run(() => _flatpakPlugin.GetFlatpak(copy));
        }
        public async Task<List<FlatpakPackage>> GetCategoryFlatpaksAsync(string category)
        {
            var pkgs = new List<FlatpakPackage>();
            if (Config.EnableFlatpak && _flatpakPlugin != null)
            {
                string copy = category;
                await Task.Run(() => _flatpakPlugin.GetCategoryFlatpaks(copy, ref pkgs));
            }
            return pkgs;
        }
    }

    internal static class HandleExtensions
    {
        public static List<string> CachedirsList(this AlpmHandle h)
        {
            var list = new List<string>();
            // cachedirs are exposed via the option getter returning an alpm_list of strings
            IntPtr head = Native.alpm_option_get_cachedirs(h._p);
            foreach (IntPtr s in new AlpmList(head))
                if (s != IntPtr.Zero) list.Add(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(s)!);
            return list;
        }
    }
}
