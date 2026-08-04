// libpamac — C# port
//
// Transaction: orchestrates alpm / snap / flatpak transactions, AUR builds,
// and DBus interactions with the system daemon. Faithful port of src/transaction.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2018-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibAlpm;
using Pamac.Compat;
using static Pamac.Compat.Gettext;
using static Pamac.Compat.Log;

namespace Pamac
{
	public class Transaction
	{
		ITransactionInterface _transactionInterface = null!;
		bool _waiting;
		readonly Config _config;
		readonly MainContext _context;

		// run transaction data
		readonly AlpmUtils _alpmUtils;
		bool _sysupgrading;
		bool _forceRefresh;
		int _transFlags;
		readonly HashSet<string> _toInstall = new HashSet<string>();
		readonly HashSet<string> _toRemove = new HashSet<string>();
		readonly HashSet<string> _toLoadLocal = new HashSet<string>();
		readonly HashSet<string> _toLoadRemote = new HashSet<string>();
		readonly HashSet<string> _toBuild = new HashSet<string>();
		readonly HashSet<string> _cloneFiles = new HashSet<string>();
		readonly HashSet<string> _cloneDepsFiles = new HashSet<string>();
		readonly HashSet<string> _ignorePkgs = new HashSet<string>();
		readonly HashSet<string> _overwriteFiles = new HashSet<string>();
		readonly HashSet<string> _toInstallAsDep = new HashSet<string>();
		readonly Dictionary<string, SnapPackage> _snapToInstall = new Dictionary<string, SnapPackage>();
		readonly Dictionary<string, SnapPackage> _snapToRemove = new Dictionary<string, SnapPackage>();
		readonly Dictionary<string, FlatpakPackage> _flatpakToInstall = new Dictionary<string, FlatpakPackage>();
		readonly Dictionary<string, FlatpakPackage> _flatpakToRemove = new Dictionary<string, FlatpakPackage>();
		readonly Dictionary<string, FlatpakPackage> _flatpakToUpgrade = new Dictionary<string, FlatpakPackage>();
		// building data
		readonly string _tmpPath;
		readonly string _aurdbPath;
		readonly HashSet<string> _alreadyCheckedAurDep = new HashSet<string>();
		readonly HashSet<string> _aurDescList = new HashSet<string>();
		readonly Queue<string> _toBuildQueue = new Queue<string>();
		readonly HashSet<string> _aurPkgsToInstall = new HashSet<string>();
		bool _building;
		readonly Cancellable _buildCancellable = new Cancellable();

		public Database Database { get; set; }
		public bool DownloadOnly { get; set; }
		public bool DryRun { get; set; }
		public bool InstallIfNeeded { get; set; }
		public bool RemoveIfUnneeded { get; set; }
		public bool Cascade { get; set; }
		public bool KeepConfigFiles { get; set; }
		public bool InstallAsDep { get; set; }
		public bool InstallAsExplicit { get; set; }
		public bool NoRefresh { get; set; }

		public event Action<string>? EmitAction;
		public event Action<string, string, double>? EmitActionProgress;
		public event Action<string, string, double>? EmitDownloadProgress;
		public event Action<string, string, string, double>? EmitHookProgress;
		public event Action<string>? EmitScriptOutput;
		public event Action<string>? EmitWarning;
		public event Action<string, List<string>>? EmitError;
		public event Action? StartWaiting;
		public event Action? StopWaiting;
		public event Action? StartPreparing;
		public event Action? StopPreparing;
		public event Action? StartDownloading;
		public event Action? StopDownloading;
		public event Action? StartBuilding;
		public event Action? StopBuilding;
		public event Action<bool>? ImportantDetailsOutpout;

		public Transaction(Database database)
		{
			Database = database;
			_config = database.Config;
			_context = database.Context;
			_alpmUtils = new AlpmUtils(_config);
			if (Posix.GetEuid() == 0)
			{
				_transactionInterface = new TransactionInterfaceRoot(_alpmUtils, _context);
			}
			else
			{
				_transactionInterface = new TransactionInterfaceDaemon(_config);
			}
			DownloadOnly = false;
			DryRun = false;
			InstallIfNeeded = true;
			RemoveIfUnneeded = false;
			Cascade = false;
			KeepConfigFiles = true;
			InstallAsDep = false;
			InstallAsExplicit = false;
			NoRefresh = false;
			_sysupgrading = false;
			_forceRefresh = false;
			_tmpPath = string.Format("/tmp/pamac-{0}", Environment.UserName);
			_aurdbPath = _tmpPath + "/aur";
			_buildCancellable = new Cancellable();
			_building = false;
			ConnectSignals();
		}

		~Transaction()
		{
			QuitDaemon();
		}

		void ConnectSignals()
		{
			_alpmUtils.ChooseProvider += (depend, providers) =>
			{
				var task = ChooseProvider(depend, providers);
				return task.GetAwaiter().GetResult();
			};
			_alpmUtils.EmitAction += (_, action) => _context.Invoke(() => EmitAction?.Invoke(action));
			_alpmUtils.EmitActionProgress += (_, a, s, p) => _context.Invoke(() => EmitActionProgress?.Invoke(a, s, p));
			_alpmUtils.EmitHookProgress += (_, a, d, s, p) => _context.Invoke(() => EmitHookProgress?.Invoke(a, d, s, p));
			_alpmUtils.EmitDownloadProgress += (_, a, s, p) => _context.Invoke(() => EmitDownloadProgress?.Invoke(a, s, p));
			_alpmUtils.StartDownloading += _ => _context.Invoke(() => StartDownloading?.Invoke());
			_alpmUtils.StopDownloading += _ => _context.Invoke(() => StopDownloading?.Invoke());
			_alpmUtils.EmitScriptOutput += (_, m) => _context.Invoke(() => EmitScriptOutput?.Invoke(m));
			_alpmUtils.EmitWarning += (_, m) => _context.Invoke(() => EmitWarning?.Invoke(m));
			_alpmUtils.EmitError += (_, m, d) => _context.Invoke(() => EmitError?.Invoke(m, d));
			_alpmUtils.ImportantDetailsOutpout += (_, b) => _context.Invoke(() => ImportantDetailsOutpout?.Invoke(b));
		}

		public void QuitDaemon()
		{
			try { _transactionInterface.QuitDaemon(); }
			catch (Exception e)
			{
				var details = new List<string> { $"quit_daemon: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
		}

		// ---- virtual hooks (UI overrides) ----
		protected virtual Task<bool> AskCommit(TransactionSummary summary) => Task.FromResult(true);
		protected virtual Task<bool> AskEditBuildFiles(TransactionSummary summary) => Task.FromResult(false);
		protected virtual Task EditBuildFiles(List<string> pkgnames) => Task.CompletedTask;
		protected virtual Task<bool> AskImportKey(string pkgname, string key, string? owner) => Task.FromResult(false);
		protected virtual Task<List<string>> ChooseOptdeps(string pkgname, List<string> optdeps) => Task.FromResult(new List<string>());
		protected virtual Task<int> ChooseProvider(string depend, List<string> providers) => Task.FromResult(0);
		protected virtual Task<bool> AskSnapInstallClassic(string name) => Task.FromResult(false);

		async Task<List<string>> GetBuildFilesAsync(string pkgname)
		{
			if (!_config.SupportAur) return new List<string>();
			string realAurBuildDir = Database.GetRealAurBuildDir();
			string pkgdirName = PathCompat.BuildFilename(realAurBuildDir, pkgname);
			var files = new List<string>();
			files.Add(PathCompat.BuildFilename(pkgdirName, "PKGBUILD"));
			var srcinfo = GFile.NewForPath(PathCompat.BuildFilename(pkgdirName, ".SRCINFO"));
			try
			{
				using var dis = new DataInputStream(srcinfo.Read());
				string? line;
				while ((line = dis.ReadLine()) != null)
				{
					if (line.Contains("source = "))
					{
						string source = line.Split(" = ", 2)[1];
						if (!source.Contains("://"))
						{
							string sourcePath = PathCompat.BuildFilename(pkgdirName, source);
							if (GFile.NewForPath(sourcePath).QueryExists()) files.Add(sourcePath);
						}
					}
					else if (line.Contains("install = "))
					{
						string install = line.Split(" = ", 2)[1];
						string installPath = PathCompat.BuildFilename(pkgdirName, install);
						if (GFile.NewForPath(installPath).QueryExists()) files.Add(installPath);
					}
				}
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return files;
		}

		public async Task<bool> GetAuthorizationAsync()
		{
			try { return await _transactionInterface.GetAuthorization(); }
			catch (Exception e)
			{
				var details = new List<string> { $"get_authorization: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
			return false;
		}

		public void RemoveAuthorization()
		{
			try { _transactionInterface.RemoveAuthorization(); }
			catch (Exception e)
			{
				var details = new List<string> { $"remove_authorization: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
		}

		public async Task GenerateMirrorsListAsync(string country)
		{
			EmitAction?.Invoke(DGettext(null, "Refreshing mirrors list") + "...");
			ImportantDetailsOutpout?.Invoke(false);
			_transactionInterface.GenerateMirrorsListData += OnGenerateMirrorsListData;
			try { await _transactionInterface.GenerateMirrorsList(country); }
			catch (Exception e)
			{
				var details = new List<string> { $"generate_mirrors_list: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
			_transactionInterface.GenerateMirrorsListData -= OnGenerateMirrorsListData;
			Database.Refresh();
		}

		void OnGenerateMirrorsListData(string line) => EmitScriptOutput?.Invoke(line);

		public async Task CleanCacheAsync()
		{
			var details = await Database.GetCleanCacheDetailsAsync();
			var array = new List<string>(details.Keys);
			try { await _transactionInterface.CleanCache(array); }
			catch (Exception e)
			{
				var errorDetails = new List<string> { $"clean_cache: {e.Message}" };
				EmitError?.Invoke("Daemon Error", errorDetails);
			}
		}

		public async Task CleanBuildFilesAsync()
		{
			if (!_config.SupportAur) return;
			string realAurBuildDir = Database.GetRealAurBuildDir();
			try { await _transactionInterface.CleanBuildFiles(realAurBuildDir); }
			catch (Exception e)
			{
				var details = new List<string> { $"clean_build_files: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
		}

		public async Task<bool> SetPkgReasonAsync(string pkgname, uint reason)
		{
			bool success = false;
			try { success = await _transactionInterface.SetPkgReason(pkgname, reason); }
			catch (Exception e)
			{
				var details = new List<string> { $"set_pkgreason: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
			Database.Refresh();
			return success;
		}

		public async Task<bool> DownloadUpdatesAsync()
		{
			bool success = false;
			try { success = await _transactionInterface.DownloadUpdates(); }
			catch (Exception e)
			{
				var details = new List<string> { $"download_updates: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
			}
			return success;
		}

		Task<int> LaunchSubprocess(string[] cmds) =>
			Task.Run(() =>
			{
				var process = new Subprocess(cmds);
				process.Wait();
				return process.HasExited ? process.ExitStatus : 1;
			});

		async Task<bool> ComputeAurBuildList()
		{
			_building = true;
			_buildCancellable.Reset();
			StartBuilding?.Invoke();
			bool success = await ComputeAurBuildListReal();
			StopBuilding?.Invoke();
			_building = false;
			if (_buildCancellable.IsCancelled)
			{
				EmitScriptOutput?.Invoke("");
				EmitAction?.Invoke(DGettext(null, "Transaction cancelled") + ".");
			}
			return success;
		}

		async Task<bool> ComputeAurBuildListReal()
		{
			await LaunchSubprocess(new[] { "mkdir", "-p", _aurdbPath });
			_aurDescList.Clear();
			_alreadyCheckedAurDep.Clear();
			var toBuildArray = new List<string>(_toBuild);
			bool success = await CheckAurDepList(toBuildArray);
			if (success && _aurDescList.Count > 0)
			{
				string tmpAurdbPath = _tmpPath + "/pamac_aur.db";
				await LaunchSubprocess(new[] { "rm", "-f", tmpAurdbPath });
				var cmdline = new List<string>(5 + _aurDescList.Count);
				cmdline.Add("bsdtar");
				cmdline.Add("-cf");
				cmdline.Add(tmpAurdbPath);
				cmdline.Add("-C");
				cmdline.Add(_aurdbPath);
				cmdline.AddRange(_aurDescList);
				int ret = await LaunchSubprocess(cmdline.ToArray());
				if (ret == 0) await LaunchSubprocess(new[] { "rm", "-rf", _aurdbPath });
				else success = false;
			}
			return success;
		}

		async Task<bool> CheckAurDepList(List<string> pkgnames)
		{
			var depToCheck = new List<string>();
			var toGetFromAur = new List<string>();
			foreach (var pkgname in pkgnames)
			{
				if (_alreadyCheckedAurDep.Contains(pkgname)) continue;
				if (_cloneFiles.Contains(pkgname)) toGetFromAur.Add(pkgname);
			}
			Dictionary<string, AURPackage?> aurPkgs = new Dictionary<string, AURPackage?>();
			if (toGetFromAur.Count > 0) aurPkgs = await Database.GetAurPkgsAsync(toGetFromAur);
			string realAurBuildDir = Database.GetRealAurBuildDir();
			foreach (var pkgname in pkgnames)
			{
				if (_buildCancellable.IsCancelled) return false;
				if (_alreadyCheckedAurDep.Contains(pkgname)) continue;
				AURPackage? aurPkg = null;
				GFile? cloneDir = GFile.NewForPath(PathCompat.BuildFilename(realAurBuildDir, pkgname));
				if (_cloneFiles.Contains(pkgname))
				{
					aurPkg = aurPkgs.TryGetValue(pkgname, out var p) ? p : null;
					if (aurPkg == null)
					{
						var providers = Database.GetAurProviders(pkgname);
						foreach (var info in providers)
						{
							string providerName = info.Name;
							depToCheck.Add(providerName);
							_cloneFiles.Add(providerName);
							if (_cloneDepsFiles.Contains(pkgname)) _cloneDepsFiles.Add(providerName);
						}
						_alreadyCheckedAurDep.Add(pkgname);
						continue;
					}
					else if (cloneDir.QueryExists())
					{
						EmitAction?.Invoke(DGettext(null, "Cloning {0} build files").Replace("{0}", aurPkg.PackageBase ?? pkgname) + "...");
						cloneDir = await Database.CloneBuildFilesAsync(aurPkg.PackageBase!, false, _buildCancellable);
						if (_buildCancellable.IsCancelled) return false;
						if (cloneDir == null)
						{
							var details = new List<string> {
								string.Format(DGettext(null, "Failed to clone {0} build files"), aurPkg.PackageBase)
							};
							EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
							return false;
						}
						EmitAction?.Invoke(string.Format(DGettext(null, "Generating {0} information"), pkgname) + "...");
						if (!await Database.RegenerateSrcinfoAsync(pkgname, _buildCancellable))
						{
							var details = new List<string> {
								string.Format(DGettext(null, "Failed to generate {0} information"), pkgname)
							};
							EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
							return false;
						}
					}
					else _cloneFiles.Add(aurPkg.PackageBase!);
				}
				else if (cloneDir.QueryExists())
				{
					EmitAction?.Invoke(string.Format(DGettext(null, "Generating {0} information"), pkgname) + "...");
					if (!await Database.RegenerateSrcinfoAsync(pkgname, _buildCancellable))
					{
						var details = new List<string> {
							string.Format(DGettext(null, "Failed to generate {0} information"), pkgname)
						};
						EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
						return false;
					}
				}
				else
				{
					bool found = await FindPkgBuildDir(pkgname, realAurBuildDir);
					if (!found)
					{
						var details = new List<string> { string.Format(DGettext(null, "target not found: {0}"), pkgname) };
						EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
						return false;
					}
				}
				if (_buildCancellable.IsCancelled) return false;
				EmitAction?.Invoke(string.Format(DGettext(null, "Checking {0} dependencies"), pkgname) + "...");
				if (cloneDir.QueryExists())
				{
					// use .SRCINFO
					await CheckSrcinfoDeps(pkgname, cloneDir, depToCheck);
				}
				else
				{
					_alreadyCheckedAurDep.Add(aurPkg!.Name);
					await WriteAurDescFile(aurPkg, pkgname, depToCheck);
				}
			}
			if (depToCheck.Count > 0) return await CheckAurDepList(depToCheck);
			return true;
		}

		async Task<bool> FindPkgBuildDir(string pkgname, string realAurBuildDir)
		{
			var builddir = GFile.NewForPath(realAurBuildDir);
			try
			{
				foreach (var (filename, _, type, _) in builddir.EnumerateChildren())
				{
					if (type != FileType.Directory) continue;
					var childFile = GFile.NewForPath(PathCompat.BuildFilename(realAurBuildDir, filename));
					bool hasPkgbuild = childFile.EnumerateChildren().Any(e => e.Name == "PKGBUILD");
					if (!hasPkgbuild) continue;
					bool success = Database.RegenerateSrcinfo(filename, null);
					if (!success) continue;
					var srcinfo = childFile.GetChild(".SRCINFO");
					using var dis = new DataInputStream(srcinfo.Read());
					string? line;
					while ((line = dis.ReadLine()) != null)
					{
						if (!line.Contains("pkgname = ")) continue;
						string srcinfoPkgname = line.Split(" = ", 2)[1];
						if (srcinfoPkgname == pkgname) return true;
					}
				}
			}
			catch (Exception e)
			{
				var details = new List<string> { e.Message, string.Format(DGettext(null, "target not found: {0}"), pkgname) };
				EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
			}
			return false;
		}

		async Task CheckSrcinfoDeps(string pkgname, GFile cloneDir, List<string> depToCheck)
		{
			var srcinfo = cloneDir.GetChild(".SRCINFO");
			try
			{
				using var dis = new DataInputStream(srcinfo.Read());
				string? line;
				while ((line = dis.ReadLine()) != null)
				{
					if (line.Contains("depends = ") || line.Contains("makedepends = ") || line.Contains("checkdepends = "))
					{
						await CheckDep(line, pkgname, depToCheck);
					}
				}
			}
			catch (Exception e)
			{
				var details = new List<string> { string.Format(DGettext(null, "Failed to check {0} dependencies"), pkgname) };
				EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
			}
		}

		async Task CheckDep(string line, string pkgname, List<string> depToCheck)
		{
			string depend = line.Split(" = ", 2)[1];
			if (!Database.HasInstalledSatisfier(depend) && !Database.HasSyncSatisfier(depend))
			{
				string depName = Database.GetAlpmDepName(depend);
				if (!_alreadyCheckedAurDep.Contains(depName))
				{
					depToCheck.Add(depName);
					if (_cloneDepsFiles.Contains(pkgname))
					{
						_cloneFiles.Add(depName);
						_cloneDepsFiles.Add(depName);
					}
				}
			}
		}

		async Task WriteAurDescFile(AURPackage aurPkg, string pkgname, List<string> depToCheck)
		{
			string pkgdir = $"{aurPkg.Name}-{aurPkg.Version}";
			string pkgdirPath = $"{_aurdbPath}/{pkgdir}";
			_aurDescList.Add(pkgdir);
			var file = GFile.NewForPath(pkgdirPath);
			if (!file.QueryExists()) Directory.CreateDirectory(pkgdirPath);
			var sb = new StringBuilder();
			sb.Append("%FILENAME%\n").Append($"{aurPkg.Name}-{aurPkg.Version}-any.pkg.tar\n\n");
			sb.Append("%NAME%\n").Append(aurPkg.Name).Append("\n\n");
			sb.Append("%VERSION%\n").Append(aurPkg.Version).Append("\n\n");
			sb.Append("%BASE%\n").Append(aurPkg.PackageBase).Append("\n\n");
			sb.Append("%DESC%\n").Append(aurPkg.Desc).Append("\n\n");
			var allDeps = new List<string>(aurPkg.Depends);
			allDeps.AddRange(aurPkg.CheckDepends);
			allDeps.AddRange(aurPkg.MakeDepends);
			bool dependsCreated = false;
			foreach (var name in allDeps)
			{
				if (!dependsCreated) { sb.Append("%DEPENDS%\n"); dependsCreated = true; }
				sb.Append(name).Append('\n');
				if (!Database.HasInstalledSatisfier(name) && !Database.HasSyncSatisfier(name))
				{
					string depName = Database.GetAlpmDepName(name);
					if (!_alreadyCheckedAurDep.Contains(depName))
					{
						depToCheck.Add(depName);
						if (_cloneDepsFiles.Contains(pkgname))
						{
							_cloneFiles.Add(depName);
							_cloneDepsFiles.Add(depName);
						}
					}
				}
			}
			if (dependsCreated) sb.Append('\n');
			if (aurPkg.Conflicts.Count != 0)
			{
				sb.Append("%CONFLICTS%\n");
				foreach (var n in aurPkg.Conflicts) sb.Append(n).Append('\n');
				sb.Append('\n');
			}
			if (aurPkg.Provides.Count != 0)
			{
				sb.Append("%PROVIDES%\n");
				foreach (var n in aurPkg.Provides) sb.Append(n).Append('\n');
				sb.Append('\n');
			}
			if (aurPkg.Replaces.Count != 0)
			{
				sb.Append("%REPLACES%\n");
				foreach (var n in aurPkg.Replaces) sb.Append(n).Append('\n');
				sb.Append('\n');
			}
			try
			{
				using var dos = new DataOutputStream(GFile.NewForPath(pkgdirPath + "/desc").Create(FileCreateFlags.ReplaceDestination));
				dos.PutString(sb.ToString());
			}
			catch (Exception e)
			{
				var details = new List<string> { string.Format(DGettext(null, "Failed to check {0} dependencies"), pkgname) };
				EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
			}
		}

		async Task<bool> CloneBuildFilesIfNeeded(string pkgdir, string pkgname)
		{
			GFile? cloneDir = GFile.NewForPath(pkgdir);
			if (!cloneDir.QueryExists())
			{
				if (_cloneFiles.Contains(pkgname))
				{
					EmitAction?.Invoke(string.Format(DGettext(null, "Cloning {0} build files"), pkgname) + "...");
					cloneDir = await Database.CloneBuildFilesAsync(pkgname, false, _buildCancellable);
					if (_buildCancellable.IsCancelled) return false;
					if (cloneDir == null) return false;
					EmitAction?.Invoke(string.Format(DGettext(null, "Generating {0} information"), pkgname) + "...");
					if (!await Database.RegenerateSrcinfoAsync(pkgname, _buildCancellable)) return false;
				}
				else
				{
					var details = new List<string> { string.Format(DGettext(null, "target not found: {0}"), pkgname) };
					EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
					return false;
				}
			}
			return true;
		}

		async Task CheckSignatures(string pkgdir, string pkgname)
		{
			GFile? cloneDir = GFile.NewForPath(pkgdir);
			if (!cloneDir.QueryExists()) return;
			var srcinfo = cloneDir.GetChild(".SRCINFO");
			try
			{
				var keys = new List<string>();
				using var dis = new DataInputStream(srcinfo.Read());
				string? line;
				while ((line = dis.ReadLine()) != null)
				{
					if (line.Contains("validpgpkeys = "))
						keys.Add(line.Split(" = ", 2)[1]);
				}
				if (keys.Count > 0) await CheckSignature(pkgname, keys);
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
		}

		async Task CheckSignature(string pkgname, List<string> keys)
		{
			foreach (var key in keys)
			{
				try
				{
					var listKeys = new Subprocess(new[] { "gpg", "--with-colons", "--batch", "--list-keys", key });
					listKeys.Wait();
					if (listKeys.ExitStatus != 0)
					{
						bool success = false;
						var searchKeys = new Subprocess(new[] { "gpg", "--with-colons", "--batch", "--search-keys", key });
						searchKeys.Wait();
						if (searchKeys.ExitStatus == 0)
						{
							string? owner = null;
							using var dis = new DataInputStream(searchKeys.GetStdOutPipe());
							string? line;
							while ((line = dis.ReadLine()) != null)
							{
								if (line.StartsWith("uid:")) { owner = line.Split(':', 3)[1]; break; }
							}
							if (await AskImportKey(pkgname, key, owner))
							{
								var cmdline = new List<string> { "gpg", "--with-colons", "--batch", "--recv-keys", key };
								int status = await RunCmdLineAsync(cmdline, null, _buildCancellable);
								EmitScriptOutput?.Invoke("");
								if (status == 0) success = true;
							}
						}
						if (!success)
							EmitError?.Invoke(string.Format(DGettext(null, "key {0} could not be imported"), key), new List<string>());
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
			}
		}

		public async Task<bool> RunAsync()
		{
			bool success = true;
			if (_sysupgrading || _toInstall.Count > 0 || _toRemove.Count > 0 ||
				_toLoadLocal.Count > 0 || _toLoadRemote.Count > 0 || _toBuild.Count > 0)
			{
				success = await RunAlpmTransaction();
				if (!DryRun && success)
				{
					if (_toBuildQueue.Count != 0)
					{
						success = await BuildAurPackages();
						_buildCancellable.Reset();
					}
					if (success && (_snapToInstall.Count > 0 || _snapToRemove.Count > 0))
						success = await RunSnapTransaction();
					if (success && (_flatpakToInstall.Count > 0 || _flatpakToRemove.Count > 0 || _flatpakToUpgrade.Count > 0))
						success = await RunFlatpakTransaction();
				}
				RemoveAuthorization();
				if (!DryRun) Database.Refresh();
				if (success) EmitAction?.Invoke(DGettext(null, "Transaction successfully finished") + ".");
				else
				{
					_toBuildQueue.Clear();
					_snapToInstall.Clear();
					_snapToRemove.Clear();
					_flatpakToInstall.Clear();
					_flatpakToRemove.Clear();
					_flatpakToUpgrade.Clear();
				}
				_sysupgrading = false;
				_forceRefresh = false;
				_toInstall.Clear();
				_toRemove.Clear();
				_toLoadLocal.Clear();
				_toLoadRemote.Clear();
				_toBuild.Clear();
				_cloneFiles.Clear();
				_cloneDepsFiles.Clear();
				_ignorePkgs.Clear();
				_overwriteFiles.Clear();
				_toInstallAsDep.Clear();
			}
			else
			{
				if (_snapToInstall.Count > 0)
				{
					EmitAction?.Invoke(DGettext(null, "Preparing") + "...");
					StartPreparing?.Invoke();
					var notInstall = new List<string>();
					foreach (var (snapName, pkg) in _snapToInstall)
					{
						if (pkg.Confined != DGettext(null, "Yes"))
						{
							bool answer = await AskSnapInstallClassic(pkg.AppName ?? pkg.Name);
							if (!answer) notInstall.Add(snapName);
						}
					}
					foreach (var name in notInstall) _snapToInstall.Remove(name);
					StopPreparing?.Invoke();
				}
				var summary = new TransactionSummary();
				if (_snapToInstall.Count > 0 || _snapToRemove.Count > 0)
				{
					StartPreparing?.Invoke();
					summary.ToInstall.AddRange(_snapToInstall.Values);
					summary.ToRemove.AddRange(_snapToRemove.Values);
					StopPreparing?.Invoke();
				}
				if (_flatpakToInstall.Count > 0 || _flatpakToRemove.Count > 0 || _flatpakToUpgrade.Count > 0)
				{
					StartPreparing?.Invoke();
					summary.ToInstall.AddRange(_flatpakToInstall.Values);
					summary.ToRemove.AddRange(_flatpakToRemove.Values);
					summary.ToUpgrade.AddRange(_flatpakToUpgrade.Values);
					StopPreparing?.Invoke();
				}
				if (summary.ToInstall.Count == 0 && summary.ToRemove.Count == 0 && summary.ToUpgrade.Count == 0)
				{
					EmitAction?.Invoke(DGettext(null, "Nothing to do") + ".");
					return false;
				}
				success = await AskCommit(summary);
				if (!success)
				{
					StopPreparing?.Invoke();
					EmitAction?.Invoke(DGettext(null, "Transaction cancelled") + ".");
				}
				if (DryRun) return true;
				if (success && (_snapToInstall.Count > 0 || _snapToRemove.Count > 0))
					success = await RunSnapTransaction();
				if (success && (_flatpakToInstall.Count > 0 || _flatpakToRemove.Count > 0 || _flatpakToUpgrade.Count > 0))
					success = await RunFlatpakTransaction();
				if (success) EmitAction?.Invoke(DGettext(null, "Transaction successfully finished") + ".");
				Database.Refresh();
				if (!success)
				{
					_snapToInstall.Clear();
					_snapToRemove.Clear();
					_flatpakToInstall.Clear();
					_flatpakToRemove.Clear();
					_flatpakToUpgrade.Clear();
				}
			}
			return success;
		}

		void AddConfigIgnorePkgs()
		{
			foreach (var name in _config.IgnorePkgs) _ignorePkgs.Add(name);
		}

		async Task AddOptdeps()
		{
			var toAddToInstall = new HashSet<string>();
			foreach (var name in _toInstall)
			{
				if (!Database.IsInstalledPkg(name))
				{
					var uninstalledOptdeps = await Database.GetUninstalledOptdepsAsync(name);
					var realUninstalledOptdeps = new List<string>();
					foreach (var optdep in uninstalledOptdeps)
					{
						string optdepName = optdep.Split(": ", 2)[0];
						if (!_toInstall.Contains(optdepName) && !toAddToInstall.Contains(optdepName))
							realUninstalledOptdeps.Add(optdep);
					}
					if (realUninstalledOptdeps.Count > 0)
					{
						var optdeps = await ChooseOptdeps(name, realUninstalledOptdeps);
						foreach (var optdep in optdeps)
						{
							string optdepName = optdep.Split(": ", 2)[0];
							toAddToInstall.Add(optdepName);
						}
					}
				}
			}
			foreach (var name in toAddToInstall)
			{
				AddPkgToInstall(name);
				AddPkgToMarkAsDep(name);
			}
		}

		async Task<bool> RunAlpmTransaction()
		{
			EmitAction?.Invoke(DGettext(null, "Preparing") + "...");
			bool autoSysupgrading = false;
			if (!DryRun && !_sysupgrading && !_config.SimpleInstall && _toInstall.Count > 0)
			{
				foreach (var name in _toInstall)
				{
					if (Database.IsInstalledPkg(name))
					{
						if (Database.IsSyncPkg(name))
						{
							var pkg = Database.GetInstalledPkg(name);
							if (pkg != null && pkg.InstalledVersion != pkg.Version) { autoSysupgrading = true; break; }
						}
					}
					else { autoSysupgrading = true; break; }
				}
			}
			bool success = false;
			if (!DryRun && !NoRefresh && (_sysupgrading || autoSysupgrading))
			{
				success = await GetAuthorizationAsync();
				if (!success) return false;
				bool enableAurCopy = _config.EnableAur;
				if (autoSysupgrading) _config.EnableAur = false;
				success = await RefreshDbsAsync();
				_config.EnableAur = enableAurCopy;
				if (!success) return false;
			}
			if (_toInstall.Count > 0) await AddOptdeps();
			if (_sysupgrading)
			{
				if (_config.CheckAurUpdates)
				{
					var updates = await Database.GetAurUpdatesAsync(_ignorePkgs);
					foreach (var aurPkg in updates.AurUpdates) AddPkgToBuild(aurPkg.Name, true, true);
					foreach (var aurPkg in updates.IgnoredAurUpdates)
					{
						EmitScriptOutput?.Invoke(string.Format("{0}: {1}",
							DGettext(null, "Warning"),
							string.Format(DGettext(null, "{0}: ignoring package upgrade ({1} => {2})"),
								aurPkg.Name, aurPkg.InstalledVersion, aurPkg.Version)));
					}
				}
				if (_toBuild.Count > 0)
				{
					var toBuildCopy = new HashSet<string>(_toBuild);
					_toBuild.Clear();
					success = await TransPrepare(out _);
					if (success) success = await TransRun(new TransactionSummary());
					if (!success) return false;
					_toBuild.UnionWith(toBuildCopy);
				}
			}
			if (_toBuild.Count > 0)
			{
				success = await ComputeAurBuildList();
				if (!success) return false;
			}
			success = await TransPrepare(out var summary);
			if (success) success = await TransRun(summary);
			return success;
		}

		async Task<bool> TransCheckPrepare(out TransactionSummary summary)
		{
			bool success = false;
			summary = new TransactionSummary();
			if (_toLoadRemote.Count > 0)
			{
				success = await GetAuthorizationAsync();
				if (!success) return false;
				try
				{
					var toLoadRemoteArray = new List<string>(_toLoadRemote);
					string[] dloadPaths = await _transactionInterface.DownloadPkgs(toLoadRemoteArray);
					if (dloadPaths.Length == 0) success = false;
					else
					{
						_toLoadRemote.Clear();
						foreach (var path in dloadPaths) _toLoadRemote.Add(path);
					}
				}
				catch (Exception e)
				{
					var details = new List<string> { $"download_pkgs: {e.Message}" };
					EmitError?.Invoke("Daemon Error", details);
					success = false;
				}
				if (!success) return false;
			}
			var newSummary = new TransactionSummary();
			await Task.Run(() =>
			{
				success = _alpmUtils.TransCheckPrepare(_sysupgrading, _config.EnableDowngrade,
					_config.SimpleInstall, _transFlags, _toInstall, _toRemove, _toLoadLocal, _toLoadRemote,
					_toBuild, _ignorePkgs, _overwriteFiles, ref newSummary);
			});
			summary = newSummary;
			return success;
		}

		public async Task CheckDbs()
		{
			if (Database.DbsMissing)
			{
				await RefreshDbsAsync();
				Database.Refresh();
			}
			else if (_config.EnableAur)
			{
				string absolutePath = PathCompat.BuildFilename(_config.DbPath, "sync", "packages-meta-ext-v1.json.gz");
				if (!GFile.NewForPath(absolutePath).QueryExists())
				{
					await RefreshDbsAsync();
					Database.Refresh();
				}
			}
		}

		public async Task<bool> RefreshDbsAsync()
		{
			bool success = false;
			try
			{
				success = await _transactionInterface.TransRefresh(_forceRefresh);
				if (_config.EnableAur)
				{
					bool aurSuccess = await _transactionInterface.TransRefreshAur(_forceRefresh);
					if (!aurSuccess) EmitWarning?.Invoke(DGettext(null, "Failed to synchronize AUR database"));
				}
			}
			catch (Exception e)
			{
				var details = new List<string> { $"trans_refresh: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
				success = false;
			}
			return success;
		}

		public async Task<bool> RefreshFilesDbsAsync()
		{
			bool success = false;
			try { success = await _transactionInterface.TransRefreshFiles(_forceRefresh); }
			catch (Exception e)
			{
				var details = new List<string> { $"trans_refresh_files: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
				success = false;
			}
			return success;
		}

		async Task<bool> TransPrepare(out TransactionSummary summary)
		{
			StartPreparing?.Invoke();
			AddConfigIgnorePkgs();
			SetFlags();
			bool success = await TransCheckPrepare(out summary);
			StopPreparing?.Invoke();
			if (!success)
			{
				if (_toBuild.Count > 0)
				{
					if (await AskEditBuildFiles(new TransactionSummary()))
					{
						foreach (var name in _toBuild)
						{
							if (!_alpmUtils.Unresolvables.Contains(name)) _alpmUtils.Unresolvables.Add(name);
						}
						foreach (var pkgname in _alpmUtils.Unresolvables)
						{
							string realAurBuildDir = Database.GetRealAurBuildDir();
							string pkgdir = PathCompat.BuildFilename(realAurBuildDir, pkgname);
							success = await CloneBuildFilesIfNeeded(pkgdir, pkgname);
							if (!success)
							{
								var details = new List<string> { string.Format(DGettext(null, "Failed to clone {0} build files"), pkgname) };
								EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
								_alpmUtils.Unresolvables.Clear();
								return false;
							}
						}
						await EditBuildFiles(_alpmUtils.Unresolvables);
						_alpmUtils.Unresolvables.Clear();
						EmitScriptOutput?.Invoke("");
						success = await ComputeAurBuildList();
						if (!success) return false;
						success = await TransPrepare(out summary);
					}
					else EmitAction?.Invoke(DGettext(null, "Transaction cancelled") + ".");
				}
			}
			return success;
		}

		async Task<bool> TransRun(TransactionSummary summary)
		{
			if (summary.AurPkgBasesToBuild.Count != 0)
			{
				_toBuildQueue.Clear();
				foreach (var name in summary.AurPkgBasesToBuild) _toBuildQueue.Enqueue(name);
				_aurPkgsToInstall.Clear();
				foreach (var pkg in summary.ToBuild)
				{
					string pkgname = pkg.Name;
					_aurPkgsToInstall.Add(pkgname);
					if (!_toBuild.Contains(pkgname) && pkg is AlpmPackage alpmpkg)
					{
						foreach (var provide in alpmpkg.Provides)
						{
							string provideName = Database.GetAlpmDepName(provide);
							if (_toBuild.Contains(provideName))
							{
								_toBuild.Remove(provideName);
								_toBuild.Add(pkgname);
								break;
							}
						}
					}
				}
				if (await AskEditBuildFilesReal(summary))
				{
					string realAurBuildDir = Database.GetRealAurBuildDir();
					foreach (var pkgname in summary.AurPkgBasesToBuild)
					{
						string pkgdir = PathCompat.BuildFilename(realAurBuildDir, pkgname);
						bool success = await CloneBuildFilesIfNeeded(pkgdir, pkgname);
						if (!success)
						{
							var details = new List<string> { string.Format(DGettext(null, "Failed to clone {0} build files"), pkgname) };
							EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
							return false;
						}
					}
					await EditBuildFiles(summary.AurPkgBasesToBuild);
					EmitScriptOutput?.Invoke("");
					bool ok = await ComputeAurBuildList();
					bool s = await TransPrepare(out var newSummary);
					if (s) return await TransRun(newSummary);
					return false;
				}
			}
			if (summary.ToInstall.Count != 0 || summary.ToUpgrade.Count != 0 || summary.ToDowngrade.Count != 0 ||
				summary.ToReinstall.Count != 0 || summary.ConflictsToRemove.Count != 0 || summary.ToRemove.Count != 0)
			{
				foreach (var pkg in summary.ToInstall)
				{
					string pkgname = pkg.Name;
					if (!_toInstall.Contains(pkgname) && !summary.ToLoad.Contains(pkgname))
					{
						_toInstall.Add(pkgname);
						bool findTopProvider = false;
						if (pkg is AlpmPackage alpmpkg)
						{
							foreach (var provide in alpmpkg.Provides)
							{
								string provideName = Database.GetAlpmDepName(provide);
								if (_toInstall.Contains(provideName))
								{
									_toInstall.Remove(provideName);
									_toInstall.Add(pkgname);
									findTopProvider = true;
									break;
								}
							}
						}
						if (!findTopProvider) _toInstallAsDep.Add(pkgname);
					}
				}
				_toRemove.Clear();
				foreach (var pkg in summary.ToRemove) _toRemove.Add(pkg.Name);
				bool success = await AskCommitReal(summary);
				if (!success && summary.ToBuild.Count != 0)
					await LaunchSubprocess(new[] { "rm", "-f", $"{_tmpPath}/pamac_aur.db" });
				if (DryRun) return true;
				if (success)
				{
					if (summary.AurPkgBasesToBuild.Count != 0)
					{
						string realAurBuildDir = Database.GetRealAurBuildDir();
						foreach (var pkgname in summary.AurPkgBasesToBuild)
						{
							string pkgdir = PathCompat.BuildFilename(realAurBuildDir, pkgname);
							success = await CloneBuildFilesIfNeeded(pkgdir, pkgname);
							if (success) await CheckSignatures(pkgdir, pkgname);
							else
							{
								var details = new List<string> { string.Format(DGettext(null, "Failed to clone {0} build files"), pkgname) };
								EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
								return false;
							}
						}
					}
					success = await GetAuthorizationAsync();
					if (!success) return false;
					try
					{
						success = await _transactionInterface.TransRun(_sysupgrading, _config.EnableDowngrade,
							_config.SimpleInstall, _config.KeepBuiltPkgs, _transFlags,
							new List<string>(_toInstall), new List<string>(_toRemove), new List<string>(_toLoadLocal),
							new List<string>(_toLoadRemote), new List<string>(_toInstallAsDep),
							new List<string>(_ignorePkgs), new List<string>(_overwriteFiles));
					}
					catch (Exception e)
					{
						var details = new List<string> { $"trans_run: {e.Message}" };
						EmitError?.Invoke("Daemon Error", details);
						success = false;
					}
					return success;
				}
				EmitAction?.Invoke(DGettext(null, "Transaction cancelled") + ".");
				return false;
			}
			else if (summary.ToBuild.Count != 0)
			{
				bool success = await AskCommitReal(summary);
				if (!success) await LaunchSubprocess(new[] { "rm", "-f", $"{_tmpPath}/pamac_aur.db" });
				if (DryRun) return true;
				if (success)
				{
					string realAurBuildDir = Database.GetRealAurBuildDir();
					foreach (var pkgname in summary.AurPkgBasesToBuild)
					{
						string pkgdir = PathCompat.BuildFilename(realAurBuildDir, pkgname);
						success = await CloneBuildFilesIfNeeded(pkgdir, pkgname);
						if (success) await CheckSignatures(pkgdir, pkgname);
						else
						{
							var details = new List<string> { string.Format(DGettext(null, "Failed to clone {0} build files"), pkgname) };
							EmitError?.Invoke(DGettext(null, "Failed to prepare transaction"), details);
							return false;
						}
					}
					return await GetAuthorizationAsync();
				}
				EmitAction?.Invoke(DGettext(null, "Transaction cancelled") + ".");
				return false;
			}
			EmitAction?.Invoke(DGettext(null, "Nothing to do") + ".");
			return true;
		}

		async Task<bool> AskEditBuildFilesReal(TransactionSummary summary) => await AskEditBuildFiles(summary);
		async Task<bool> AskCommitReal(TransactionSummary summary) => await AskCommit(summary);

		void SetFlags()
		{
			_transFlags = 0;
			if (DownloadOnly) _transFlags |= 1 << 9;
			if (InstallIfNeeded) _transFlags |= 1 << 13;
			if (InstallAsDep) _transFlags |= 1 << 8;
			else if (InstallAsExplicit) _transFlags |= 1 << 14;
			if (RemoveIfUnneeded) _transFlags |= 1 << 15;
			else if (Cascade) _transFlags |= 1 << 4;
			if (Database.Config.Recurse) _transFlags |= 1 << 5;
			if (!KeepConfigFiles) _transFlags |= 1 << 2;
		}

		public void AddPkgToInstall(string name) => _toInstall.Add(name);
		public void AddPkgToRemove(string name) => _toRemove.Add(name);

		public void AddPathToLoad(string path)
		{
			if (path.Contains("://")) _toLoadRemote.Add(path);
			else _toLoadLocal.Add(path);
		}

		public void AddPkgToBuild(string name, bool cloneBuildFiles, bool cloneDepsBuildFiles)
		{
			if (!_config.SupportAur) return;
			_toBuild.Add(name);
			if (cloneBuildFiles) _cloneFiles.Add(name);
			if (cloneDepsBuildFiles) _cloneDepsFiles.Add(name);
		}

		public void AddTemporaryIgnorePkg(string name) => _ignorePkgs.Add(name);
		public void AddOverwriteFile(string glob) => _overwriteFiles.Add(glob);
		public void AddPkgToMarkAsDep(string name) => _toInstallAsDep.Add(name);

		public void AddPkgsToUpgrade(bool forceRefresh)
		{
			_forceRefresh = forceRefresh;
			_sysupgrading = true;
		}

		public void AddSnapToInstall(SnapPackage pkg)
		{
			if (_config.EnableSnap) _snapToInstall[pkg.Name] = pkg;
			else Warning("snap support disabled");
		}

		public void AddSnapToRemove(SnapPackage pkg)
		{
			if (_config.EnableSnap) _snapToRemove[pkg.Name] = pkg;
			else Warning("snap support disabled");
		}

		async Task<bool> RunSnapTransaction()
		{
			var toInstallArray = new List<string>(_snapToInstall.Keys);
			var toRemoveArray = new List<string>(_snapToRemove.Keys);
			_snapToInstall.Clear();
			_snapToRemove.Clear();
			try
			{
				StartDownloading?.Invoke();
				bool success = await _transactionInterface.SnapTransRun(toInstallArray, toRemoveArray);
				StopDownloading?.Invoke();
				return success;
			}
			catch (Exception e)
			{
				var details = new List<string> { $"snap_trans_run: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
				return false;
			}
		}

		public async Task<bool> SnapSwitchChannelAsync(string snapName, string channel)
		{
			if (_config.EnableSnap)
			{
				try { return await _transactionInterface.SnapSwitchChannel(snapName, channel); }
				catch (Exception e)
				{
					var details = new List<string> { $"snap_switch_channel: {e.Message}" };
					EmitError?.Invoke("Daemon Error", details);
				}
			}
			else Warning("snap support disabled");
			return false;
		}

		public void AddFlatpakToInstall(FlatpakPackage pkg)
		{
			if (_config.EnableFlatpak) _flatpakToInstall[pkg.Id] = pkg;
			else Warning("flatpak support disabled");
		}

		public void AddFlatpakToRemove(FlatpakPackage pkg)
		{
			if (_config.EnableFlatpak) _flatpakToRemove[pkg.Id] = pkg;
			else Warning("flatpak support disabled");
		}

		public void AddFlatpakToUpgrade(FlatpakPackage pkg)
		{
			if (_config.EnableFlatpak) _flatpakToUpgrade[pkg.Id] = pkg;
			else Warning("flatpak support disabled");
		}

		async Task<bool> RunFlatpakTransaction()
		{
			var toInstallArray = new List<string>(_flatpakToInstall.Keys);
			var toRemoveArray = new List<string>(_flatpakToRemove.Keys);
			var toUpgradeArray = new List<string>(_flatpakToUpgrade.Keys);
			_flatpakToInstall.Clear();
			_flatpakToRemove.Clear();
			_flatpakToUpgrade.Clear();
			try
			{
				StartDownloading?.Invoke();
				bool success = await _transactionInterface.FlatpakTransRun(toInstallArray, toRemoveArray, toUpgradeArray);
				StopDownloading?.Invoke();
				return success;
			}
			catch (Exception e)
			{
				var details = new List<string> { $"flatpak_trans_run: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
				return false;
			}
		}

		public virtual async Task<int> RunCmdLineAsync(List<string> args, string? workingDirectory, Cancellable cancellable)
		{
			int status = 1;
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo
				{
					FileName = args[0],
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
				};
				for (int i = 1; i < args.Count; i++) psi.ArgumentList.Add(args[i]);
				using var p = new System.Diagnostics.Process { StartInfo = psi };
				p.Start();
				while (!p.StandardOutput.EndOfStream)
				{
					if (cancellable.IsCancelled) break;
					string? line = p.StandardOutput.ReadLine();
					if (line != null) EmitScriptOutput?.Invoke(line);
				}
				if (cancellable.IsCancelled)
				{
					try { p.Kill(); } catch { }
				}
				p.WaitForExit();
				status = p.ExitCode;
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return status;
		}

		async Task<bool> BuildAurPackages()
		{
			bool success = true;
			Handle? aurDb = null;
			var tmpHandle = Database.GetTmpHandle();
			if (tmpHandle != null)
			{
				try
				{
					var cp = new Subprocess(new[] { "cp", $"{_tmpPath}/pamac_aur.db", $"{tmpHandle.DbPath}sync" });
					cp.Wait();
					aurDb = tmpHandle.RegisterSyncDb("pamac_aur", 0);
					if (aurDb == null) EmitWarning?.Invoke(DGettext(null, "Failed to initialize AUR database"));
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
			}
			var builtPkgs = new Dictionary<string, string>();
			var toInstallAsDepArray = new List<string>();
			while (_toBuildQueue.Count > 0)
			{
				string pkgname = _toBuildQueue.Dequeue();
				_buildCancellable.Reset();
				var builtPkgsPath = new List<string>();
				string realAurBuildDir = Database.GetRealAurBuildDir();
				string pkgdir = PathCompat.BuildFilename(realAurBuildDir, pkgname);
				bool asRoot = Posix.GetEuid() == 0;
				_building = true;
				StartBuilding?.Invoke();
				var cmdline = new List<string>();
				if (asRoot)
				{
					cmdline.AddRange(new[]
					{
						"systemd-run", "--service-type=oneshot", "--pipe", "--wait", "--pty",
						"--property=DynamicUser=yes", "--property=CacheDirectory=pamac",
						$"--property=WorkingDirectory=/var/cache/pamac/{pkgname}"
					});
				}
				cmdline.Add("makepkg");
				cmdline.Add("-cCf");
				cmdline.Add("--nocheck");
				if (!_config.KeepBuiltPkgs)
				{
					cmdline.Add("--nosign");
					cmdline.Add($"PKGDEST={pkgdir}");
					cmdline.Add("PKGEXT=.pkg.tar");
				}
				EmitScriptOutput?.Invoke("");
				EmitAction?.Invoke(string.Format(DGettext(null, "Building {0}"), pkgname) + "...");
				ImportantDetailsOutpout?.Invoke(false);
				int status = await RunCmdLineAsync(cmdline, pkgdir, _buildCancellable);
				if (_buildCancellable.IsCancelled) status = 1;
				else if (status == 1) EmitError?.Invoke(string.Format(DGettext(null, "Failed to build {0}"), pkgname), new List<string>());
				if (status == 0)
				{
					var listCmdline = new List<string>();
					if (asRoot)
					{
						listCmdline.AddRange(new[]
						{
							"systemd-run", "--service-type=oneshot", "--pipe", "--wait", "--pty",
							"--property=DynamicUser=yes", "--property=CacheDirectory=pamac",
							$"--property=WorkingDirectory=/var/cache/pamac/{pkgname}"
						});
					}
					listCmdline.Add("makepkg");
					listCmdline.Add("--packagelist");
					if (!_config.KeepBuiltPkgs)
					{
						listCmdline.Add($"PKGDEST={pkgdir}");
						listCmdline.Add("PKGEXT=.pkg.tar");
					}
					var listProc = new Subprocess(listCmdline.ToArray());
					listProc.Wait();
					status = listProc.ExitStatus;
					if (status == 0)
					{
						using var dis = new DataInputStream(listProc.GetStdOutPipe());
						string? line;
						while ((line = dis.ReadLine()) != null)
						{
							if (!_aurPkgsToInstall.Contains(line.Trim())) continue;
							builtPkgsPath.Add(line.Trim());
							builtPkgs[line.Trim()] = line.Trim();
							if (!_toBuild.Contains(line.Trim())) toInstallAsDepArray.Add(line.Trim());
						}
					}
				}
				StopBuilding?.Invoke();
				_building = false;
				if (status == 0 && builtPkgsPath.Count > 0)
				{
					bool builtPkgsNeeded = _toBuildQueue.Count == 0 || aurDb == null;
					if (!builtPkgsNeeded)
					{
						string nextPkgName = _toBuildQueue.Peek();
						var nextPkg = aurDb.GetPkg(nextPkgName);
						if (nextPkg == null) builtPkgsNeeded = true;
						else
						{
							foreach (var builtPkg in builtPkgs.Values)
							{
								var bp = aurDb.GetPkg(builtPkg);
								if (bp == null) { builtPkgsNeeded = true; break; }
								foreach (var depend in nextPkg.Depends)
								{
									if (Alpm.FindSatisfier(new List<Package> { bp }, depend.ComputeString()) != null)
									{ builtPkgsNeeded = true; break; }
								}
								if (builtPkgsNeeded) break;
							}
						}
					}
					if (builtPkgsNeeded)
					{
						var toLoadArray = new List<string>(builtPkgs.Values);
						success = await InstallBuiltPkgs(toLoadArray, toInstallAsDepArray);
						if (!success) break;
						builtPkgs.Clear();
						toInstallAsDepArray = new List<string>();
					}
				}
				else
				{
					ImportantDetailsOutpout?.Invoke(true);
					_toBuildQueue.Clear();
					success = false;
					break;
				}
			}
			try
			{
				var rm = new Subprocess(new[] { "rm", "-f", $"{_tmpPath}/pamac_aur.db", $"{tmpHandle?.DbPath}sync/pamac_aur.db" });
				rm.Wait();
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			return success;
		}

		async Task<bool> InstallBuiltPkgs(List<string> toLoadArray, List<string> toInstallAsDepArray)
		{
			bool success = false;
			try
			{
				EmitScriptOutput?.Invoke("");
				success = await _transactionInterface.TransRun(false, false, false, _config.KeepBuiltPkgs,
					0, new List<string>(), new List<string>(), toLoadArray, new List<string>(),
					toInstallAsDepArray, new List<string>(), new List<string>());
			}
			catch (Exception e)
			{
				var details = new List<string> { $"trans_run: {e.Message}" };
				EmitError?.Invoke("Daemon Error", details);
				success = false;
			}
			return success;
		}

		public void Cancel() => _alpmUtils.TransCancel("");
	}
}
