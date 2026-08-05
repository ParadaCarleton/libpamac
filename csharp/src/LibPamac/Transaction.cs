using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pamac.LibAlpm;

namespace Pamac
{
    /// <summary>
    /// Public transaction API (equivalent of transaction.vala). Collects the
    /// set of packages to install/remove/upgrade and drives them through the
    /// TransactionInterface (either directly as root or via the daemon).
    /// </summary>
    public class Transaction
    {
        private TransactionInterface _transactionInterface = null!;
        private bool _waiting;
        private readonly Config _config;
        private AlpmUtils? _alpmUtils;

        // run transaction data
        private bool _sysupgrading;
        private bool _forceRefresh;
        private int _transFlags;
        private readonly HashSet<string> _toInstall = new();
        private readonly HashSet<string> _toRemove = new();
        private readonly HashSet<string> _toLoadLocal = new();
        private readonly HashSet<string> _toLoadRemote = new();
        private readonly HashSet<string> _toBuild = new();
        private readonly HashSet<string> _cloneFiles = new();
        private readonly HashSet<string> _cloneDepsFiles = new();
        private readonly HashSet<string> _ignorepkgs = new();
        private readonly HashSet<string> _overwriteFiles = new();
        private readonly HashSet<string> _toInstallAsDep = new();
        private readonly Dictionary<string, SnapPackage> _snapToInstall = new();
        private readonly Dictionary<string, SnapPackage> _snapToRemove = new();
        private readonly Dictionary<string, FlatpakPackage> _flatpakToInstall = new();
        private readonly Dictionary<string, FlatpakPackage> _flatpakToRemove = new();
        private readonly Dictionary<string, FlatpakPackage> _flatpakToUpgrade = new();

        public Database Database { get; }

        // transaction options
        public bool DownloadOnly { get; set; }
        public bool DryRun { get; set; }
        public bool InstallIfNeeded { get; set; }
        public bool RemoveIfUnneeded { get; set; }
        public bool Cascade { get; set; }
        public bool KeepConfigFiles { get; set; }
        public bool InstallAsDep { get; set; }
        public bool InstallAsExplicit { get; set; }
        public bool NoRefresh { get; set; }

        // ---- signals ----
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
        public event Action<bool>? ImportantDetailsOutput;

        public Transaction(Database database)
        {
            Database = database;
            _config = database.Config;
            _alpmUtils = new AlpmUtils(_config);
            bool isRoot = geteuid() == 0;
            _transactionInterface = isRoot
                ? new TransactionInterfaceRoot(_alpmUtils)
                : CreateDaemonInterface();
            // defaults
            DownloadOnly = false;
            DryRun = false;
            InstallIfNeeded = true;
            RemoveIfUnneeded = false;
            Cascade = false;
            KeepConfigFiles = true;
            InstallAsDep = false;
            InstallAsExplicit = false;
            NoRefresh = false;
            ConnectingSignals();
        }

        private TransactionInterface CreateDaemonInterface()
        {
            try
            {
                return new TransactionInterfaceDaemon(_config);
            }
            catch
            {
                // fall back to running in-process
                return new TransactionInterfaceRoot(_alpmUtils!);
            }
        }

        private static int geteuid()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("id", "-u")
                {
                    RedirectStandardOutput = true, UseShellExecute = false
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return int.TryParse(o, out int u) ? u : 1000;
            }
            catch { return 1000; }
        }

        public void QuitDaemon()
        {
            try { _transactionInterface.QuitDaemon(); }
            catch (Exception e) { EmitError?.Invoke("Daemon Error", new List<string> { $"quit_daemon: {e.Message}" }); }
        }

        // ---- protected virtual hooks (overridden by the UI) ----
        protected virtual Task<bool> AskCommitAsync(TransactionSummary summary) => Task.FromResult(true);
        protected virtual Task<bool> AskEditBuildFilesAsync(TransactionSummary summary) => Task.FromResult(false);
        protected virtual Task EditBuildFilesAsync(List<string> pkgnames) => Task.CompletedTask;
        protected virtual Task<bool> AskImportKeyAsync(string pkgname, string key, string? owner) => Task.FromResult(false);
        protected virtual Task<List<string>> ChooseOptdepsAsync(string pkgname, List<string> optdeps) => Task.FromResult(new List<string>());
        protected virtual Task<int> ChooseProviderAsync(string depend, List<string> providers) => Task.FromResult(0);
        protected virtual Task<bool> AskSnapInstallClassicAsync(string name) => Task.FromResult(true);

        public async Task<bool> GetAuthorizationAsync() => await _transactionInterface.GetAuthorizationAsync();
        public void RemoveAuthorization() => _transactionInterface.RemoveAuthorization();

        public async Task GenerateMirrorsListAsync(string country) => await _transactionInterface.GenerateMirrorsListAsync(country);

        public async Task CleanCacheAsync()
        {
            var filenames = new List<string>();
            var details = await Database.GetCleanCacheDetailsAsync();
            filenames.AddRange(details.Keys);
            await _transactionInterface.CleanCacheAsync(filenames);
        }

        public async Task CleanBuildFilesAsync()
        {
            await _transactionInterface.CleanBuildFilesAsync(_config.AurBuildDir);
        }

        public async Task<bool> SetPkgreasonAsync(string pkgname, uint reason)
            => await _transactionInterface.SetPkgreasonAsync(pkgname, reason);

        public async Task<bool> DownloadUpdatesAsync() => await _transactionInterface.DownloadUpdatesAsync();

        public async Task<bool> RefreshDbsAsync()
        {
            bool success = true;
            if (Database.NeedRefresh())
            {
                if (_alpmUtils != null)
                    success = await _transactionInterface.TransRefreshAsync(_forceRefresh);
            }
            return success;
        }

        public async Task<bool> RefreshFilesDbsAsync()
        {
            return await _transactionInterface.TransRefreshFilesAsync(_forceRefresh);
        }

        public async Task CheckDbsAsync()
        {
            if (Database.NeedRefresh())
                await RefreshDbsAsync();
        }

        // ---- collect packages ----
        public void AddPkgToInstall(string name) => _toInstall.Add(name);
        public void AddPkgToRemove(string name) => _toRemove.Add(name);
        public void AddPathToLoad(string path) => _toLoadLocal.Add(path);
        public void AddPkgToBuild(string name, bool cloneBuildFiles, bool cloneDepsBuildFiles)
        {
            _toBuild.Add(name);
            if (cloneBuildFiles) _cloneFiles.Add(name);
            if (cloneDepsBuildFiles) _cloneDepsFiles.Add(name);
        }
        public void AddTemporaryIgnorePkg(string name) => _ignorepkgs.Add(name);
        public void AddOverwriteFile(string glob) => _overwriteFiles.Add(glob);
        public void AddPkgToMarkAsDep(string name) => _toInstallAsDep.Add(name);

        public void AddPkgsToUpgrade(bool forceRefresh)
        {
            _sysupgrading = true;
            _forceRefresh = forceRefresh;
        }

        public void AddSnapToInstall(SnapPackage pkg) => _snapToInstall[pkg.Name] = pkg;
        public void AddSnapToRemove(SnapPackage pkg) => _snapToRemove[pkg.Name] = pkg;
        public async Task<bool> SnapSwitchChannelAsync(string snapName, string channel)
            => await _transactionInterface.SnapSwitchChannelAsync(snapName, channel);
        public void AddFlatpakToInstall(FlatpakPackage pkg) => _flatpakToInstall[pkg.Name] = pkg;
        public void AddFlatpakToRemove(FlatpakPackage pkg) => _flatpakToRemove[pkg.Name] = pkg;
        public void AddFlatpakToUpgrade(FlatpakPackage pkg) => _flatpakToUpgrade[pkg.Name] = pkg;

        private int ComputeTransFlags()
        {
            int flags = 0;
            if (!InstallIfNeeded) flags |= (int)TransFlag.NEEDED;
            if (Cascade) flags |= (int)TransFlag.CASCADE;
            if (RemoveIfUnneeded) flags |= (int)TransFlag.RECURSE | (int)TransFlag.RECURSEALL;
            if (InstallAsDep) flags |= (int)TransFlag.ALLDEPS;
            if (InstallAsExplicit) flags |= (int)TransFlag.ALLEXPLICIT;
            if (!KeepConfigFiles) flags |= (int)TransFlag.NOSAVE;
            if (DownloadOnly) flags |= (int)TransFlag.DOWNLOADONLY;
            if (DryRun) flags |= (int)TransFlag.DBONLY;
            return flags;
        }

        private async Task<bool> RunAlpmTransactionAsync()
        {
            if (_toInstall.Count + _toRemove.Count + _toLoadLocal.Count + _toLoadRemote.Count == 0 && !_sysupgrading)
                return true;

            var summary = new TransactionSummary();
            // build summary from db lookups
            foreach (string name in _toInstall)
            {
                var pkg = Database.GetSyncPkg(name);
                if (pkg != null) summary.ToInstall.Add(pkg);
            }
            foreach (string name in _toRemove)
            {
                var pkg = Database.GetInstalledPkg(name);
                if (pkg != null) summary.ToRemove.Add(pkg);
            }

            bool ok = await AskCommitAsync(summary);
            if (!ok) return false;

            bool success = await _transactionInterface.TransRunAsync(_sysupgrading, _config.EnableDowngrade,
                _config.SimpleInstall, _config.KeepBuiltPkgs, ComputeTransFlags(),
                new List<string>(_toInstall), new List<string>(_toRemove), new List<string>(_toLoadLocal),
                new List<string>(_toLoadRemote), new List<string>(_toInstallAsDep),
                new List<string>(_ignorepkgs), new List<string>(_overwriteFiles));
            return success;
        }

        private async Task<bool> RunSnapTransactionAsync()
        {
            if (_snapToInstall.Count == 0 && _snapToRemove.Count == 0) return true;
            return await _transactionInterface.SnapTransRunAsync(
                new List<string>(_snapToInstall.Keys), new List<string>(_snapToRemove.Keys));
        }

        private async Task<bool> RunFlatpakTransactionAsync()
        {
            if (_flatpakToInstall.Count == 0 && _flatpakToRemove.Count == 0 && _flatpakToUpgrade.Count == 0) return true;
            return await _transactionInterface.FlatpakTransRunAsync(
                new List<string>(_flatpakToInstall.Keys), new List<string>(_flatpakToRemove.Keys),
                new List<string>(_flatpakToUpgrade.Keys));
        }

        /// <summary>Main entry point: run the whole transaction (equivalent of run_async).</summary>
        public async Task<bool> RunAsync()
        {
            if (Database.NeedRefresh() && !NoRefresh)
            {
                bool refreshed = await RefreshDbsAsync();
                if (!refreshed) return false;
            }

            StartPreparing?.Invoke();
            bool success = false;
            try
            {
                success = await RunAlpmTransactionAsync();
                if (success) success = await RunSnapTransactionAsync();
                if (success) success = await RunFlatpakTransactionAsync();
            }
            finally
            {
                StopPreparing?.Invoke();
                ResetTransactionState();
            }
            return success;
        }

        private void ResetTransactionState()
        {
            _toInstall.Clear();
            _toRemove.Clear();
            _toLoadLocal.Clear();
            _toLoadRemote.Clear();
            _toBuild.Clear();
            _cloneFiles.Clear();
            _cloneDepsFiles.Clear();
            _overwriteFiles.Clear();
            _toInstallAsDep.Clear();
            _sysupgrading = false;
            _forceRefresh = false;
        }

        public void Cancel()
        {
            if (_waiting)
            {
                _waiting = false;
                return;
            }
            try { _transactionInterface.TransCancel(); }
            catch (Exception e)
            {
                EmitError?.Invoke("Daemon Error", new List<string> { $"trans_cancel: {e.Message}" });
            }
        }

        private void ConnectingSignals()
        {
            _transactionInterface.EmitAction += OnEmitAction;
            _transactionInterface.EmitActionProgress += OnEmitActionProgress;
            _transactionInterface.EmitDownloadProgress += OnEmitDownloadProgress;
            _transactionInterface.EmitHookProgress += OnEmitHookProgress;
            _transactionInterface.EmitScriptOutput += OnEmitScriptOutput;
            _transactionInterface.EmitWarning += OnEmitWarning;
            _transactionInterface.EmitError += OnEmitError;
            _transactionInterface.ImportantDetailsOutput += OnImportantDetailsOutput;
            _transactionInterface.StartDownloading += _ => StartDownloading?.Invoke();
            _transactionInterface.StopDownloading += _ => StopDownloading?.Invoke();
            _transactionInterface.StartWaiting += _ => StartWaiting?.Invoke();
            _transactionInterface.StopWaiting += _ => StopWaiting?.Invoke();
        }

        private void OnEmitAction(string action) => EmitAction?.Invoke(action);
        private void OnEmitActionProgress(string action, string status, double progress) => EmitActionProgress?.Invoke(action, status, progress);
        private void OnEmitDownloadProgress(string action, string status, double progress) => EmitDownloadProgress?.Invoke(action, status, progress);
        private void OnEmitHookProgress(string action, string details, string status, double progress) => EmitHookProgress?.Invoke(action, details, status, progress);
        private void OnEmitScriptOutput(string message) => EmitScriptOutput?.Invoke(message);
        private void OnEmitWarning(string message) => EmitWarning?.Invoke(message);
        private void OnEmitError(string message, List<string> details) => EmitError?.Invoke(message, details);
        private void OnImportantDetailsOutput(bool mustShow) => ImportantDetailsOutput?.Invoke(mustShow);
    }
}
