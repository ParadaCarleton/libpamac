// libpamac — C# port
//
// AlpmUtils: the transaction engine that drives libalpm for refreshes,
// downloads and installs/removes, plus all progress/event forwarding.
// Faithful port of src/alpm_utils.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LibAlpm;
using Pamac.Compat;
using static Pamac.Compat.Gettext;
using static Pamac.Compat.Log;

namespace Pamac
{
	internal sealed class AlpmUtils
	{
		string _sender = "";
		readonly Config _config;
		public AlpmConfig AlpmConfig { get; private set; }
		string _tmpPath;
		public GFile Lockfile = null!;

		// run transaction data
		public Handle? AlpmHandle;
		byte _commitRetries;
		string _currentStatus = "";
		bool _sysupgrade;
		bool _enableDowngrade;
		bool _simpleInstall;
		bool _noConfirmCommit;
		bool _keepBuiltPkgs;
		int _transFlags;
		readonly HashSet<string> _toInstall = new HashSet<string>();
		readonly HashSet<string> _depsToInstall = new HashSet<string>();
		readonly HashSet<string> _toRemove = new HashSet<string>();
		readonly HashSet<string> _requiredToRemove = new HashSet<string>();
		readonly HashSet<string> _orphansToRemove = new HashSet<string>();
		readonly HashSet<string> _conflictsToRemove = new HashSet<string>();
		readonly HashSet<string> _localPaths = new HashSet<string>();
		readonly HashSet<string> _remotePaths = new HashSet<string>();
		readonly HashSet<string> _toBuild = new HashSet<string>();
		readonly HashSet<string> _checkedDeps = new HashSet<string>();
		readonly Dictionary<string, string> _toInstallAsDep = new Dictionary<string, string>();
		readonly HashSet<string> _ignorePkgs = new HashSet<string>();
		readonly HashSet<string> _overwriteFiles = new HashSet<string>();
		readonly HashSet<string> _toSyncFirst = new HashSet<string>();

		public Cancellable Cancellable { get; } = new Cancellable();
		public bool DownloadingUpdates { get; private set; }

		// progress data
		public string CurrentFilename = "";
		public string CurrentAction = "";
		public double CurrentProgress;
		public List<string> Unresolvables = new List<string>();

		// download data
		public ulong TotalDownload;
		public ulong AlreadyDownloaded;
		public readonly Dictionary<string, ulong?> MultiProgress = new Dictionary<string, ulong?>();
		readonly System.Diagnostics.Stopwatch _rateTimer = new System.Diagnostics.Stopwatch();
		readonly Queue<double> _downloadRates = new Queue<double>();
		double _downloadRate;

		// signals
		public event Func<string, List<string>, int>? ChooseProvider;
		public event Action<string>? StartDownloading;
		public event Action<string>? StopDownloading;
		public event Action<string, string>? EmitAction;
		public event Action<string, string, string, double>? EmitActionProgress;
		public event Action<string, string, string, double>? EmitDownloadProgress;
		public event Action<string, string, string, string, double>? EmitHookProgress;
		public event Action<string, string>? EmitScriptOutput;
		public event Action<string, string>? EmitWarning;
		public event Action<string, string, List<string>>? EmitError;
		public event Action<string, bool>? ImportantDetailsOutpout;

		public AlpmUtils(Config config)
		{
			_config = config;
			AlpmConfig = config.AlpmConfig;
			_tmpPath = string.Format("/tmp/pamac-{0}", Environment.UserName);
			Unresolvables = new List<string>();
			CurrentFilename = "";
			CurrentAction = "";
			_rateTimer.Reset();
			Cancellable = new Cancellable();
			DownloadingUpdates = false;
			CheckOldLock();
		}

		public int DoChooseProvider(string depend, List<string> providers) =>
			ChooseProvider?.Invoke(depend, providers) ?? 0;

		void DoStartDownloading() => StartDownloading?.Invoke(_sender);
		void DoStopDownloading() => StopDownloading?.Invoke(_sender);
		public void DoEmitAction(string action) => EmitAction?.Invoke(_sender, action);
		void DoEmitActionProgress(string action, string status, double progress) =>
			EmitActionProgress?.Invoke(_sender, action, status, progress);
		void DoEmitDownloadProgress(string action, string status, double progress) =>
			EmitDownloadProgress?.Invoke(_sender, action, status, progress);
		void DoEmitHookProgress(string action, string details, string status, double progress) =>
			EmitHookProgress?.Invoke(_sender, action, details, status, progress);
		public void DoEmitScriptOutput(string message) => EmitScriptOutput?.Invoke(_sender, message);
		void DoEmitWarning(string message) => EmitWarning?.Invoke(_sender, message);
		void DoEmitError(string message, List<string> details) => EmitError?.Invoke(_sender, message, details);
		void DoImportantDetailsOutpout(bool mustShow) => ImportantDetailsOutpout?.Invoke(_sender, mustShow);

		void CheckOldLock()
		{
			var alpmHandle = GetHandle(false, false, false);
			if (alpmHandle == null) return;
			Lockfile = GFile.NewForPath(alpmHandle.Lockfile);
			if (Lockfile.QueryExists())
			{
				try
				{
					int status = Spawn.SpawnCommandLineSync(
						$"stat -c %Y {alpmHandle.Lockfile}", out string output, out _);
					if (status == 0)
					{
						string[] splitted = output.Split('\n');
						if (splitted.Length == 2 && ulong.TryParse(splitted[0], out ulong lockfileTime))
						{
							int bootStatus = Spawn.SpawnCommandLineSync("cat /proc/stat", out string bootOutput, out _);
							if (bootStatus == 0)
							{
								foreach (var line in bootOutput.Split('\n'))
								{
									if (!line.Contains("btime")) continue;
									string[] spaceSplitted = line.Split(' ');
									if (spaceSplitted.Length == 2 && ulong.TryParse(spaceSplitted[1], out ulong bootTime))
									{
										if (lockfileTime < bootTime)
										{
											try { Lockfile.Delete(); }
											catch (Exception e) { Warning(e.Message); }
										}
									}
								}
							}
						}
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
			}
		}

		public Handle? GetHandle(bool filesDb = false, bool tmpDb = false, bool callbacks = true)
		{
			AlpmConfig.Reload();
			var alpmHandle = AlpmConfig.GetHandle(filesDb, tmpDb);
			if (alpmHandle == null)
			{
				var details = new List<string> { DGettext(null, "Failed to initialize alpm library") };
				DoEmitError("Alpm Error", details);
				return null;
			}
			if (callbacks)
			{
				// callbacks are wired through the Handle's native layer; see cb_* methods.
			}
			AlpmConfig.RegisterSyncdbs(alpmHandle);
			return alpmHandle;
		}

		public bool SetPkgReason(string sender, string pkgname, uint reason)
		{
			_sender = sender;
			var alpmHandle = GetHandle(false, false, false);
			if (alpmHandle == null) return false;
			var pkg = alpmHandle.LocalDb.GetPkg(pkgname);
			if (pkg != null)
			{
				if (alpmHandle.TransInit(0) == 0)
				{
					pkg.Reason = (PackageReason)reason;
					alpmHandle.TransRelease();
					return true;
				}
			}
			return false;
		}

		public bool CleanCache(string[] filenames)
		{
			foreach (var filename in filenames)
			{
				var file = GFile.NewForPath(filename);
				try { file.Delete(); }
				catch (Exception e) { Warning(e.Message); }
			}
			return true;
		}

		internal bool CleanBuildFiles(string aurBuildDir)
		{
			var buildDirectory = GFile.NewForPath(aurBuildDir);
			if (!buildDirectory.QueryExists()) return true;
			try
			{
				foreach (var (filename, _, _, _) in buildDirectory.EnumerateChildren())
				{
					if (filename == "packages-meta-ext-v1.json.gz") continue;
					string absoluteFilename = PathCompat.BuildFilename(buildDirectory.GetPath(), filename);
					Spawn.SpawnCommandLineSync("rm -rf " + absoluteFilename);
				}
				return true;
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return false;
		}

		Package? GetSyncpkg(Handle? alpmHandle, string name)
		{
			foreach (var db in alpmHandle!.SyncDbs)
			{
				var pkg = db.GetPkg(name);
				if (pkg != null) return pkg;
			}
			return null;
		}

		bool UpdateDbs(Handle? alpmHandle, int force)
		{
			bool success = true;
			if (alpmHandle!.UpdateDbs(alpmHandle.SyncDbs, force) < 0)
			{
				success = false;
				if (alpmHandle.Errno() == Errno.HandleLock)
				{
					try { Spawn.SpawnCommandLineSync($"rm -f {_tmpPath}/dbs/db.lck"); }
					catch (Exception e) { Warning(e.Message); }
				}
				DoEmitWarning(Alpm.Strerror(alpmHandle.Errno()));
			}
			return success;
		}

		public bool TransRefresh(string sender, bool forceRefresh)
		{
			_sender = sender;
			DoEmitAction(DGettext(null, "Synchronizing package databases") + "...");
			WriteLogFile("synchronizing package lists");
			Cancellable.Reset();
			int force = forceRefresh ? 1 : 0;
			if (forceRefresh)
			{
				try { Spawn.SpawnCommandLineSync($"bash -c 'rm -rf {_tmpPath}/dbs'"); }
				catch (Exception e) { Warning(e.Message); }
			}
			AlpmHandle = GetHandle(false, false);
			if (AlpmHandle == null) return false;
			bool success = UpdateDbs(AlpmHandle, force);
			if (Cancellable.IsCancelled) return false;
			if (forceRefresh)
			{
				AlpmHandle = GetHandle(true, false);
				if (AlpmHandle != null) UpdateDbs(AlpmHandle, force);
			}
			if (Cancellable.IsCancelled) return false;
			else if (!success) DoEmitWarning(DGettext(null, "Failed to synchronize databases"));
			if (success)
			{
				try
				{
					string timestampPath = PathCompat.BuildFilename(AlpmHandle.DbPath!, "sync", "refresh_timestamp");
					Spawn.SpawnCommandLineSync("touch " + timestampPath);
				}
				catch (Exception e) { Warning(e.Message); }
			}
			CurrentFilename = "";
			return true;
		}

		public bool TransRefreshFiles(string sender, bool forceRefresh)
		{
			_sender = sender;
			Cancellable.Reset();
			AlpmHandle = GetHandle(true, false);
			if (AlpmHandle == null) return false;
			int force = forceRefresh ? 1 : 0;
			UpdateDbs(AlpmHandle, force);
			if (Cancellable.IsCancelled) return false;
			return true;
		}

		public bool TransRefreshAur(string sender, bool forceRefresh)
		{
			_sender = sender;
			Cancellable.Reset();
			AlpmHandle = AlpmConfig.GetHandle(false, false, false);
			if (AlpmHandle == null) return false;
			AlpmHandle.Dbext = ".json.gz";
			var db = AlpmHandle.RegisterSyncDb("packages-meta-ext-v1", Signature.Level.UseDefault);
			string? id = GetOsId();
			db.AddServer(id == "manjaro" ? "https://aur.manjaro.org" : "https://aur.archlinux.org");
			db.Usage = Usage.All;
			int force = forceRefresh ? 1 : 0;
			UpdateDbs(AlpmHandle, force);
			if (Cancellable.IsCancelled) return false;
			return true;
		}

		void AddIgnorePkgs(Handle? alpmHandle)
		{
			foreach (var name in _ignorePkgs) alpmHandle!.AddIgnorepkg(name);
		}

		void RemoveIgnorePkgs(Handle? alpmHandle)
		{
			foreach (var name in _ignorePkgs) alpmHandle!.RemoveIgnorepkg(name);
		}

		void AddOverwriteFiles(Handle? alpmHandle)
		{
			foreach (var name in _overwriteFiles) alpmHandle!.AddOverwriteFile(name);
		}

		void RemoveOverwriteFiles(Handle? alpmHandle)
		{
			foreach (var name in _overwriteFiles) alpmHandle!.RemoveOverwriteFile(name);
		}

		AlpmPackage InitialisePkg(Handle? alpmHandle, Package alpmPkg)
		{
			Package? localPkg;
			Package? syncPkg;
			if (alpmPkg.Origin == PackageFrom.LocalDb)
			{
				localPkg = alpmPkg;
				syncPkg = GetSyncpkg(alpmHandle, alpmPkg.Name);
			}
			else if (alpmPkg.Origin == PackageFrom.SyncDb)
			{
				localPkg = alpmHandle!.LocalDb.GetPkg(alpmPkg.Name);
				syncPkg = alpmPkg;
			}
			else
			{
				localPkg = alpmHandle!.LocalDb.GetPkg(alpmPkg.Name);
				syncPkg = null;
			}
			return new AlpmPackageStatic(localPkg, syncPkg);
		}

		public bool DownloadUpdates(string sender)
		{
			_sender = sender;
			DownloadingUpdates = true;
			AlpmHandle = GetHandle(false, false);
			if (AlpmHandle == null) return false;
			AlpmHandle.ParallelDownloads = (uint)_config.MaxParallelDownloads;
			Cancellable.Reset();
			bool success = false;
			if (AlpmHandle.TransInit((int)TransFlag.DownloadOnly) == 0)
			{
				if (AlpmHandle.TransSysUpgrade(0) == 0)
				{
					if (AlpmHandle.TransPrepare(out _) == 0)
					{
						if (AlpmHandle.TransCommit(out _) == 0) success = true;
					}
				}
				AlpmHandle.TransRelease();
			}
			DownloadingUpdates = false;
			if (success && _config.OfflineUpgrade)
			{
				try { Spawn.SpawnCommandLineSync("touch /system-update"); }
				catch (Exception e) { Warning(e.Message); }
			}
			return success;
		}

		bool TransInit(Handle? alpmHandle, int flags, bool emitError = true)
		{
			Cancellable.Reset();
			if (alpmHandle!.TransInit(flags) == -1)
			{
				if (emitError)
				{
					var details = new List<string>();
					if (alpmHandle.Errno() != 0) details.Add(Alpm.Strerror(alpmHandle.Errno()));
					DoEmitError(DGettext(null, "Failed to init transaction"), details);
				}
				return false;
			}
			return true;
		}

		bool TransSysUpgrade(Handle? alpmHandle, bool emitError = true)
		{
			AddIgnorePkgs(alpmHandle);
			if (alpmHandle!.TransSysUpgrade(_enableDowngrade ? 1 : 0) == -1)
			{
				if (emitError)
				{
					var details = new List<string>();
					if (alpmHandle.Errno() != 0) details.Add(Alpm.Strerror(alpmHandle.Errno()));
					DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
				}
				return false;
			}
			foreach (var name in AlpmConfig.SyncFirsts)
			{
				var pkg = Alpm.FindSatisfier(alpmHandle.LocalDb.PkgCache, name);
				if (pkg != null)
				{
					var candidate = pkg.GetNewVersion(alpmHandle.SyncDbs);
					if (candidate != null) _toSyncFirst.Add(candidate.Name);
				}
			}
			return true;
		}

		bool TransAddPkgReal(Handle? alpmHandle, Package? pkg, bool emitError = true)
		{
			if (alpmHandle!.TransAddPkg(pkg!) == -1)
			{
				if (alpmHandle.Errno() == Errno.TransDupTarget) return true;
				if (emitError)
				{
					var details = new List<string>();
					if (alpmHandle.Errno() != 0) details.Add(Alpm.Strerror(alpmHandle.Errno()));
					DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
				}
				return false;
			}
			return true;
		}

		bool TransAddPkg(Handle? alpmHandle, string pkgname, bool emitError = true)
		{
			var pkg = alpmHandle!.FindDbsSatisfier(alpmHandle.SyncDbs, pkgname);
			if (pkg == null)
			{
				if (emitError)
				{
					var details = new List<string>();
					if (alpmHandle.Errno() == Errno.PkgIgnored) details.Add(Alpm.Strerror(alpmHandle.Errno()));
					else details.Add(string.Format(DGettext(null, "target not found: {0}"), pkgname));
					DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
				}
				return false;
			}
			bool success = TransAddPkgReal(alpmHandle, pkg, emitError);
			if (success)
			{
				if (pkg.Name.Contains("linux5") || pkg.Name.Contains("linux6"))
				{
					var installedKernels = new List<string>();
					var installedModules = new List<string>();
					foreach (var localPkg in alpmHandle.LocalDb.PkgCache)
					{
						if (localPkg.Name.Contains("linux5") || localPkg.Name.Contains("linux6"))
						{
							string[] split = localPkg.Name.Split('-', 2);
							if (!installedKernels.Contains(split[0])) installedKernels.Add(split[0]);
							if (split.Length == 2 && !installedModules.Contains(split[1])) installedModules.Add(split[1]);
						}
					}
					string[] pkgSplitted = pkg.Name.Split('-', 2);
					if (pkgSplitted.Length == 2)
					{
						foreach (var installedKernel in installedKernels)
						{
							string module = installedKernel + "-" + pkgSplitted[1];
							if (alpmHandle.LocalDb.GetPkg(module) == null)
							{
								var modulePkg = GetSyncpkg(alpmHandle, module);
								if (modulePkg != null) TransAddPkgReal(alpmHandle, modulePkg, emitError);
							}
						}
					}
					else if (pkgSplitted.Length == 1)
					{
						foreach (var installedModule in installedModules)
						{
							string module = pkgSplitted[0] + "-" + installedModule;
							var modulePkg = GetSyncpkg(alpmHandle, module);
							if (modulePkg != null) TransAddPkgReal(alpmHandle, modulePkg, emitError);
						}
					}
				}
			}
			return success;
		}

		public bool DownloadPkgs(string sender, string[] urls, ref List<string> dloadPaths)
		{
			_sender = sender;
			AlpmHandle = GetHandle(false, false);
			if (AlpmHandle == null) return false;
			AlpmHandle.ParallelDownloads = (uint)_config.MaxParallelDownloads;
			var urlsList = new List<string>(urls);
			int ret = AlpmHandle.FetchPkgUrl(urlsList, out var fetched);
			if (ret != 0) return false;
			if (Cancellable.IsCancelled) return false;
			foreach (var cachedir in AlpmHandle.CacheDirs)
			{
				foreach (var url in fetched)
				{
					string filename = System.IO.Path.GetFileName(url);
					string dloadPath = PathCompat.BuildFilename(cachedir, filename);
					var destfile = GFile.NewForPath(dloadPath);
					if (destfile.QueryExists()) dloadPaths.Add(dloadPath);
				}
			}
			if (dloadPaths.Count == 0 && !Cancellable.IsCancelled)
			{
				var details = new List<string> { DGettext(null, "failed to retrieve some files") };
				DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
				return false;
			}
			return true;
		}

		bool TransLoadPkg(Handle? alpmHandle, string path, Signature.Level siglevel, bool emitError = true)
		{
			var pkg = alpmHandle!.LoadTarball(path);
			if (alpmHandle.TransAddPkg(pkg) == -1)
			{
				if (alpmHandle.Errno() == Errno.TransDupTarget) return true;
				if (emitError)
				{
					var details = new List<string>();
					if (alpmHandle.Errno() != 0) details.Add($"{pkg.Name}: {Alpm.Strerror(alpmHandle.Errno())}");
					DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
				}
				return false;
			}
			return true;
		}

		bool TransRemovePkg(Handle? alpmHandle, string pkgname, bool emitError = true)
		{
			bool success = true;
			var pkg = alpmHandle!.LocalDb.GetPkg(pkgname);
			if (pkg == null)
			{
				if (emitError)
				{
					var details = new List<string> { string.Format(DGettext(null, "target not found: {0}"), pkgname) };
					DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
				}
				success = false;
			}
			else if (alpmHandle.TransRemovePkg(pkg) == -1)
			{
				if (alpmHandle.Errno() != Errno.TransDupTarget)
				{
					if (emitError)
					{
						var details = new List<string>();
						if (alpmHandle.Errno() != 0) details.Add($"{pkg.Name}: {Alpm.Strerror(alpmHandle.Errno())}");
						DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
					}
					success = false;
				}
			}
			return success;
		}

		bool TransPrepareReal(Handle? alpmHandle, bool emitError = true)
		{
			bool success = true;
			bool needRetry = false;
			List<object>? errData;
			if (alpmHandle!.TransPrepare(out errData) == -1)
			{
				var details = new List<string>();
				var errNo = alpmHandle.Errno();
				switch (errNo)
				{
					case Errno.Ok: break;
					case Errno.UnsatisfiedDeps:
						details.Add(Alpm.Strerror(errNo) + ":");
						var seenDeps = new HashSet<string>();
						foreach (var item in errData ?? new List<object>())
						{
							if (item is not DepMissing miss) continue;
							string depstring = miss.Depend.ComputeString();
							if (!seenDeps.Add(depstring)) continue;
							var transAdd = alpmHandle.TransToAdd() ?? new List<Package>();
							Package? pkg = transAdd.Find(p => p.Name == miss.CausingPkg);
							if (miss.CausingPkg == null)
							{
								details.Add("- " + string.Format(
									DGettext(null, "unable to satisfy dependency '{1}' required by {0}"),
									miss.Target, depstring));
							}
							else if (pkg != null)
							{
								if (_commitRetries < 1)
								{
									DoEmitWarning(DGettext(null, "Warning") + ": " + string.Format(
										DGettext(null, "installing {0} ({1}) breaks dependency '{3}' required by {2}"),
										miss.CausingPkg, pkg.Version, miss.Target, depstring));
									DoEmitWarning(DGettext(null, "Add {0} to remove").Replace("{0}", "{0}"));
									DoEmitWarning(string.Format(DGettext(null, "Add {0} to remove"), miss.Target));
									_requiredToRemove.Add(miss.Target);
									if (TransRemovePkg(alpmHandle, miss.Target)) needRetry = true;
								}
								else
								{
									details.Add("- " + string.Format(
										DGettext(null, "installing {0} ({1}) breaks dependency '{3}' required by {2}"),
										miss.CausingPkg, pkg.Version, miss.Target, depstring));
									details.Add("- " + string.Format(DGettext(null, "if possible, remove {0} and retry"), miss.Target));
								}
							}
							else
							{
								details.Add("- " + string.Format(
									DGettext(null, "removing {0} breaks dependency '{1}' required by {2}"),
									miss.CausingPkg, depstring, miss.Target));
							}
						}
						break;
					case Errno.ConflictingDeps:
						details.Add(Alpm.Strerror(errNo) + ":");
						foreach (var item in errData ?? new List<object>())
						{
							if (item is not Conflict conflict) continue;
							string detail = "- " + string.Format(
								DGettext(null, "{0} and {1} are in conflict"),
								conflict.Package1.Name, conflict.Package2.Name);
							if (conflict.Reason.Version != "") detail += " (" + conflict.Reason.ComputeString() + ")";
							details.Add(detail);
						}
						break;
					default:
						details.Add(Alpm.Strerror(errNo));
						break;
				}
				if (needRetry && _commitRetries < 1)
				{
					_commitRetries++;
					success = TransPrepareReal(alpmHandle, emitError);
				}
				else
				{
					TransRelease(alpmHandle);
					if (emitError) DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
					success = false;
				}
			}
			else if (emitError)
			{
				var details = new List<string>();
				bool foundLockedPkg = false;
				foreach (var pkg in alpmHandle.TransToRemove() ?? new List<Package>())
				{
					if (AlpmConfig.HoldPkgs.Contains(pkg.Name))
					{
						details.Add("- " + string.Format(
							DGettext(null, "{0} needs to be removed but it is a locked package"), pkg.Name));
						foundLockedPkg = true;
					}
				}
				if (foundLockedPkg)
				{
					DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
					TransRelease(alpmHandle);
					success = false;
				}
			}
			if (Cancellable.IsCancelled)
			{
				TransRelease(alpmHandle);
				return false;
			}
			return success;
		}

		void PrepareAurDb(Handle? alpmHandle)
		{
			try { Spawn.SpawnCommandLineSync($"cp {_tmpPath}/pamac_aur.db {alpmHandle!.DbPath}sync"); }
			catch (Exception e) { DoEmitWarning(e.Message); }
			foreach (var name in _toBuild)
			{
				string debugPkgName = name + "-debug";
				if (alpmHandle!.LocalDb.GetPkg(debugPkgName) != null) _toRemove.Add(debugPkgName);
			}
			const string develPkgname = "base-devel";
			if (Alpm.FindSatisfier(alpmHandle!.LocalDb.PkgCache, develPkgname) == null)
			{
				var pkg = alpmHandle.FindDbsSatisfier(alpmHandle.SyncDbs, develPkgname);
				if (pkg != null) _toInstall.Add(develPkgname);
			}
			else _toRemove.Remove(develPkgname);
		}

		void RemoveAurDb(Handle? alpmHandle)
		{
			try { Spawn.SpawnCommandLineSync($"rm -f {alpmHandle!.DbPath}sync/pamac_aur.db"); }
			catch (Exception e) { Warning(e.Message); }
		}

		public bool TransCheckPrepare(bool sysupgrade, bool enableDowngrade, bool simpleInstall, int transFlags,
			HashSet<string> toInstall, HashSet<string> toRemove, HashSet<string> localPaths, HashSet<string> remotePaths,
			HashSet<string> toBuild, HashSet<string> ignorepkgs, HashSet<string> overwriteFiles, ref TransactionSummary summary)
		{
			var tmpHandle = GetHandle(false, true, false);
			if (tmpHandle == null) return false;
			_sender = "";
			_sysupgrade = sysupgrade;
			_enableDowngrade = enableDowngrade;
			_simpleInstall = simpleInstall;
			_transFlags = transFlags | (int)TransFlag.NoLock;
			_toInstall.UnionWith(toInstall);
			_toRemove.UnionWith(toRemove);
			_localPaths.UnionWith(localPaths);
			_remotePaths.UnionWith(remotePaths);
			_toBuild.UnionWith(toBuild);
			_ignorePkgs.UnionWith(ignorepkgs);
			_overwriteFiles.UnionWith(overwriteFiles);
			DB? aurDb = null;
			if (toRemove.Count > 0) InternComputePkgsToRemove(tmpHandle);
			if (toInstall.Count > 0 || sysupgrade || toBuild.Count > 0 || localPaths.Count > 0 || remotePaths.Count > 0)
			{
				if (toBuild.Count > 0)
				{
					PrepareAurDb(tmpHandle);
					aurDb = tmpHandle.RegisterSyncDb("pamac_aur", 0);
					if (aurDb == null)
					{
						RemoveAurDb(tmpHandle);
						var details = new List<string> { Alpm.Strerror(tmpHandle.Errno()) };
						DoEmitError(DGettext(null, "Failed to initialize AUR database"), details);
						return false;
					}
				}
				InternComputePkgsToInstall(tmpHandle, aurDb);
			}
			if ((transFlags & (int)TransFlag.Recurse) != 0) InternComputeOrphansToRemove(tmpHandle);
			bool success = TransPrepare(tmpHandle, aurDb);
			if (success)
			{
				GetTransactionSummary(tmpHandle, ref summary);
				TransRelease(tmpHandle);
			}
			if (aurDb != null) RemoveAurDb(tmpHandle);
			TransReset();
			return success;
		}

		public bool TransRun(string sender, bool sysupgrade, bool enableDowngrade, bool simpleInstall,
			bool keepBuiltPkgs, int transFlags, string[] toInstall, string[] toRemove, string[] toLoadLocal,
			string[] toLoadRemote, string[] toInstallAsDep, string[] ignorepkgs, string[] overwriteFiles)
		{
			_sender = sender;
			_sysupgrade = sysupgrade;
			_enableDowngrade = enableDowngrade;
			_simpleInstall = simpleInstall;
			_noConfirmCommit = true;
			_keepBuiltPkgs = keepBuiltPkgs;
			_transFlags = transFlags;
			_transFlags &= ~(int)TransFlag.Cascade;
			_transFlags &= ~(int)TransFlag.Recurse;
			AlpmHandle = GetHandle(false, false, false);
			if (AlpmHandle == null) return false;
			AlpmHandle.ParallelDownloads = (uint)_config.MaxParallelDownloads;
			_toInstall.UnionWith(toInstall);
			_toRemove.UnionWith(toRemove);
			_localPaths.UnionWith(toLoadLocal);
			_remotePaths.UnionWith(toLoadRemote);
			foreach (var name in toInstallAsDep) _toInstallAsDep[name] = name;
			_ignorePkgs.UnionWith(ignorepkgs);
			_overwriteFiles.UnionWith(overwriteFiles);
			return TransRunReal(AlpmHandle);
		}

		bool TransRunReal(Handle? alpmHandle)
		{
			DB? aurDb = null;
			bool success = TransPrepare(alpmHandle, aurDb);
			if (success)
			{
				if ((alpmHandle!.TransToAdd()?.Count ?? 0) > 0 || (alpmHandle.TransToRemove()?.Count ?? 0) > 0)
				{
					success = TransCommit(alpmHandle);
				}
				else
				{
					TransRelease(alpmHandle);
					TransReset();
					success = true;
				}
			}
			return success;
		}

		void TransReset()
		{
			_commitRetries = 0;
			TotalDownload = 0;
			AlreadyDownloaded = 0;
			CurrentFilename = "";
			_toSyncFirst.Clear();
			_toInstall.Clear();
			_depsToInstall.Clear();
			_toRemove.Clear();
			_requiredToRemove.Clear();
			_orphansToRemove.Clear();
			_conflictsToRemove.Clear();
			_localPaths.Clear();
			_remotePaths.Clear();
			_toBuild.Clear();
			_checkedDeps.Clear();
			_ignorePkgs.Clear();
			_overwriteFiles.Clear();
			_toInstallAsDep.Clear();
			_noConfirmCommit = false;
		}

		void InternComputePkgsToRemove(Handle? alpmHandle)
		{
			int tmpTransFlags = (int)TransFlag.NoLock;
			if ((_transFlags & (int)TransFlag.Unneeded) != 0) tmpTransFlags |= (int)TransFlag.Unneeded;
			else if ((_transFlags & (int)TransFlag.Cascade) != 0)
			{
				_transFlags &= ~(int)TransFlag.Cascade;
				tmpTransFlags |= (int)TransFlag.Cascade;
			}
			bool success = TransInit(alpmHandle, tmpTransFlags, false);
			if (success)
			{
				foreach (var name in _toRemove)
				{
					success = TransRemovePkg(alpmHandle, name, false);
					if (!success) break;
				}
				if (success) success = TransPrepareReal(alpmHandle, false);
				else TransRelease(alpmHandle);
			}
			if (success)
			{
				foreach (var pkg in alpmHandle!.TransToRemove() ?? new List<Package>())
				{
					string name = pkg.Name;
					if (!_toRemove.Contains(name)) _requiredToRemove.Add(name);
				}
				TransRelease(alpmHandle);
			}
		}

		void RemoveInstallDepsInToRemove(Handle? alpmHandle, List<Package>? depsToCheck)
		{
			var depsToCheckNext = new List<Package>();
			foreach (var alpmPkg in depsToCheck ?? new List<Package>())
			{
				_checkedDeps.Add(alpmPkg.Name);
				var deps = new List<Depend>(alpmPkg.Depends);
				deps.AddRange(alpmPkg.OptDepends);
				foreach (var depend in deps)
				{
					var satisfier = Alpm.FindSatisfier(alpmHandle!.LocalDb.PkgCache, depend.ComputeString());
					if (satisfier == null) continue;
					if (_checkedDeps.Contains(satisfier.Name)) continue;
					if (_toRemove.Remove(satisfier.Name))
					{
						DoEmitScriptOutput(DGettext(null, "Warning") + ": " +
							string.Format(DGettext(null, "removing {0} from target list"), satisfier.Name));
					}
					depsToCheckNext.Add(satisfier);
				}
			}
			if (depsToCheckNext.Count > 0) RemoveInstallDepsInToRemove(alpmHandle, depsToCheckNext);
		}

		void InternComputePkgsToInstall(Handle? alpmHandle, DB? aurDb)
		{
			bool success = TransInit(alpmHandle, (int)TransFlag.NoLock, false);
			if (success && _sysupgrade)
			{
				success = TransSysUpgrade(alpmHandle, false);
				if (!success) TransRelease(alpmHandle);
			}
			if (success)
			{
				foreach (var name in _toInstall)
				{
					success = TransAddPkg(alpmHandle, name, false);
					if (!success) break;
				}
				if (success)
				{
					var dbs = new List<DB> { aurDb! };
					foreach (var name in _toBuild)
					{
						var pkg = alpmHandle!.FindDbsSatisfier(dbs, name);
						if (pkg == null) { success = false; break; }
						success = TransAddPkgReal(alpmHandle, pkg, false);
						if (!success) break;
					}
				}
				if (success)
				{
					foreach (var path in _localPaths)
					{
						success = TransLoadPkg(alpmHandle, path, alpmHandle!.LocalFileSigLevel, false);
						if (!success) break;
					}
				}
				if (success)
				{
					foreach (var path in _remotePaths)
					{
						success = TransLoadPkg(alpmHandle, path, alpmHandle!.RemoteFileSigLevel, false);
						if (!success) break;
					}
				}
				if (success) success = TransPrepareReal(alpmHandle, false);
				else TransRelease(alpmHandle);
			}
			if (success)
			{
				var toInstallCopy = new HashSet<string>(_toInstall);
				_toInstall.Clear();
				_toBuild.Clear();
				var depsToCheck = new List<Package>();
				foreach (var transPkg in alpmHandle!.TransToAdd() ?? new List<Package>())
				{
					if (transPkg.DB != null)
					{
						string name = transPkg.Name;
						if (transPkg.DB.Name == "pamac_aur") _toBuild.Add(name);
						else
						{
							if (toInstallCopy.Contains(name)) _toInstall.Add(name);
							else _depsToInstall.Add(name);
						}
					}
					if (_toRemove.Count > 0)
					{
						if (_toRemove.Remove(transPkg.Name))
						{
							DoEmitScriptOutput(DGettext(null, "Warning") + ": " +
								string.Format(DGettext(null, "removing {0} from target list"), transPkg.Name));
						}
						_checkedDeps.Add(transPkg.Name);
						var deps = new List<Depend>(transPkg.Depends);
						deps.AddRange(transPkg.OptDepends);
						foreach (var depend in deps)
						{
							var satisfier = Alpm.FindSatisfier(alpmHandle.LocalDb.PkgCache, depend.ComputeString());
							if (satisfier == null) continue;
							if (_checkedDeps.Contains(satisfier.Name)) continue;
							if (_toRemove.Remove(satisfier.Name))
							{
								DoEmitScriptOutput(DGettext(null, "Warning") + ": " +
									string.Format(DGettext(null, "removing {0} from target list"), satisfier.Name));
							}
							depsToCheck.Add(satisfier);
						}
					}
				}
				foreach (var pkg in alpmHandle.TransToRemove() ?? new List<Package>()) _conflictsToRemove.Add(pkg.Name);
				TransRelease(alpmHandle);
				if (depsToCheck.Count > 0) RemoveInstallDepsInToRemove(alpmHandle, depsToCheck);
			}
		}

		void CheckOrphansToRemove(Handle? alpmHandle, List<Package>? depsToCheck)
		{
			var depsToCheckNext = new List<Package>();
			foreach (var alpmPkg in depsToCheck ?? new List<Package>())
			{
				if (_checkedDeps.Contains(alpmPkg.Name)) continue;
				if (alpmPkg.Reason != PackageReason.Depend) continue;
				var requiredby = alpmPkg.ComputeRequiredBy();
				bool externDep = HasExternDep(alpmHandle, requiredby);
				if (!externDep)
				{
					var optionalfor = alpmPkg.ComputeOptionalFor();
					externDep = HasExternDep(alpmHandle, optionalfor);
					if (!externDep)
					{
						_orphansToRemove.Add(alpmPkg.Name);
						_checkedDeps.Add(alpmPkg.Name);
						foreach (var depend in alpmPkg.Depends)
						{
							var satisfier = Alpm.FindSatisfier(alpmHandle!.LocalDb.PkgCache, depend.ComputeString());
							if (satisfier == null) continue;
							if (!_toRemove.Contains(satisfier.Name) && !_requiredToRemove.Contains(satisfier.Name)
								&& !_orphansToRemove.Contains(satisfier.Name)) depsToCheckNext.Add(satisfier);
						}
					}
				}
			}
			if (depsToCheckNext.Count > 0) CheckOrphansToRemove(alpmHandle, depsToCheckNext);
		}

		bool HasExternDep(Handle? alpmHandle, List<string> requiredByList)
		{
			foreach (var r in requiredByList)
			{
				var satisfier = Alpm.FindSatisfier(alpmHandle!.LocalDb.PkgCache, r);
				if (satisfier != null)
				{
					if (!_toRemove.Contains(satisfier.Name) && !_requiredToRemove.Contains(satisfier.Name)
						&& !_orphansToRemove.Contains(satisfier.Name)) return true;
				}
			}
			return false;
		}

		void InternComputeOrphansToRemove(Handle? alpmHandle)
		{
			_transFlags &= ~(int)TransFlag.Recurse;
			_checkedDeps.Clear();
			var depsToCheck = new List<Package>();
			foreach (var name in _toRemove)
			{
				var transPkg = alpmHandle!.LocalDb.GetPkg(name);
				if (transPkg == null || _checkedDeps.Contains(transPkg.Name)) continue;
				_checkedDeps.Add(transPkg.Name);
				foreach (var depend in transPkg.Depends)
				{
					var satisfier = Alpm.FindSatisfier(alpmHandle.LocalDb.PkgCache, depend.ComputeString());
					if (satisfier != null && !_toRemove.Contains(satisfier.Name) && !_requiredToRemove.Contains(satisfier.Name))
						depsToCheck.Add(satisfier);
				}
			}
			foreach (var name in _requiredToRemove)
			{
				var transPkg = alpmHandle!.LocalDb.GetPkg(name);
				if (transPkg == null || _checkedDeps.Contains(transPkg.Name)) continue;
				_checkedDeps.Add(transPkg.Name);
				foreach (var depend in transPkg.Depends)
				{
					var satisfier = Alpm.FindSatisfier(alpmHandle.LocalDb.PkgCache, depend.ComputeString());
					if (satisfier != null && !_toRemove.Contains(satisfier.Name) && !_requiredToRemove.Contains(satisfier.Name))
						depsToCheck.Add(satisfier);
				}
			}
			if (depsToCheck.Count > 0) CheckOrphansToRemove(alpmHandle, depsToCheck);
		}

		bool TransPrepare(Handle? alpmHandle, DB? aurDb)
		{
			bool success = TransInit(alpmHandle, _transFlags);
			if (success && _sysupgrade) success = TransSysUpgrade(alpmHandle);
			if (success)
			{
				foreach (var name in _toInstall) { success = TransAddPkg(alpmHandle, name); if (!success) break; }
			}
			if (success)
			{
				foreach (var name in _depsToInstall) { success = TransAddPkg(alpmHandle, name); if (!success) break; }
			}
			if (_toBuild.Count > 0)
			{
				var dbs = new List<DB> { aurDb! };
				foreach (var name in _toBuild)
				{
					var pkg = alpmHandle!.FindDbsSatisfier(dbs, name);
					if (pkg == null)
					{
						var details = new List<string> { string.Format(DGettext(null, "target not found: {0}"), name) };
						DoEmitError(DGettext(null, "Failed to prepare transaction"), details);
						success = false;
						break;
					}
					success = TransAddPkgReal(alpmHandle, pkg);
					if (!success) break;
				}
			}
			if (success)
			{
				foreach (var name in _toRemove) { success = TransRemovePkg(alpmHandle, name); if (!success) break; }
			}
			if (success)
			{
				foreach (var name in _requiredToRemove) { success = TransRemovePkg(alpmHandle, name); if (!success) break; }
			}
			if (success)
			{
				foreach (var name in _orphansToRemove) { success = TransRemovePkg(alpmHandle, name); if (!success) break; }
			}
			if (success)
			{
				foreach (var path in _localPaths) { success = TransLoadPkg(alpmHandle, path, alpmHandle!.LocalFileSigLevel); if (!success) break; }
			}
			if (success)
			{
				foreach (var path in _remotePaths) { success = TransLoadPkg(alpmHandle, path, alpmHandle!.RemoteFileSigLevel); if (!success) break; }
			}
			if (success) success = TransPrepareReal(alpmHandle);
			else TransRelease(alpmHandle);
			return success;
		}

		void GetTransactionSummary(Handle? alpmHandle, ref TransactionSummary summary)
		{
			var checked = new Dictionary<string, int>();
			foreach (var transPkg in alpmHandle!.TransToAdd() ?? new List<Package>())
			{
				DB? db = transPkg.DB;
				string transPkgName = transPkg.Name;
				if (db != null && db.Name == "pamac_aur")
				{
					var pkg = InitialisePkg(alpmHandle, transPkg);
					if (!summary.AurPkgBasesToBuild.Contains(transPkg.PkgBase))
						summary.AurPkgBasesToBuild.Add(transPkg.PkgBase);
					if (pkg.InstalledVersion == null && !_toBuild.Contains(transPkgName))
					{
						// it is a new required dep — record the top requiredby package
						if (!pkg.RequiredBy.Contains(transPkg.PkgBase) && !string.IsNullOrEmpty(transPkg.PkgBase))
							pkg.RequiredBy.Add(transPkg.PkgBase);
					}
					summary.ToBuild.Add(pkg);
				}
				else
				{
					var pkg = InitialisePkg(alpmHandle, transPkg);
					if (pkg.InstalledVersion == null)
					{
						summary.ToInstall.Add(pkg);
					}
					else
					{
						int cmp = Alpm.PkgVerCmp(pkg.Version, pkg.InstalledVersion);
						if (cmp == 1) summary.ToUpgrade.Add(pkg);
						else if (cmp == 0) summary.ToReinstall.Add(pkg);
						else summary.ToDowngrade.Add(pkg);
					}
					if (db == null) summary.ToLoad.Add(transPkgName);
				}
			}
			checked.Clear();
			foreach (var transPkg in alpmHandle.TransToRemove() ?? new List<Package>())
			{
				string transPkgName = transPkg.Name;
				var pkg = InitialisePkg(alpmHandle, transPkg);
				if (_toRemove.Contains(transPkgName)) summary.ToRemove.Add(pkg);
				else if (_requiredToRemove.Contains(transPkgName)) summary.ToRemove.Add(pkg);
				else if (_orphansToRemove.Contains(transPkgName)) summary.ToRemove.Add(pkg);
				else summary.ConflictsToRemove.Add(pkg);
			}
		}

		bool NeedReboot(Handle? alpmHandle)
		{
			bool rebootNeeded = false;
			string[] prefix = { "linux-", "linux4", "linux5", "linux6", "nvidia-", "lib32-nvidia-", "systemd", "xf86-", "xorg-" };
			string[] contains = { "mesa", "wayland" };
			string[] full = { "cryptsetup" };
			string[] suffix = { "-ucode" };
			foreach (var pkg in alpmHandle!.TransToAdd() ?? new List<Package>())
			{
				foreach (var str in prefix) { if (pkg.Name.StartsWith(str)) { rebootNeeded = true; break; } }
				if (rebootNeeded) break;
				foreach (var str in contains) { if (pkg.Name.Contains(str)) { rebootNeeded = true; break; } }
				if (rebootNeeded) break;
				foreach (var str in full) { if (str == pkg.Name) { rebootNeeded = true; break; } }
				if (rebootNeeded) break;
				foreach (var str in suffix) { if (pkg.Name.EndsWith(str)) { rebootNeeded = true; break; } }
				if (rebootNeeded) break;
			}
			return rebootNeeded;
		}

		bool TransCommit(Handle? alpmHandle)
		{
			AddOverwriteFiles(alpmHandle);
			bool needRetry = false;
			bool success = false;
			bool rebootNeeded = false;
			if (_toSyncFirst.Count > 0)
			{
				TransRelease(alpmHandle);
				success = TransInit(alpmHandle, _transFlags);
				if (success)
				{
					foreach (var name in _toSyncFirst) { success = TransAddPkg(alpmHandle, name); if (!success) break; }
					if (success) success = TransPrepareReal(alpmHandle);
					if (success)
					{
						rebootNeeded = NeedReboot(alpmHandle);
						success = TransCommitReal(alpmHandle, ref needRetry);
					}
					TransRelease(alpmHandle);
					if (success)
					{
						foreach (var name in _toSyncFirst) _toInstall.Remove(name);
						success = TransInit(alpmHandle, _transFlags);
						if (success && _sysupgrade) success = TransSysUpgrade(alpmHandle);
						if (success)
						{
							foreach (var name in _toInstall) { success = TransAddPkg(alpmHandle, name); if (!success) break; }
						}
						if (success)
						{
							foreach (var name in _toRemove) { success = TransRemovePkg(alpmHandle, name); if (!success) break; }
						}
						if (success)
						{
							foreach (var path in _localPaths) { success = TransLoadPkg(alpmHandle, path, alpmHandle!.LocalFileSigLevel); if (!success) break; }
						}
						if (success)
						{
							foreach (var path in _remotePaths) { success = TransLoadPkg(alpmHandle, path, alpmHandle!.RemoteFileSigLevel); if (!success) break; }
						}
						if (success)
						{
							success = TransPrepareReal(alpmHandle);
							if (success && (alpmHandle.TransToAdd()?.Count ?? 0) == 0 && (alpmHandle.TransToRemove()?.Count ?? 0) == 0)
							{
								TransRelease(alpmHandle);
								TransReset();
								return true;
							}
						}
						if (!success) TransRelease(alpmHandle);
					}
					else if (needRetry)
					{
						if (_commitRetries < 1) { _commitRetries++; success = TransRunReal(alpmHandle); }
					}
				}
				if (!success) { TransReset(); return false; }
			}
			if (!rebootNeeded) rebootNeeded = NeedReboot(alpmHandle);
			success = TransCommitReal(alpmHandle, ref needRetry);
			if (success)
			{
				foreach (var path in _localPaths)
				{
					if (path.StartsWith("/var/tmp/pamac-build") || path.StartsWith("/tmp/pamac-build")
						|| path.StartsWith(_config.AurBuildDir))
					{
						if (_keepBuiltPkgs)
						{
							try
							{
								var cachedir = alpmHandle!.CacheDirs.FirstOrDefault() ?? "/var/cache/pacman/pkg/";
								Spawn.SpawnCommandLineSync($"mv -f {path} {cachedir}");
							}
							catch (Exception e) { Warning(e.Message); }
						}
						else
						{
							try { Spawn.SpawnCommandLineSync("rm -f " + path); }
							catch (Exception e) { Warning(e.Message); }
						}
					}
				}
				foreach (var (pkgname, _) in _toInstallAsDep.ToList())
				{
					var pkg = alpmHandle!.LocalDb.GetPkg(pkgname);
					if (pkg != null) pkg.Reason = PackageReason.Depend;
				}
				if (rebootNeeded)
					DoEmitWarning(DGettext(null, "A restart is required for the changes to take effect") + ".");
				if (_sysupgrade)
				{
					try { Spawn.SpawnCommandLineSync("rm -f /system-update"); }
					catch (Exception e) { Warning(e.Message); }
				}
			}
			else if (needRetry)
			{
				if (_commitRetries < 1) { _commitRetries++; success = TransRunReal(alpmHandle); }
			}
			TransReset();
			return success;
		}

		string? BackupConflictFile(string filePath)
		{
			var backupFilePath = filePath + ".old";
			var backupFile = GFile.NewForPath(backupFilePath);
			if (backupFile.QueryExists())
			{
				uint i = 0;
				do
				{
					i++;
					backupFile = GFile.NewForPath(backupFilePath + i);
				} while (backupFile.QueryExists());
			}
			try
			{
				Spawn.SpawnCommandLineSync($"mv -f {filePath} {backupFile.GetPath()}");
				return backupFile.GetPath();
			}
			catch (Exception e) { Warning(e.Message); }
			return null;
		}

		bool TransCommitReal(Handle? alpmHandle, ref bool needRetry)
		{
			bool success = true;
			List<object>? errData;
			if (alpmHandle!.TransCommit(out errData) == -1)
			{
				var errNo = alpmHandle.Errno();
				needRetry = false;
				var details = new List<string>();
				switch (errNo)
				{
					case Errno.FileConflicts:
						details.Add(Alpm.Strerror(errNo) + ":");
						foreach (var item in errData ?? new List<object>())
						{
							if (item is not FileConflict conflict) continue;
							if (conflict.Type == FileConflict.ConflictType.Target)
							{
								details.Add("- " + string.Format(
									DGettext(null, "{0} exists in both {1} and {2}"),
									conflict.File, conflict.Target, conflict.CTarget));
							}
							else
							{
								if (conflict.CTarget.Length > 0)
								{
									details.Add("- " + string.Format(
										DGettext(null, "{0}: {1} already exists in filesystem (owned by {2})"),
										conflict.Target, conflict.File, conflict.CTarget));
								}
								else if (_commitRetries < 1)
								{
									string? backupPath = BackupConflictFile(conflict.File);
									if (backupPath == null)
									{
										details.Add("- " + string.Format(
											DGettext(null, "{0}: {1} already exists in filesystem"),
											conflict.Target, conflict.File) + ",");
										details.Add("  " + DGettext(null, "if this file is not needed, remove it and retry"));
									}
									else
									{
										DoEmitWarning(DGettext(null, "Warning") + ": " + string.Format(
											DGettext(null, "{0}: {1} already exists in filesystem"),
											conflict.Target, conflict.File));
										DoEmitWarning(string.Format(DGettext(null, "It has been backed up to {0}"), backupPath));
										needRetry = true;
									}
								}
								else
								{
									details.Add("- " + string.Format(
										DGettext(null, "{0}: {1} already exists in filesystem"),
										conflict.Target, conflict.File) + ",");
									details.Add("  " + DGettext(null, "if this file is not needed, remove it and retry"));
								}
							}
						}
						break;
					case Errno.PkgInvalidCheckSum:
						if (_commitRetries < 1)
						{
							DoEmitScriptOutput(DGettext(null, "Removing invalid files and retrying") + "...");
							needRetry = true;
						}
						else details.Add(Alpm.Strerror(errNo) + ":");
						foreach (var item in errData ?? new List<object>())
						{
							if (item is not string filename) continue;
							if (!needRetry)
							{
								details.Add("- " + string.Format(DGettext(null, "{0} is invalid or corrupted"), filename) + ",");
								details.Add("- " + DGettext(null, "you can remove this file and retry"));
							}
						}
						break;
					case Errno.PkgInvalid:
					case Errno.PkgInvalidSig:
						if (_commitRetries < 1)
						{
							DoEmitScriptOutput(DGettext(null, "Removing invalid files and retrying") + "...");
							needRetry = true;
						}
						else details.Add(Alpm.Strerror(errNo) + ":");
						foreach (var item in errData ?? new List<object>())
						{
							if (item is not string filename) continue;
							if (!needRetry)
							{
								details.Add("- " + string.Format(DGettext(null, "{0} is invalid or corrupted"), filename) + ",");
								details.Add("  " + DGettext(null, "you can remove this file and retry"));
							}
							else
							{
								try { Spawn.SpawnCommandLineSync("rm -f " + filename); }
								catch (Exception e) { Warning(e.Message); }
							}
						}
						break;
					default:
						details.Add(Alpm.Strerror(errNo));
						break;
				}
				success = false;
				if (!needRetry) DoEmitError(DGettext(null, "Failed to commit transaction"), details);
			}
			TransRelease(alpmHandle);
			return success;
		}

		void TransRelease(Handle? alpmHandle)
		{
			alpmHandle!.TransRelease();
			RemoveIgnorePkgs(alpmHandle);
			RemoveOverwriteFiles(alpmHandle);
		}

		public void TransCancel(string sender)
		{
			if (sender != _sender) return;
			Cancellable.Cancel();
			TransReset();
		}

		string RemoveBashColors(string msg) =>
			Regex.Replace(msg, "\u001B\\[[0-9;]*[JKmsu]", "");

		public void EmitEvent(uint primaryEvent, uint secondaryEvent, List<string> details)
		{
			switch (primaryEvent)
			{
				case 1: DoEmitAction(DGettext(null, "Checking dependencies") + "..."); break;
				case 3: CurrentAction = DGettext(null, "Checking file conflicts") + "..."; break;
				case 5: DoEmitAction(DGettext(null, "Resolving dependencies") + "..."); break;
				case 7: DoEmitAction(DGettext(null, "Checking inter-conflicts") + "..."); break;
				case 11:
					switch (secondaryEvent)
					{
						case 1:
							CurrentFilename = details[0];
							CurrentAction = string.Format(DGettext(null, "Installing {0}"),
								string.Format("{0} ({1})", details[0], details[1])) + "...";
							break;
						case 2:
							CurrentFilename = details[0];
							CurrentAction = string.Format(DGettext(null, "Upgrading {0}"),
								string.Format("{0} ({1} -> {2})", details[0], details[1], details[2])) + "...";
							break;
						case 3:
							CurrentFilename = details[0];
							CurrentAction = string.Format(DGettext(null, "Reinstalling {0}"),
								string.Format("{0} ({1})", details[0], details[1])) + "...";
							break;
						case 4:
							CurrentFilename = details[0];
							CurrentAction = string.Format(DGettext(null, "Downgrading {0}"),
								string.Format("{0} ({1} -> {2})", details[0], details[1], details[2])) + "...";
							break;
						case 5:
							CurrentFilename = details[0];
							CurrentAction = string.Format(DGettext(null, "Removing {0}"),
								string.Format("{0} ({1})", details[0], details[1])) + "...";
							break;
					}
					break;
				case 13: CurrentAction = DGettext(null, "Checking integrity") + "..."; break;
				case 15: CurrentAction = DGettext(null, "Loading packages files") + "..."; break;
				case 17:
					string msg = RemoveBashColors(details[0]).Replace("\n", "");
					DoEmitScriptOutput(msg);
					if (CurrentFilename != "")
					{
						string action = string.Format(DGettext(null, "Configuring {0}"), CurrentFilename) + "...";
						if (action != CurrentAction) CurrentAction = action;
						if (msg.ToLowerInvariant().Contains("error"))
						{
							DoEmitWarning(string.Format(DGettext(null, "Error while configuring {0}"), CurrentFilename));
							DoImportantDetailsOutpout(true);
						}
						else DoImportantDetailsOutpout(false);
					}
					break;
				case 18: DoStartDownloading(); break;
				case 19:
				case 20: DoStopDownloading(); break;
				case 21: DoStartDownloading(); break;
				case 22:
				case 23:
					DoStopDownloading();
					CurrentFilename = "";
					MultiProgress.Clear();
					_downloadRates.Clear();
					_downloadRate = 0;
					CurrentProgress = 0;
					AlreadyDownloaded = 0;
					_currentStatus = "";
					TotalDownload = 0;
					if (primaryEvent == 23) DoEmitWarning(DGettext(null, "failed to retrieve some files"));
					break;
				case 24: CurrentAction = DGettext(null, "Checking available disk space") + "..."; break;
				case 26:
					DoEmitWarning(string.Format("{0}: {1}",
						DGettext(null, "Warning"),
						string.Format(DGettext(null, "{0} optionally requires {1}"), details[0], details[1])));
					break;
				case 28: CurrentAction = DGettext(null, "Checking keyring") + "..."; break;
				case 30: DoEmitAction(DGettext(null, "Downloading required keys") + "..."); break;
				case 32:
					DoEmitScriptOutput(string.Format(DGettext(null, "{0} installed as {1}.pacnew"), details[0], details[0]) + ".");
					break;
				case 33:
					DoEmitScriptOutput(string.Format(DGettext(null, "{0} installed as {1}.pacsave"), details[0], details[0]) + ".");
					break;
				case 34:
					switch (secondaryEvent)
					{
						case 1: CurrentAction = DGettext(null, "Running pre-transaction hooks") + "..."; break;
						case 2:
							CurrentFilename = "";
							CurrentAction = DGettext(null, "Running post-transaction hooks") + "...";
							break;
					}
					break;
				case 36:
					double progress = int.Parse(details[2]) / (double)int.Parse(details[3]);
					string status = $"{details[2]}/{details[3]}";
					bool changed = false;
					if (progress != CurrentProgress) { CurrentProgress = progress; changed = true; }
					if (status != _currentStatus) { _currentStatus = status; changed = true; }
					if (changed)
					{
						string hook = details[1] != "" ? details[1] : details[0];
						DoEmitHookProgress(CurrentAction, hook, _currentStatus, CurrentProgress);
						if (hook.ToLowerInvariant().Contains("error"))
						{
							DoEmitWarning(DGettext(null, "Error while running hooks"));
							DoImportantDetailsOutpout(true);
						}
					}
					break;
			}
		}

		public void EmitProgress(uint progress, string pkgname, uint percent, uint nTargets, uint currentTarget)
		{
			double fraction;
			switch (progress)
			{
				case 0:
				case 1:
				case 2:
				case 3:
				case 4:
					fraction = ((double)(currentTarget - 1) / nTargets) + ((double)percent / (100 * nTargets));
					break;
				default:
					fraction = (double)percent / 100;
					break;
			}
			string status = $"{currentTarget}/{nTargets}";
			bool changed = false;
			if (fraction != CurrentProgress) { CurrentProgress = fraction; changed = true; }
			if (status != _currentStatus) { _currentStatus = status; changed = true; }
			if (changed && CurrentAction != "") DoEmitActionProgress(CurrentAction, _currentStatus, CurrentProgress);
		}

		public void EmitDownload(ulong xfered, ulong total)
		{
			if (xfered == 0)
			{
				_rateTimer.Restart();
				if (TotalDownload == 0) { _downloadRates.Clear(); _downloadRate = 0; }
				return;
			}
			var text = new StringBuilder(FormatSize(xfered));
			if (CurrentProgress < 1)
			{
				double fraction = (double)xfered / total;
				if (fraction <= 1)
				{
					text.Append("/").Append(FormatSize(total));
					double elapsed = _rateTimer.Elapsed.TotalSeconds;
					if (elapsed > 1)
					{
						double currentRate = (xfered - AlreadyDownloaded) / elapsed;
						AlreadyDownloaded = xfered;
						if (_downloadRates.Count > 10) _downloadRates.Dequeue();
						_downloadRates.Enqueue(currentRate);
						if (xfered == total) _rateTimer.Stop();
						else _rateTimer.Restart();
						if (_downloadRates.Count == 10) _downloadRate = _downloadRates.Average();
					}
					if (_downloadRate > 0)
					{
						uint remainingSeconds = (uint)Math.Round((total - xfered) / _downloadRate);
						text.Append(' ');
						if (remainingSeconds > 0)
						{
							if (remainingSeconds < 60)
							{
								text.Append(string.Format(
									DGettext(null, "About {0} second remaining"),
									remainingSeconds));
							}
							else
							{
								uint remainingMinutes = (uint)Math.Round(remainingSeconds / 60.0);
								text.Append(string.Format(
									DGettext(null, "About {0} minute remaining"),
									remainingMinutes));
							}
						}
					}
				}
				else
				{
					fraction = 1;
					_rateTimer.Stop();
				}
				if (fraction != CurrentProgress) CurrentProgress = fraction;
			}
			if (text.ToString() != _currentStatus) _currentStatus = text.ToString();
			DoEmitDownloadProgress(CurrentAction, _currentStatus, CurrentProgress);
		}

		public void EmitTotaldownload(ulong total)
		{
			_downloadRates.Clear();
			_downloadRate = 0;
			CurrentProgress = 0;
			AlreadyDownloaded = 0;
			_currentStatus = "";
			TotalDownload = total;
		}

		public void EmitLog(uint level, string msg)
		{
			string? line = null;
			if (level == 1)
			{
				line = CurrentFilename != ""
					? DGettext(null, "Error") + ": " + CurrentFilename + ": " + msg
					: DGettext(null, "Error") + ": " + msg;
				DoImportantDetailsOutpout(false);
				DoEmitScriptOutput(line.Replace("\n", ""));
			}
			else if (level == (1 << 1))
			{
				if (_noConfirmCommit) return;
				if (CurrentFilename != "manjaro-system")
				{
					line = CurrentFilename != ""
						? DGettext(null, "Warning") + ": " + CurrentFilename + ": " + msg
						: DGettext(null, "Warning") + ": " + msg;
					DoEmitScriptOutput(line.Replace("\n", ""));
				}
			}
		}

		static string FormatSize(ulong bytes)
		{
			string[] units = { "B", "KiB", "MiB", "GiB" };
			double size = bytes;
			int i = 0;
			while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
			return $"{size:0.#} {units[i]}";
		}

		void WriteLogFile(string message)
		{
			try
			{
				using var w = new StreamWriter("/var/log/pamac.log", append: true);
				w.WriteLine(message);
			}
			catch
			{
				// ignore
			}
		}
	}
}
