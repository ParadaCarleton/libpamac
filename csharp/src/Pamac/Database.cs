// libpamac — C# port
//
// Database: the central query layer over libalpm plus the AUR / appstream /
// snap / flatpak backends. Faithful port of src/database.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using LibAlpm;
using Pamac.Compat;
using static Pamac.Compat.Gettext;
using static Pamac.Compat.Log;

namespace Pamac
{
	public class Database : GObject
	{
		readonly AlpmConfig _alpmConfig;
		Handle? _alpmHandle;
		Handle? _filesHandle;
		readonly Dictionary<string, AURPackageLinked> _aurVcsPkgs = new Dictionary<string, AURPackageLinked>();
		readonly Dictionary<string, AlpmPackageLinked> _pkgsCache = new Dictionary<string, AlpmPackageLinked>();
		readonly Dictionary<string, AURPackageLinked> _aurPkgsCache = new Dictionary<string, AURPackageLinked>();
		List<string>? _mirrorsCountries;
		string _mirrorsChoosenCountry = "";
		List<string>? _groupsNames;
		List<string>? _reposNames;
		List<string>? _categoriesNames;
		IAurPlugin _aurPlugin = null!;
		IAppstreamPlugin _appstreamPlugin = null!;
		ISnapPlugin _snapPlugin = null!;
		IFlatpakPlugin _flatpakPlugin = null!;

		public Config Config { get; set; }
		internal MainContext Context { get; private set; } = MainContext.Default;
		internal bool DbsMissing { get; private set; }

		public event Action<uint>? GetUpdatesProgress;
		public event Action<string>? EmitWarning;

		public Database(Config config)
		{
			Config = config;
			Construct();
		}

		void Construct()
		{
			_alpmConfig = Config.AlpmConfig;
			Context = MainContext.Default;
			// user agent for HTTP downloads
			string userAgent = GetUserAgent();
			Environment.SetEnvironmentVariable("HTTP_USER_AGENT", userAgent, true);

			_alpmHandle = _alpmConfig.GetHandle();
			if (_alpmHandle != null)
			{
				foreach (var name in Config.IgnorePkgs) _alpmHandle.AddIgnorepkg(name);
				_alpmConfig.RegisterSyncdbs(_alpmHandle);
				_filesHandle = _alpmConfig.GetHandle(true);
				_alpmConfig.RegisterSyncdbs(_filesHandle);
			}
			_aurVcsPkgs.Clear();
			_pkgsCache.Clear();
			_aurPkgsCache.Clear();

			if (Config.SupportAur)
			{
				_aurPlugin = Config.GetAurPlugin()!;
				if (_aurPlugin == null) Config.EnableAur = false;
				else _aurPlugin.SetRealBuildDir(Config.AurBuildDir);
			}
			if (Config.SupportAppstream)
			{
				_appstreamPlugin = Config.GetAppstreamPlugin()!;
				if (_appstreamPlugin == null) Config.EnableAppstream = false;
				else if (Config.EnableAppstream) _appstreamPlugin.Load(GetReposNames());
			}
			if (Config.SupportSnap)
			{
				_snapPlugin = Config.GetSnapPlugin()!;
				if (_snapPlugin == null) Config.EnableSnap = false;
			}
			if (Config.SupportFlatpak)
			{
				_flatpakPlugin = Config.GetFlatpakPlugin()!;
				if (_flatpakPlugin == null) Config.EnableFlatpak = false;
				else
				{
					_flatpakPlugin.RefreshPeriod = Config.RefreshPeriod;
					if (Config.EnableFlatpak) _flatpakPlugin.LoadAppstreamData();
				}
			}
		}

		internal void Refresh()
		{
			_alpmConfig.Reload();
			_alpmHandle = _alpmConfig.GetHandle();
			if (_alpmHandle != null)
			{
				foreach (var name in Config.IgnorePkgs) _alpmHandle.AddIgnorepkg(name);
				_alpmConfig.RegisterSyncdbs(_alpmHandle);
				_filesHandle = _alpmConfig.GetHandle(true);
				_alpmConfig.RegisterSyncdbs(_filesHandle);
			}
			_aurVcsPkgs.Clear();
			_pkgsCache.Clear();
			_aurPkgsCache.Clear();
			if (Config.EnableSnap) _snapPlugin.Refresh();
			if (Config.EnableFlatpak) _flatpakPlugin.Refresh();
		}

		internal Handle? GetTmpHandle()
		{
			var tmpHandle = _alpmConfig.GetHandle(false, true);
			if (tmpHandle != null) _alpmConfig.RegisterSyncdbs(tmpHandle);
			return tmpHandle;
		}

		public async Task<List<string>> GetMirrorsCountriesAsync()
		{
			if (_mirrorsCountries != null) return _mirrorsCountries;
			return await Task.Run(() =>
			{
				try
				{
					int status = Spawn.SpawnCommandLineSync("pacman-mirrors -l", out string countriesStr, out _);
					if (status == 0)
					{
						_mirrorsCountries = new List<string>();
						foreach (var country in countriesStr.Split('\n'))
						{
							if (country != "") _mirrorsCountries.Add(country);
						}
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
				return _mirrorsCountries ?? new List<string>();
			});
		}

		public async Task<string> GetMirrorsChoosenCountryAsync()
		{
			_mirrorsChoosenCountry = "";
			return await Task.Run(() =>
			{
				try
				{
					int status = Spawn.SpawnCommandLineSync("pacman-mirrors -lc", out string countriesStr, out _);
					if (status == 0)
					{
						_mirrorsChoosenCountry = countriesStr.Split('\n', 2)[0];
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
				return _mirrorsChoosenCountry;
			});
		}

		public string GetAlpmDepName(string depString) => Depend.FromString(depString).Name;

		public Func<string?, string?, int> VerCmp = Alpm.PkgVerCmp;

		void GetCleanCacheDetailsReal(Dictionary<string, ulong?> filenamesSize)
		{
			var pkgVersionFilenames = new Dictionary<string, List<string>>();
			var pkgVersions = new Dictionary<string, List<string>>();
			foreach (var cachedirName in _alpmHandle!.CacheDirs)
			{
				var cachedir = GFile.NewForPath(cachedirName);
				try
				{
					foreach (var (filename, size, type, _) in cachedir.EnumerateChildren())
					{
						if (type != FileType.Regular) continue;
						string absoluteFilename = cachedirName + filename;
						string? nameVersionRelease = filename.Substring(0, filename.LastIndexOf('-'));
						int releaseIndex = nameVersionRelease.LastIndexOf('-');
						string nameVersion = nameVersionRelease.Substring(0, releaseIndex);
						int versionIndex = nameVersion.LastIndexOf('-');
						string name = nameVersion.Substring(0, versionIndex);
						if (Config.CleanRmOnlyUninstalled && IsInstalledPkg(name)) continue;
						filenamesSize[absoluteFilename] = size;
						if (pkgVersions.ContainsKey(name))
						{
							if (pkgVersionFilenames.ContainsKey(nameVersionRelease))
							{
								pkgVersionFilenames[nameVersionRelease].Add(absoluteFilename);
							}
							else
							{
								string versionRelease = nameVersionRelease.Substring(versionIndex + 1);
								pkgVersions[name].Add(versionRelease);
								pkgVersionFilenames[nameVersionRelease] = new List<string> { absoluteFilename };
							}
						}
						else
						{
							string versionRelease = nameVersionRelease.Substring(versionIndex + 1);
							pkgVersions[name] = new List<string> { versionRelease };
							pkgVersionFilenames[nameVersionRelease] = new List<string> { absoluteFilename };
						}
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
			}
			if (Config.CleanKeepNumPkgs == 0) return;
			foreach (var (name, versions) in pkgVersions)
			{
				if (versions.Count > (int)Config.CleanKeepNumPkgs)
				{
					versions.Sort((v1, v2) => Alpm.PkgVerCmp(v2, v1));
				}
				for (int i = 0; i < versions.Count; i++)
				{
					if (i == (int)Config.CleanKeepNumPkgs) break;
					if (pkgVersionFilenames.TryGetValue(name + "-" + versions[i], out var filenames))
					{
						foreach (var f in filenames) filenamesSize.Remove(f);
					}
				}
			}
		}

		public Dictionary<string, ulong?> GetCleanCacheDetails()
		{
			var filenamesSize = new Dictionary<string, ulong?>();
			GetCleanCacheDetailsReal(filenamesSize);
			return filenamesSize;
		}

		public async Task<Dictionary<string, ulong?>> GetCleanCacheDetailsAsync()
		{
			var filenamesSize = new Dictionary<string, ulong?>();
			await Task.Run(() => GetCleanCacheDetailsReal(filenamesSize));
			return filenamesSize;
		}

		public string GetRealAurBuildDir() => _aurPlugin.GetRealBuildDir();

		internal List<AURInfos> GetAurProviders(string pkgname) => _aurPlugin.GetProviders(pkgname);

		void GetBuildFilesDetailsReal(Dictionary<string, ulong?> filenamesSize)
		{
			string realAurBuildDir = GetRealAurBuildDir();
			var buildDirectory = GFile.NewForPath(realAurBuildDir);
			if (!buildDirectory.QueryExists()) return;
			try
			{
				foreach (var (filename, _, _, _) in buildDirectory.EnumerateChildren())
				{
					if (filename == "packages-meta-ext-v1.json.gz") continue;
					string absoluteFilename = PathCompat.BuildFilename(buildDirectory.GetPath(), filename);
					var child = GFile.NewForPath(absoluteFilename);
					filenamesSize[absoluteFilename] = child.MeasureDiskUsage();
				}
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
		}

		public Dictionary<string, ulong?> GetBuildFilesDetails()
		{
			var filenamesSize = new Dictionary<string, ulong?>();
			GetBuildFilesDetailsReal(filenamesSize);
			return filenamesSize;
		}

		public async Task<Dictionary<string, ulong?>> GetBuildFilesDetailsAsync()
		{
			var filenamesSize = new Dictionary<string, ulong?>();
			await Task.Run(() => GetBuildFilesDetailsReal(filenamesSize));
			return filenamesSize;
		}

		public bool IsInstalledPkg(string pkgname) => _alpmHandle!.LocalDb.GetPkg(pkgname) != null;

		internal Package? InternGetLocalPkg(string pkgname) => _alpmHandle!.LocalDb.GetPkg(pkgname);

		public AlpmPackage? GetInstalledPkg(string pkgname) =>
			InitialisePkg(_alpmHandle!.LocalDb.GetPkg(pkgname), null);

		public bool HasInstalledSatisfier(string depstring) =>
			Alpm.FindSatisfier(_alpmHandle!.LocalDb.PkgCache, depstring) != null;

		public AlpmPackage? GetInstalledSatisfier(string depstring) =>
			InitialisePkg(Alpm.FindSatisfier(_alpmHandle!.LocalDb.PkgCache, depstring), null);

		public List<AlpmPackage> GetInstalledPkgsByGlob(string glob)
		{
			var pkgs = new List<AlpmPackage>();
			foreach (var localPkg in _alpmHandle!.LocalDb.PkgCache)
			{
				if (Posix.FnMatch(glob, localPkg.Name) == 0)
					pkgs.Add(InitialisePkg(localPkg, null)!);
			}
			return pkgs;
		}

		public bool ShouldHold(string pkgname) => Config.AlpmConfig.HoldPkgs.Contains(pkgname);

		internal async Task<List<string>> GetUninstalledOptdepsAsync(string pkgname)
		{
			var optdeps = new List<string>();
			await Task.Run(() =>
			{
				var pkg = GetSyncpkg(_alpmHandle!, pkgname);
				if (pkg != null)
				{
					foreach (var od in pkg.OptDepends)
					{
						string optdep = od.ComputeString();
						if (Alpm.FindSatisfier(_alpmHandle!.LocalDb.PkgCache, optdep) == null)
							optdeps.Add(optdep);
					}
				}
			});
			return optdeps;
		}

		AlpmPackageStatic InitialisePkgData(Package? localPkg, Package? syncPkg)
		{
			// only used for updates so it is a sync_pkg
			var pkg = new AlpmPackageStatic(localPkg, syncPkg);
			if (Config.EnableAppstream)
			{
				var matchingApps = _appstreamPlugin.GetPkgnameApps(syncPkg!.Name);
				if (matchingApps.Count == 1) pkg.SetApp(matchingApps[0]);
			}
			return pkg;
		}

		AlpmPackage? InitialisePkg(Package? alpmPkg, Package? syncPkg)
		{
			if (alpmPkg == null) return null;
			string pkgname = alpmPkg.Name;
			if (_pkgsCache.TryGetValue(pkgname, out var cached)) return cached;
			var newPkg = new AlpmPackageLinked(alpmPkg, this);
			if (syncPkg != null)
			{
				newPkg.SetSyncPkg(syncPkg);
			}
			else if (Config.EnableAur && alpmPkg.Origin == PackageFrom.LocalDb)
			{
				var foundSyncPkg = GetSyncpkg(_alpmHandle!, pkgname);
				newPkg.SetSyncPkg(foundSyncPkg);
				newPkg.SetLocalPkg(alpmPkg);
				if (foundSyncPkg == null)
				{
					if (_aurPlugin.GetInfos(alpmPkg.Name) != null) newPkg.Repo = DGettext(null, "AUR");
				}
			}
			if (Config.EnableAppstream)
			{
				var matchingApps = _appstreamPlugin.GetPkgnameApps(pkgname);
				if (matchingApps.Count == 1) newPkg.SetApp(matchingApps[0]);
			}
			if (_pkgsCache.TryGetValue(newPkg.Id, out var byId)) return byId;
			_pkgsCache[newPkg.Id] = newPkg;
			return newPkg;
		}

		void InitialisePkgs(List<Package>? alpmPkgs, List<AlpmPackage> pkgs)
		{
			var data = new Dictionary<string, AlpmPackageLinked>();
			var foreignPkgnames = new List<string>();
			if (alpmPkgs == null) return;
			foreach (var alpmPkg in alpmPkgs)
			{
				string pkgname = alpmPkg.Name;
				if (_pkgsCache.TryGetValue(pkgname, out var cached))
				{
					pkgs.Add(cached);
					continue;
				}
				var pkg = new AlpmPackageLinked(alpmPkg, this);
				if (Config.EnableAur && alpmPkg.Origin == PackageFrom.LocalDb)
				{
					var syncPkg = GetSyncpkg(_alpmHandle!, pkgname);
					pkg.SetSyncPkg(syncPkg);
					pkg.SetLocalPkg(alpmPkg);
					if (syncPkg == null)
					{
						foreignPkgnames.Add(pkgname);
						data[pkgname] = pkg;
					}
				}
				if (Config.EnableAppstream)
				{
					var apps = _appstreamPlugin.GetPkgnameApps(pkgname);
					int appsLength = apps.Count;
					if (appsLength > 0)
					{
						var app = apps[0];
						pkg.SetApp(app);
						if (_pkgsCache.TryGetValue(pkg.Id, out var cachedPkg)) pkg = cachedPkg;
						else _pkgsCache[pkg.Id] = pkg;
						for (int i = 1; i < appsLength; i++)
						{
							app = apps[i];
							if (_pkgsCache.TryGetValue(app.Id!, out var cachedPkg2)) pkgs.Add(cachedPkg2);
							else
							{
								var pkgDup = new AlpmPackageLinked(alpmPkg, this);
								pkgDup.SetApp(apps[i]);
								pkgs.Add(pkgDup);
								_pkgsCache[pkgDup.Id] = pkgDup;
							}
						}
					}
					else _pkgsCache[pkg.Id] = pkg;
				}
				else _pkgsCache[pkg.Id] = pkg;
				pkgs.Add(pkg);
			}
			if (foreignPkgnames.Count > 0)
			{
				var aurInfosList = _aurPlugin.GetMultiInfos(foreignPkgnames);
				foreach (var aurInfos in aurInfosList)
				{
					if (data.TryGetValue(aurInfos.Name, out var pkg)) pkg.Repo = DGettext(null, "AUR");
				}
			}
		}

		public List<AlpmPackage> GetInstalledPkgs()
		{
			var pkgs = new List<AlpmPackage>();
			InitialisePkgs(_alpmHandle!.LocalDb.PkgCache, pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetInstalledPkgsAsync()
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => InitialisePkgs(_alpmHandle!.LocalDb.PkgCache, pkgs));
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetInstalledAppsAsync()
		{
			var pkgs = new List<AlpmPackage>();
			if (Config.EnableAppstream)
			{
				await Task.Run(() =>
				{
					foreach (var apps in _appstreamPlugin.GetApps())
					{
						foreach (var app in apps.Values)
						{
							string pkgname = app.Pkgname!;
							var localPkg = _alpmHandle!.LocalDb.GetPkg(pkgname);
							if (localPkg == null) continue;
							if (!_pkgsCache.TryGetValue(app.Id!, out var pkg))
							{
								pkg = new AlpmPackageLinked(localPkg, this);
								pkg.SetLocalPkg(localPkg);
								pkg.SetApp(app);
								_pkgsCache[pkg.Id] = pkg;
							}
							pkgs.Add(pkg);
						}
					}
				});
			}
			return pkgs;
		}

		void GetExplicitlyInstalledPkgsReal(List<AlpmPackage> pkgs)
		{
			var alpmPkgs = new List<Package>();
			foreach (var pkg in _alpmHandle!.LocalDb.PkgCache)
			{
				if (pkg.Reason == PackageReason.Explicit) alpmPkgs.Add(pkg);
			}
			InitialisePkgs(alpmPkgs, pkgs);
		}

		public List<AlpmPackage> GetExplicitlyInstalledPkgs()
		{
			var pkgs = new List<AlpmPackage>();
			GetExplicitlyInstalledPkgsReal(pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetExplicitlyInstalledPkgsAsync()
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => GetExplicitlyInstalledPkgsReal(pkgs));
			return pkgs;
		}

		void GetForeignPkgsReal(List<AlpmPackage> pkgs)
		{
			var alpmPkgs = new List<Package>();
			foreach (var pkg in _alpmHandle!.LocalDb.PkgCache)
			{
				if (!IsSyncpkg(pkg.Name)) alpmPkgs.Add(pkg);
			}
			InitialisePkgs(alpmPkgs, pkgs);
		}

		public List<AlpmPackage> GetForeignPkgs()
		{
			var pkgs = new List<AlpmPackage>();
			GetForeignPkgsReal(pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetForeignPkgsAsync()
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => GetForeignPkgsReal(pkgs));
			return pkgs;
		}

		void GetOrphansReal(List<AlpmPackage> pkgs)
		{
			var alpmPkgs = new List<Package>();
			foreach (var pkg in _alpmHandle!.LocalDb.PkgCache)
			{
				if (pkg.Reason == PackageReason.Depend)
				{
					var requiredby = pkg.ComputeRequiredBy();
					if (requiredby.Count == 0)
					{
						var optionalfor = pkg.ComputeOptionalFor();
						if (optionalfor.Count == 0) alpmPkgs.Add(pkg);
					}
				}
			}
			InitialisePkgs(alpmPkgs, pkgs);
		}

		public List<AlpmPackage> GetOrphans()
		{
			var pkgs = new List<AlpmPackage>();
			GetOrphansReal(pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetOrphansAsync()
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => GetOrphansReal(pkgs));
			return pkgs;
		}

		internal Package? InternGetSyncPkg(string pkgname) => GetSyncpkg(_alpmHandle!, pkgname);

		Package? GetSyncpkg(Handle? handle, string pkgname)
		{
			foreach (var db in handle!.SyncDbs)
			{
				var pkg = db.GetPkg(pkgname);
				if (pkg != null) return pkg;
			}
			return null;
		}

		bool IsSyncpkg(string pkgname) => GetSyncpkg(_alpmHandle!, pkgname) != null;

		public bool IsSyncPkg(string pkgname) => IsSyncpkg(pkgname);

		public AlpmPackage? GetSyncPkg(string pkgname)
		{
			var syncPkg = GetSyncpkg(_alpmHandle!, pkgname);
			return InitialisePkg(syncPkg, syncPkg);
		}

		Package? FindDbsSatisfier(string depstring)
		{
			foreach (var db in _alpmHandle!.SyncDbs)
			{
				var pkg = Alpm.FindSatisfier(db.PkgCache, depstring);
				if (pkg != null) return pkg;
			}
			return null;
		}

		public bool HasSyncSatisfier(string depstring) => FindDbsSatisfier(depstring) != null;

		public AlpmPackage? GetSyncSatisfier(string depstring)
		{
			var syncPkg = FindDbsSatisfier(depstring);
			return InitialisePkg(syncPkg, syncPkg);
		}

		public List<AlpmPackage> GetSyncPkgsByGlob(string glob)
		{
			var pkgs = new List<AlpmPackage>();
			for (int i = _alpmHandle!.SyncDbs.Count - 1; i >= 0; i--)
			{
				var db = _alpmHandle.SyncDbs[i];
				foreach (var syncPkg in db.PkgCache)
				{
					if (Posix.FnMatch(glob, syncPkg.Name) == 0)
						pkgs.Add(InitialisePkg(syncPkg, syncPkg)!);
				}
			}
			return pkgs;
		}

		public Package? GetAppById(string appId)
		{
			if (_pkgsCache.TryGetValue(appId, out var pkg)) return pkg;
			if (Config.EnableAppstream)
			{
				foreach (var apps in _appstreamPlugin.GetApps())
				{
					if (!apps.TryGetValue(appId, out var app)) continue;
					string pkgname = app.Pkgname!;
					var localPkg = _alpmHandle!.LocalDb.GetPkg(pkgname);
					if (localPkg != null)
					{
						var newPkg = new AlpmPackageLinked(localPkg, this);
						newPkg.SetLocalPkg(localPkg);
						newPkg.SetApp(app);
						_pkgsCache[newPkg.Id] = newPkg;
						pkg = newPkg;
					}
					else
					{
						var syncPkg = GetSyncpkg(_alpmHandle!, pkgname);
						if (syncPkg != null)
						{
							var newPkg = new AlpmPackageLinked(syncPkg, this);
							newPkg.SetLocalPkg(localPkg);
							newPkg.SetSyncPkg(syncPkg);
							newPkg.SetApp(app);
							_pkgsCache[newPkg.Id] = newPkg;
							pkg = newPkg;
						}
					}
					break;
				}
			}
			if (pkg == null && Config.EnableFlatpak) pkg = _flatpakPlugin.GetFlatpakByAppId(appId);
			if (pkg == null && Config.EnableSnap) pkg = _snapPlugin.GetSnapByAppId(appId);
			return pkg;
		}

		public async Task<Stream> GetUrlStream(string url)
		{
			using var client = new System.Net.Http.HttpClient();
			var response = await client.GetAsync(url);
			return await response.Content.ReadAsStreamAsync();
		}

		List<Package> CustomDbSearch(DB db, List<string> needles)
		{
			var allMatch = new List<Package>(db.PkgCache);
			foreach (var targ in needles)
			{
				if (targ == null) continue;
				var needleMatch = new List<Package>();
				Regex? regex = null;
				try { regex = new Regex(targ); } catch (Exception e) { Warning(e.Message); }
				foreach (var pkg in allMatch)
				{
					bool matched = false;
					string name = pkg.Name;
					string desc = pkg.Desc ?? string.Empty;
					if (name != null && (targ == name || targ == name.ToLowerInvariant() || (regex != null && regex.IsMatch(name))))
						matched = true;
					else if (desc != null && desc.Contains(targ)) matched = true;
					if (!matched)
					{
						foreach (var provide in pkg.Provides)
						{
							if (targ == provide.Name || (regex != null && regex.IsMatch(provide.Name))) { matched = true; break; }
						}
					}
					if (!matched)
					{
						foreach (var group in pkg.Groups)
						{
							if (targ == group || (regex != null && regex.IsMatch(group))) { matched = true; break; }
						}
					}
					if (matched) needleMatch.Add(pkg);
				}
				allMatch = needleMatch;
			}
			return allMatch;
		}

		List<Package> SearchLocalDb(string searchString)
		{
			var needles = new List<string>(searchString.Split(' '));
			return CustomDbSearch(_alpmHandle!.LocalDb, needles);
		}

		List<Package> SearchSyncDbs(string searchString)
		{
			var needles = new List<string>(searchString.Split(' '));
			var result = CustomDbSearch(_alpmHandle!.LocalDb, needles);
			var syncpkgs = new List<Package>();
			foreach (var db in _alpmHandle!.SyncDbs)
			{
				var found = CustomDbSearch(db, needles);
				foreach (var p in found)
				{
					if (!syncpkgs.Any(x => x.Name == p.Name)) syncpkgs.Add(p);
				}
			}
			// remove foreign pkgs from local pkgs found
			var resultNames = new HashSet<string>(result.Select(p => p.Name));
			var foreigns = result.Where(p => !syncpkgs.Any(s => s.Name == p.Name)).ToList();
			result = result.Where(p => !foreigns.Contains(p)).ToList();
			foreach (var p in syncpkgs)
			{
				if (!result.Any(x => x.Name == p.Name)) result.Add(p);
			}
			return result;
		}

		void SearchInstalledPkgsReal(string searchString, List<AlpmPackage> pkgs)
		{
			var result = SearchLocalDb(searchString);
			if (Config.EnableAppstream)
			{
				var searchTokens = searchString.Split(' ');
				foreach (var app in _appstreamPlugin.Search(searchTokens))
				{
					var alpmPkg = _alpmHandle!.LocalDb.GetPkg(app.Pkgname!);
					if (alpmPkg != null && !result.Any(p => p.Name == alpmPkg.Name)) result.Add(alpmPkg);
				}
			}
			InitialisePkgs(result, pkgs);
		}

		public List<AlpmPackage> SearchInstalledPkgs(string searchString)
		{
			var pkgs = new List<AlpmPackage>();
			SearchInstalledPkgsReal(searchString.ToLowerInvariant(), pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> SearchInstalledPkgsAsync(string searchString)
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => SearchInstalledPkgsReal(searchString.ToLowerInvariant(), pkgs));
			return pkgs;
		}

		void SearchReposPkgsReal(string searchString, List<AlpmPackage> pkgs)
		{
			var result = SearchSyncDbs(searchString);
			if (Config.EnableAppstream)
			{
				var searchTokens = searchString.Split(' ');
				foreach (var app in _appstreamPlugin.Search(searchTokens))
				{
					var alpmPkg = GetSyncpkg(_alpmHandle!, app.Pkgname!);
					if (alpmPkg != null && !result.Any(p => p.Name == alpmPkg.Name)) result.Add(alpmPkg);
				}
			}
			InitialisePkgs(result, pkgs);
		}

		public List<AlpmPackage> SearchReposPkgs(string searchString)
		{
			var pkgs = new List<AlpmPackage>();
			SearchReposPkgsReal(searchString.ToLowerInvariant(), pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> SearchReposPkgsAsync(string searchString)
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => SearchReposPkgsReal(searchString.ToLowerInvariant(), pkgs));
			return pkgs;
		}

		List<Package> SearchAllDbs(string searchString)
		{
			var needles = new List<string>(searchString.Split(' '));
			var result = CustomDbSearch(_alpmHandle!.LocalDb, needles);
			var syncpkgs = new List<Package>();
			foreach (var db in _alpmHandle!.SyncDbs)
			{
				foreach (var p in CustomDbSearch(db, needles))
				{
					if (!syncpkgs.Any(x => x.Name == p.Name)) syncpkgs.Add(p);
				}
			}
			foreach (var p in syncpkgs)
			{
				if (!result.Any(x => x.Name == p.Name)) result.Add(p);
			}
			return result;
		}

		public List<AlpmPackage> SearchUninstalledApps(List<string> searchTerms)
		{
			var pkgs = new List<AlpmPackage>();
			var appstreamResult = new List<Package>();
			if (Config.EnableAppstream)
			{
				var data = searchTerms.ToArray();
				foreach (var app in _appstreamPlugin.Search(data))
				{
					var alpmPkg = _alpmHandle!.LocalDb.GetPkg(app.Pkgname!);
					if (alpmPkg == null)
					{
						alpmPkg = GetSyncpkg(_alpmHandle!, app.Pkgname!);
						if (alpmPkg != null && !appstreamResult.Any(p => p.Name == alpmPkg.Name))
							appstreamResult.Add(alpmPkg);
					}
				}
			}
			InitialisePkgs(appstreamResult, pkgs);
			return pkgs;
		}

		void SearchPkgsReal(string searchString, List<AlpmPackage> pkgs)
		{
			var result = SearchAllDbs(searchString);
			if (Config.EnableAppstream)
			{
				var searchTokens = searchString.Split(' ');
				foreach (var app in _appstreamPlugin.Search(searchTokens))
				{
					var alpmPkg = _alpmHandle!.LocalDb.GetPkg(app.Pkgname!);
					if (alpmPkg == null) alpmPkg = GetSyncpkg(_alpmHandle!, app.Pkgname!);
					if (alpmPkg != null && !result.Any(p => p.Name == alpmPkg.Name)) result.Add(alpmPkg);
				}
			}
			InitialisePkgs(result, pkgs);
		}

		public List<AlpmPackage> SearchPkgs(string searchString)
		{
			var pkgs = new List<AlpmPackage>();
			SearchPkgsReal(searchString.ToLowerInvariant(), pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> SearchPkgsAsync(string searchString)
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => SearchPkgsReal(searchString.ToLowerInvariant(), pkgs));
			return pkgs;
		}

		void SearchAurPkgsReal(string searchString, List<AURPackage> pkgs)
		{
			var aurInfosList = _aurPlugin.Search(searchString);
			foreach (var aurInfos in aurInfosList)
			{
				string name = aurInfos.Name;
				if (!_aurPkgsCache.TryGetValue(name, out var pkg))
				{
					var localPkg = _alpmHandle!.LocalDb.GetPkg(name);
					pkg = new AURPackageLinked();
					pkg.InitialiseFromAurInfos(aurInfos, localPkg, this);
					_aurPkgsCache[pkg.Id] = pkg;
				}
				pkgs.Add(pkg);
			}
		}

		public List<AURPackage> SearchAurPkgs(string searchString)
		{
			var pkgs = new List<AURPackage>();
			if (Config.EnableAur) SearchAurPkgsReal(searchString.ToLowerInvariant(), pkgs);
			return pkgs;
		}

		public async Task<List<AURPackage>> SearchAurPkgsAsync(string searchString)
		{
			var pkgs = new List<AURPackage>();
			if (Config.EnableAur)
				await Task.Run(() => SearchAurPkgsReal(searchString.ToLowerInvariant(), pkgs));
			return pkgs;
		}

		public Dictionary<string, List<string>> SearchFiles(List<string> files)
		{
			var result = new Dictionary<string, List<string>>();
			foreach (var file in files)
			{
				foreach (var alpmPkg in _alpmHandle!.LocalDb.PkgCache)
				{
					var foundFiles = new List<string>();
					foreach (var f in alpmPkg.Files.Files)
					{
						if (f.Name.EndsWith("/")) continue;
						string realFileName = (_alpmHandle.RootDir ?? "/") + f.Name;
						if (realFileName.Contains(file)) foundFiles.Add(realFileName);
					}
					if (foundFiles.Count != 0) result[alpmPkg.Name] = foundFiles;
				}
				foreach (var db in _filesHandle!.SyncDbs)
				{
					foreach (var alpmPkg in db.PkgCache)
					{
						var foundFiles = new List<string>();
						foreach (var f in alpmPkg.Files.Files)
						{
							if (f.Name.EndsWith("/")) continue;
							string realFileName = (_alpmHandle.RootDir ?? "/") + f.Name;
							if (realFileName.Contains(file)) foundFiles.Add(realFileName);
						}
						if (foundFiles.Count != 0) result[alpmPkg.Name] = foundFiles;
					}
				}
			}
			return result;
		}

		public List<string> GetCategoriesNames()
		{
			if (_categoriesNames != null) return _categoriesNames;
			_categoriesNames = new List<string>
			{
				"Featured", "Photo & Video", "Music & Audio", "Productivity",
				"Communication & News", "Education & Science", "Games", "Utilities", "Development"
			};
			return _categoriesNames;
		}

		List<AlpmPackage> GetAppsPkgs(Dictionary<string, App> apps)
		{
			var pkgs = new List<AlpmPackage>();
			foreach (var app in apps.Values)
			{
				string pkgname = app.Pkgname!;
				if (_pkgsCache.TryGetValue(app.Id!, out var cached))
				{
					pkgs.Add(cached);
					continue;
				}
				var localPkg = _alpmHandle!.LocalDb.GetPkg(pkgname);
				if (localPkg != null)
				{
					var pkg = new AlpmPackageLinked(localPkg, this);
					pkg.SetLocalPkg(localPkg);
					pkg.SetApp(app);
					pkgs.Add(pkg);
					_pkgsCache[pkg.Id] = pkg;
				}
				else
				{
					var syncPkg = GetSyncpkg(_alpmHandle!, pkgname);
					if (syncPkg != null)
					{
						var pkg = new AlpmPackageLinked(syncPkg, this);
						pkg.SetLocalPkg(localPkg);
						pkg.SetSyncPkg(syncPkg);
						pkg.SetApp(app);
						pkgs.Add(pkg);
						_pkgsCache[pkg.Id] = pkg;
					}
				}
			}
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetCategoryPkgsAsync(string category)
		{
			var pkgs = new List<AlpmPackage>();
			if (Config.EnableAppstream)
			{
				await Task.Run(() =>
				{
					switch (category)
					{
						case "Featured":
						case "Photo & Video":
						case "Music & Audio":
						case "Productivity":
						case "Communication & News":
						case "Education & Science":
						case "Games":
						case "Utilities":
						case "Development":
							pkgs = GetAppsPkgs(_appstreamPlugin.GetCategoryApps(category));
							break;
					}
				});
			}
			return pkgs;
		}

		public List<string> GetReposNames()
		{
			if (_reposNames != null) return _reposNames;
			_reposNames = new List<string>();
			foreach (var db in _alpmHandle!.SyncDbs) _reposNames.Add(db.Name);
			return _reposNames;
		}

		void GetRepoPkgsReal(string repo, List<AlpmPackage> pkgs)
		{
			foreach (var db in _alpmHandle!.SyncDbs)
			{
				if (db.Name == repo)
				{
					foreach (var syncPkg in db.PkgCache)
					{
						var localPkg = _alpmHandle.LocalDb.GetPkg(syncPkg.Name);
						if (localPkg != null) pkgs.Add(InitialisePkg(localPkg, syncPkg)!);
						else pkgs.Add(InitialisePkg(syncPkg, syncPkg)!);
					}
					break;
				}
			}
		}

		public List<AlpmPackage> GetRepoPkgs(string repo)
		{
			var pkgs = new List<AlpmPackage>();
			GetRepoPkgsReal(repo, pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetRepoPkgsAsync(string repo)
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => GetRepoPkgsReal(repo, pkgs));
			return pkgs;
		}

		public List<string> GetGroupsNames()
		{
			if (_groupsNames != null) return _groupsNames;
			_groupsNames = new List<string>();
			var seen = new HashSet<string>();
			foreach (var g in _alpmHandle!.LocalDb.GroupCache) { seen.Add(g.Name); _groupsNames.Add(g.Name); }
			foreach (var db in _alpmHandle.SyncDbs)
			{
				foreach (var g in db.GroupCache)
				{
					if (seen.Add(g.Name)) _groupsNames.Add(g.Name);
				}
			}
			_groupsNames.Sort();
			return _groupsNames;
		}

		void GetGroupPkgsReal(string groupName, List<AlpmPackage> pkgs)
		{
			var alpmPkgs = new List<Package>();
			int dbsFound = 0;
			var grp = _alpmHandle!.LocalDb.GetGroup(groupName);
			if (grp != null)
			{
				dbsFound++;
				foreach (var localPkg in grp.Packages) alpmPkgs.Add(localPkg);
			}
			foreach (var db in _alpmHandle.SyncDbs)
			{
				grp = db.GetGroup(groupName);
				if (grp != null)
				{
					dbsFound++;
					foreach (var pkg in grp.Packages)
					{
						if (!alpmPkgs.Any(p => p.Name == pkg.Name)) alpmPkgs.Add(pkg);
					}
				}
			}
			InitialisePkgs(alpmPkgs, pkgs);
			if (dbsFound > 1) pkgs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
		}

		public List<AlpmPackage> GetGroupPkgs(string groupName)
		{
			var pkgs = new List<AlpmPackage>();
			GetGroupPkgsReal(groupName, pkgs);
			return pkgs;
		}

		public async Task<List<AlpmPackage>> GetGroupPkgsAsync(string groupName)
		{
			var pkgs = new List<AlpmPackage>();
			await Task.Run(() => GetGroupPkgsReal(groupName, pkgs));
			return pkgs;
		}

		public AlpmPackage? GetPkg(string pkgname)
		{
			if (IsInstalledPkg(pkgname)) return GetInstalledPkg(pkgname);
			return GetSyncPkg(pkgname);
		}

		void GetPkgFilesReal(string pkgname, Package? localPkg, List<string> files)
		{
			if (localPkg != null)
			{
				foreach (var f in localPkg.Files.Files)
				{
					if (!f.Name.EndsWith("/")) files.Add((_alpmHandle!.RootDir ?? "/") + f.Name);
				}
			}
			else
			{
				foreach (var db in _filesHandle!.SyncDbs)
				{
					var filesPkg = db.GetPkg(pkgname);
					if (filesPkg != null)
					{
						foreach (var f in filesPkg.Files.Files)
						{
							if (!f.Name.EndsWith("/")) files.Add((_alpmHandle!.RootDir ?? "/") + f.Name);
						}
						break;
					}
				}
			}
		}

		internal List<string> GetPkgFiles(string pkgname, Package? localPkg)
		{
			var files = new List<string>();
			GetPkgFilesReal(pkgname, localPkg, files);
			return files;
		}

		internal async Task<List<string>> GetPkgFilesAsync(string pkgname, Package? localPkg)
		{
			var files = new List<string>();
			await Task.Run(() => GetPkgFilesReal(pkgname, localPkg, files));
			return files;
		}

		int LaunchSubprocess(System.Diagnostics.ProcessStartInfo launcher, List<string> cmdline, Cancellable? cancellable = null)
		{
			int status = 1;
			try
			{
				using var p = new System.Diagnostics.Process { StartInfo = launcher };
				foreach (var arg in cmdline) p.StartInfo.ArgumentList.Add(arg);
				p.Start();
				if (cancellable != null && cancellable.IsCancelled) { p.Kill(); return 1; }
				p.WaitForExit();
				status = p.ExitCode;
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return status;
		}

		public GFile? CloneBuildFiles(string pkgname, bool overwriteFiles, Cancellable? cancellable = null)
		{
			int status;
			string realAurBuildDir = GetRealAurBuildDir();
			var builddir = GFile.NewForPath(realAurBuildDir);
			if (!builddir.QueryExists())
			{
				try { builddir.MakeDirectoryWithParents(); }
				catch (Exception e) { Warning(e.Message); return null; }
			}
			var launcher = new System.Diagnostics.ProcessStartInfo
			{
				UseShellExecute = false,
				RedirectStandardOutput = false,
				RedirectStandardError = false
			};
			var dynamicUserCmdline = GetDynamicUserCmdline(realAurBuildDir);
			var pkgdir = builddir.GetChild(pkgname);
			var cmdline = new List<string>();
			if (pkgdir.QueryExists())
			{
				if (overwriteFiles)
				{
					launcher.WorkingDirectory = realAurBuildDir;
					cmdline = dynamicUserCmdline.ToList();
					cmdline.Add("rm"); cmdline.Add("-rf"); cmdline.Add(pkgdir.GetPath());
					LaunchSubprocess(launcher, cmdline);
					cmdline = dynamicUserCmdline.ToList();
					cmdline.Add("git"); cmdline.Add("clone"); cmdline.Add("-q"); cmdline.Add("--depth=1");
					cmdline.Add($"https://aur.archlinux.org/{pkgname}.git");
				}
				else
				{
					string cwd = pkgdir.GetPath();
					launcher.WorkingDirectory = cwd;
					cmdline = dynamicUserCmdline.ToList();
					cmdline.Add("git"); cmdline.Add("fetch"); cmdline.Add("-q");
					status = LaunchSubprocess(launcher, cmdline, cancellable);
					if (cancellable != null && cancellable.IsCancelled) return null;
					if (status == 0)
					{
						// write diff file
						try
						{
							var file = GFile.NewForPath(PathCompat.BuildFilename(cwd, "diff"));
							if (file.QueryExists()) file.Delete();
							cmdline = dynamicUserCmdline.ToList();
							cmdline.Add("git"); cmdline.Add("diff"); cmdline.Add("--exit-code"); cmdline.Add("origin/master");
							foreach (var (filename, _, _, _) in pkgdir.EnumerateChildren())
							{
								if (filename != ".SRCINFO") cmdline.Add(filename);
							}
							var diffProc = new Subprocess(cmdline.ToArray());
							diffProc.Wait();
							if (diffProc.ExitStatus == 1)
							{
								using var dis = new DataInputStream(diffProc.GetStdOutPipe());
								using var dos = new DataOutputStream(file.Create());
								string? line;
								while ((line = dis.ReadLine()) != null) dos.PutString(line + "\n");
								status = 0;
							}
						}
						catch (Exception e)
						{
							Warning(e.Message);
						}
					}
					if (status == 0)
					{
						cmdline = dynamicUserCmdline.ToList();
						cmdline.Add("git"); cmdline.Add("merge"); cmdline.Add("-q");
						status = LaunchSubprocess(launcher, cmdline);
					}
					if (status == 0) return pkgdir;
					// remove and re-clone
					launcher.WorkingDirectory = realAurBuildDir;
					cmdline = dynamicUserCmdline.ToList();
					cmdline.Add("rm"); cmdline.Add("-rf"); cmdline.Add(pkgdir.GetPath());
					LaunchSubprocess(launcher, cmdline);
					cmdline = dynamicUserCmdline.ToList();
					cmdline.Add("git"); cmdline.Add("clone"); cmdline.Add("-q"); cmdline.Add("--depth=1");
					cmdline.Add($"https://aur.archlinux.org/{pkgname}.git");
				}
			}
			else
			{
				launcher.WorkingDirectory = realAurBuildDir;
				cmdline = dynamicUserCmdline.ToList();
				cmdline.Add("git"); cmdline.Add("clone"); cmdline.Add("-q"); cmdline.Add("--depth=1");
				cmdline.Add($"https://aur.archlinux.org/{pkgname}.git");
			}
			status = LaunchSubprocess(launcher, cmdline, cancellable);
			if (status == 0) return pkgdir;
			return null;
		}

		public async Task<GFile?> CloneBuildFilesAsync(string pkgname, bool overwriteFiles, Cancellable? cancellable = null)
		{
			return await Task.Run(() => CloneBuildFiles(pkgname, overwriteFiles, cancellable));
		}

		List<string> GetDynamicUserCmdline(string cwd)
		{
			var cmdline = new List<string>();
			if (Posix.GetEuid() == 0)
			{
				cmdline.Add("systemd-run");
				cmdline.Add("--service-type=oneshot");
				cmdline.Add("--pipe");
				cmdline.Add("--wait");
				cmdline.Add("--pty");
				cmdline.Add("--property=DynamicUser=yes");
				cmdline.Add("--property=CacheDirectory=pamac");
				cmdline.Add($"--property=WorkingDirectory={cwd}");
			}
			return cmdline;
		}

		public bool RegenerateSrcinfo(string pkgname, Cancellable? cancellable = null)
		{
			string realAurBuildDir = GetRealAurBuildDir();
			string pkgdirName = PathCompat.BuildFilename(realAurBuildDir, pkgname);
			var srcinfo = GFile.NewForPath(PathCompat.BuildFilename(pkgdirName, ".SRCINFO"));
			var pkgbuild = GFile.NewForPath(PathCompat.BuildFilename(pkgdirName, "PKGBUILD"));
			if (srcinfo.QueryExists())
			{
				try
				{
					DateTime? srcinfoTime = srcinfo.GetModificationDateTime();
					DateTime? pkgbuildTime = pkgbuild.GetModificationDateTime();
					if (srcinfoTime > pkgbuildTime) return true;
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
			}
			var launcher = new System.Diagnostics.ProcessStartInfo
			{
				WorkingDirectory = pkgdirName,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			try
			{
				var cmdline = GetDynamicUserCmdline(pkgdirName);
				cmdline.Add("makepkg"); cmdline.Add("--printsrcinfo");
				using var p = new System.Diagnostics.Process { StartInfo = launcher };
				foreach (var arg in cmdline) p.StartInfo.ArgumentList.Add(arg);
				p.Start();
				if (cancellable != null && cancellable.IsCancelled) { p.Kill(); return false; }
				string output = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				if (p.ExitCode == 0)
				{
					try
					{
						using var dos = new DataOutputStream(
							srcinfo.QueryExists()
								? srcinfo.Replace(null, false, FileCreateFlags.None)
								: srcinfo.Create(FileCreateFlags.None));
						dos.PutString(output);
						return true;
					}
					catch (Exception e)
					{
						Warning(e.Message);
					}
				}
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return false;
		}

		public async Task<bool> RegenerateSrcinfoAsync(string pkgname, Cancellable? cancellable = null)
		{
			return await Task.Run(() => RegenerateSrcinfo(pkgname, cancellable));
		}

		public AURPackage? GetAurPkg(string pkgname)
		{
			if (!Config.EnableAur) return null;
			if (_aurPkgsCache.TryGetValue(pkgname, out var cached)) return cached;
			var aurInfos = _aurPlugin.GetInfos(pkgname);
			if (aurInfos == null) return null;
			var localPkg = _alpmHandle!.LocalDb.GetPkg(pkgname);
			var newPkg = new AURPackageLinked();
			newPkg.InitialiseFromAurInfos(aurInfos, localPkg, this);
			_aurPkgsCache[newPkg.Id] = newPkg;
			return newPkg;
		}

		public async Task<AURPackage?> GetAurPkgAsync(string pkgname)
		{
			return await Task.Run(() => GetAurPkg(pkgname));
		}

		void GetAurPkgsReal(List<string> pkgnames, Dictionary<string, AURPackage?> data)
		{
			foreach (var pkgname in pkgnames) data[pkgname] = null;
			var aurInfosList = _aurPlugin.GetMultiInfos(pkgnames);
			foreach (var aurInfos in aurInfosList)
			{
				string name = aurInfos.Name;
				if (!_aurPkgsCache.TryGetValue(name, out var pkg))
				{
					var localPkg = _alpmHandle!.LocalDb.GetPkg(name);
					pkg = new AURPackageLinked();
					pkg.InitialiseFromAurInfos(aurInfos, localPkg, this);
					_aurPkgsCache[pkg.Id] = pkg;
				}
				data[name] = pkg;
			}
		}

		public Dictionary<string, AURPackage?> GetAurPkgs(List<string> pkgnames)
		{
			var data = new Dictionary<string, AURPackage?>();
			if (!Config.EnableAur) return data;
			GetAurPkgsReal(pkgnames, data);
			return data;
		}

		public async Task<Dictionary<string, AURPackage?>> GetAurPkgsAsync(List<string> pkgnames)
		{
			var data = new Dictionary<string, AURPackage?>();
			if (!Config.EnableAur) return data;
			await Task.Run(() => GetAurPkgsReal(pkgnames, data));
			return data;
		}

		public void RefreshTmpFilesDbs()
		{
			var tmpFilesHandle = _alpmConfig.GetHandle(true, true);
			if (tmpFilesHandle != null)
			{
				_alpmConfig.RegisterSyncdbs(tmpFilesHandle);
				tmpFilesHandle.UpdateDbs(tmpFilesHandle.SyncDbs, 0);
			}
		}

		public async Task RefreshTmpFilesDbsAsync()
		{
			await Task.Run(RefreshTmpFilesDbs);
		}

		public DateTime? GetLastRefreshTime()
		{
			string timestampPath = PathCompat.BuildFilename(_alpmHandle!.DbPath!, "sync", "refresh_timestamp");
			try
			{
				var timestampFile = GFile.NewForPath(timestampPath);
				if (timestampFile.QueryExists()) return timestampFile.GetModificationDateTime()?.ToLocalTime();
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return null;
		}

		long GetLastRefreshAge()
		{
			DateTime? lastModified = GetLastRefreshTime();
			if (lastModified == null) return long.MaxValue;
			var now = DateTime.Now;
			return (long)(now - lastModified.Value).TotalMilliseconds;
		}

		public bool NeedRefresh()
		{
			long elapsedTime = GetLastRefreshAge();
			if (elapsedTime < TimeSpan.TicksPerHour) return false;
			long elapsedHours = elapsedTime / TimeSpan.TicksPerHour;
			return elapsedHours >= (long)Config.RefreshPeriod;
		}

		void GetUpdatesReal(Updates updates)
		{
			var reposUpdates = updates.ReposUpdates;
			var ignoredReposUpdates = updates.IgnoredReposUpdates;
			var localPkgs = new List<string>();
			var vcsLocalPkgs = new List<string>();
			foreach (var installedPkg in _alpmHandle!.LocalDb.PkgCache)
			{
				var candidate = installedPkg.GetNewVersion(_alpmHandle.SyncDbs);
				if (candidate != null)
				{
					if (_alpmHandle.ShouldIgnore(installedPkg) == 1 || _alpmHandle.ShouldIgnore(candidate) == 1)
						ignoredReposUpdates.Add(InitialisePkgData(installedPkg, candidate));
					else
						reposUpdates.Add(InitialisePkgData(installedPkg, candidate));
				}
				else
				{
					if (Config.CheckAurUpdates)
					{
						var pkg = GetSyncpkg(_alpmHandle, installedPkg.Name);
						if (pkg == null)
						{
							if (Config.CheckAurVcsUpdates && IsVcsPkg(installedPkg.Name))
							{
								if (_alpmHandle.ShouldIgnore(installedPkg) == 0)
								{
									localPkgs.Add(installedPkg.Name);
									vcsLocalPkgs.Add(installedPkg.Name);
								}
							}
							else localPkgs.Add(installedPkg.Name);
						}
					}
				}
			}
			var flatpakUpdates = updates.FlatpakUpdates;
			if (Config.CheckFlatpakUpdates) _flatpakPlugin.GetFlatpakUpdates(ref flatpakUpdates);
			if (Config.CheckAurUpdates)
			{
				if (Config.CheckAurVcsUpdates) RefreshVcsSources(vcsLocalPkgs);
				GetUpdatesProgress?.Invoke(95);
				GetAurUpdatesReal(_aurPlugin.GetMultiInfos(localPkgs), vcsLocalPkgs, updates);
				GetUpdatesProgress?.Invoke(100);
			}
			else GetUpdatesProgress?.Invoke(100);
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

		static bool IsVcsPkg(string name) =>
			name.EndsWith("-git") || name.EndsWith("-svn") || name.EndsWith("-bzr") || name.EndsWith("-hg");

		void RefreshVcsSources(List<string> vcsLocalPkgs)
		{
			foreach (var pkgname in vcsLocalPkgs)
			{
				var aurInfos = _aurPlugin.GetInfos(pkgname);
				if (aurInfos == null) continue;
				GFile? cloneDir = CloneBuildFiles(aurInfos.PackageBase!, false, null);
				if (cloneDir != null)
				{
					var launcher = new System.Diagnostics.ProcessStartInfo
					{
						WorkingDirectory = cloneDir.GetPath(),
						UseShellExecute = false,
						RedirectStandardOutput = false,
						RedirectStandardError = false
					};
					var cmdline = GetDynamicUserCmdline(cloneDir.GetPath());
					cmdline.Add("makepkg"); cmdline.Add("--nobuild"); cmdline.Add("--noprepare");
					cmdline.Add("--nodeps"); cmdline.Add("--skipinteg");
					int status = LaunchSubprocess(launcher, cmdline);
					if (status == 0) RegenerateSrcinfo(cloneDir.GetBasename(), null);
				}
			}
		}

		Dictionary<string, string> GetVcsLastVersion(List<string> vcsLocalPkgs)
		{
			var pkgnamesTable = new Dictionary<string, string>();
			string realAurBuildDir = GetRealAurBuildDir();
			foreach (var pkgname in vcsLocalPkgs)
			{
				if (_aurVcsPkgs.ContainsKey(pkgname)) continue;
				var pkgdir = GFile.NewForPath(PathCompat.BuildFilename(realAurBuildDir, pkgname));
				if (!pkgdir.QueryExists()) continue;
				var srcinfo = pkgdir.GetChild(".SRCINFO");
				try
				{
					var version = new StringBuilder("");
					using var dis = new DataInputStream(srcinfo.Read());
					string? line;
					while ((line = dis.ReadLine()) != null)
					{
						if (line.Contains("pkgver = "))
							version.Append(line.Split(" = ", 2)[1]);
						else if (line.Contains("pkgrel = "))
							version.Append("-").Append(line.Split(" = ", 2)[1]);
						else if (line.Contains("epoch = "))
							version.Insert(0, line.Split(" = ", 2)[1] + ":");
						else if (line.Contains("pkgname = "))
						{
							string pkgnameFound = line.Split(" = ", 2)[1];
							if (vcsLocalPkgs.Contains(pkgnameFound)) pkgnamesTable[pkgnameFound] = version.ToString();
						}
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
					continue;
				}
			}
			return pkgnamesTable;
		}

		void GetAurUpdatesReal(List<AURInfos> aurInfosList, List<string> vcsLocalPkgs, Updates updates)
		{
			var aurUpdates = updates.AurUpdates;
			var outOfDate = updates.OutOfDate;
			var ignoredAurUpdates = updates.IgnoredAurUpdates;
			Dictionary<string, string> vcsVersions = null!;
			if (Config.CheckAurVcsUpdates) vcsVersions = GetVcsLastVersion(vcsLocalPkgs);
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
					if (!_aurVcsPkgs.TryGetValue(name, out aurPkg))
					{
						newVersion = vcsVersions.TryGetValue(name, out var vcs) ? vcs : aurInfos.Version;
						aurPkg = new AURPackageLinked();
						aurPkg.InitialiseFromAurInfos(aurInfos, localPkg, this, true);
						aurPkg.Version = newVersion;
						_aurVcsPkgs[name] = aurPkg;
					}
					else newVersion = aurPkg.Version;
				}
				else
				{
					newVersion = aurInfos.Version;
					aurPkg = new AURPackageLinked();
					aurPkg.InitialiseFromAurInfos(aurInfos, localPkg, this, true);
					aurPkg.Version = newVersion;
				}
				if (Alpm.PkgVerCmp(newVersion, oldVersion) == 1)
				{
					if (_alpmHandle.ShouldIgnore(localPkg) == 1) ignoredAurUpdates.Add(aurPkg);
					else aurUpdates.Add(aurPkg);
				}
				else if (aurInfos.OutOfDate != null) outOfDate.Add(aurPkg);
			}
		}

		internal async Task<Updates> GetAurUpdatesAsync(HashSet<string> ignorepkgs)
		{
			var updates = new Updates();
			await Task.Run(() =>
			{
				var localPkgs = new List<string>();
				var vcsLocalPkgs = new List<string>();
				foreach (var name in ignorepkgs) _alpmHandle!.AddIgnorepkg(name);
				foreach (var installedPkg in _alpmHandle!.LocalDb.PkgCache)
				{
					var pkg = GetSyncpkg(_alpmHandle, installedPkg.Name);
					if (pkg == null)
					{
						if (Config.CheckAurVcsUpdates && IsVcsPkg(installedPkg.Name))
						{
							if (_alpmHandle.ShouldIgnore(installedPkg) == 0)
							{
								localPkgs.Add(installedPkg.Name);
								vcsLocalPkgs.Add(installedPkg.Name);
							}
						}
						else localPkgs.Add(installedPkg.Name);
					}
				}
				GetAurUpdatesReal(_aurPlugin.GetMultiInfos(localPkgs), vcsLocalPkgs, updates);
				foreach (var name in ignorepkgs) _alpmHandle.RemoveIgnorepkg(name);
			});
			return updates;
		}

		// ---- Snap ----
		public async Task<List<SnapPackage>> SearchSnapsAsync(string searchString)
		{
			var pkgs = new List<SnapPackage>();
			if (Config.EnableSnap)
				await Task.Run(() => _snapPlugin.SearchSnaps(searchString.ToLowerInvariant(), ref pkgs));
			return pkgs;
		}

		public bool IsInstalledSnap(string name) =>
			Config.EnableSnap && _snapPlugin.IsInstalledSnap(name);

		public async Task<SnapPackage?> GetSnapAsync(string name)
		{
			SnapPackage? pkg = null;
			if (Config.EnableSnap) await Task.Run(() => pkg = _snapPlugin.GetSnap(name));
			return pkg;
		}

		public async Task<List<SnapPackage>> GetInstalledSnapsAsync()
		{
			var pkgs = new List<SnapPackage>();
			if (Config.EnableSnap) await Task.Run(() => _snapPlugin.GetInstalledSnaps(ref pkgs));
			return pkgs;
		}

		public async Task<string> GetInstalledSnapIconAsync(string name)
		{
			string icon = "";
			if (Config.EnableSnap) await Task.Run(() => icon = _snapPlugin.GetInstalledSnapIcon(name));
			return icon;
		}

		public async Task<List<SnapPackage>> GetCategorySnapsAsync(string category)
		{
			var pkgs = new List<SnapPackage>();
			if (Config.EnableSnap) await Task.Run(() => _snapPlugin.GetCategorySnaps(category, ref pkgs));
			return pkgs;
		}

		// ---- Flatpak ----
		public void RefreshFlatpakAppstreamData()
		{
			if (Config.EnableFlatpak) _flatpakPlugin.RefreshAppstreamData();
		}

		public async Task RefreshFlatpakAppstreamDataAsync()
		{
			if (Config.EnableFlatpak) await Task.Run(() => _flatpakPlugin.RefreshAppstreamData());
		}

		public List<string> GetFlatpakRemotesNames()
		{
			var list = new List<string>();
			if (Config.EnableFlatpak) _flatpakPlugin.GetRemotesNames(ref list);
			return list;
		}

		public async Task<List<FlatpakPackage>> GetInstalledFlatpaksAsync()
		{
			var pkgs = new List<FlatpakPackage>();
			if (Config.EnableFlatpak) await Task.Run(() => _flatpakPlugin.GetInstalledFlatpaks(ref pkgs));
			return pkgs;
		}

		public async Task<List<FlatpakPackage>> SearchFlatpaksAsync(string searchString)
		{
			var pkgs = new List<FlatpakPackage>();
			if (Config.EnableFlatpak)
				await Task.Run(() => _flatpakPlugin.SearchFlatpaks(searchString.ToLowerInvariant(), ref pkgs));
			return pkgs;
		}

		public bool IsInstalledFlatpak(string name) =>
			Config.EnableFlatpak && _flatpakPlugin.IsInstalledFlatpak(name);

		public async Task<FlatpakPackage?> GetFlatpakAsync(string id)
		{
			FlatpakPackage? pkg = null;
			if (Config.EnableFlatpak) await Task.Run(() => pkg = _flatpakPlugin.GetFlatpak(id));
			return pkg;
		}

		public async Task<List<FlatpakPackage>> GetCategoryFlatpaksAsync(string category)
		{
			var pkgs = new List<FlatpakPackage>();
			if (Config.EnableFlatpak) await Task.Run(() => _flatpakPlugin.GetCategoryFlatpaks(category, ref pkgs));
			return pkgs;
		}
	}
}
