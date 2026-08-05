using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pamac
{
    /// <summary>
    /// Executes transactions directly in-process when running as root
    /// (equivalent of transaction_interface_root.vala).
    /// </summary>
    internal class TransactionInterfaceRoot : TransactionInterface
    {
        private readonly AlpmUtils _alpmUtils;

        public TransactionInterfaceRoot(AlpmUtils alpmUtils)
        {
            _alpmUtils = alpmUtils;
            WireEvents();
        }

        private void WireEvents()
        {
            _alpmUtils.EmitAction += (sender, action) => EmitAction?.Invoke(action);
            _alpmUtils.EmitActionProgress += (sender, action, status, progress) => EmitActionProgress?.Invoke(action, status, progress);
            _alpmUtils.EmitDownloadProgress += (sender, action, status, progress) => EmitDownloadProgress?.Invoke(action, status, progress);
            _alpmUtils.EmitHookProgress += (sender, action, details, status, progress) => EmitHookProgress?.Invoke(action, details, status, progress);
            _alpmUtils.EmitScriptOutput += (sender, message) => EmitScriptOutput?.Invoke(message);
            _alpmUtils.EmitWarning += (sender, message) => EmitWarning?.Invoke(message);
            _alpmUtils.EmitError += (sender, message, details) => EmitError?.Invoke(message, details);
            _alpmUtils.ImportantDetailsOutput += (sender, mustShow) => ImportantDetailsOutput?.Invoke(mustShow);
            _alpmUtils.StartDownloading += (sender) => StartDownloading?.Invoke("");
            _alpmUtils.StopDownloading += (sender) => StopDownloading?.Invoke("");
        }

        public Task<bool> GetAuthorizationAsync() => Task.FromResult(true);

        public void RemoveAuthorization() { }

        public Task GenerateMirrorsListAsync(string country)
        {
            Utils.RunCommandSync($"pacman-mirrors -c {country}", out string output);
            foreach (string line in output.Split('\n'))
                GenerateMirrorsListData?.Invoke(line);
            return Task.CompletedTask;
        }

        public Task<bool> CleanCacheAsync(List<string> filenames) => Task.FromResult(_alpmUtils.CleanCache(filenames.ToArray()));

        public Task<bool> CleanBuildFilesAsync(string aurBuildDir) => Task.FromResult(_alpmUtils.CleanBuildFiles(aurBuildDir));

        public Task<bool> SetPkgreasonAsync(string pkgname, uint reason) => Task.FromResult(_alpmUtils.SetPkgreason("", pkgname, reason));

        public Task<bool> DownloadUpdatesAsync() => Task.FromResult(_alpmUtils.DownloadUpdates(""));

        public Task<List<string>> DownloadPkgsAsync(List<string> urls)
        {
            var paths = new List<string>();
            _alpmUtils.DownloadPkgs("", urls, ref paths);
            return Task.FromResult(paths);
        }

        public Task<bool> TransRefreshAsync(bool force) => Task.FromResult(_alpmUtils.TransRefresh("", force));

        public Task<bool> TransRefreshFilesAsync(bool force) => Task.FromResult(_alpmUtils.TransRefreshFiles("", force));

        public Task<bool> TransRefreshAurAsync(bool force) => Task.FromResult(true);

        public Task<bool> TransRunAsync(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
            int transFlags, List<string> toInstall, List<string> toRemove, List<string> toLoadLocal,
            List<string> toLoadRemote, List<string> toInstallAsDep, List<string> ignorepkgs, List<string> overwriteFiles)
        {
            return Task.Run(() => _alpmUtils.TransRun("", sysupgrade, enableDowngrade, simpleInstall, keepBuiltPkgs,
                transFlags, toInstall, toRemove, toLoadLocal, toLoadRemote, toInstallAsDep, ignorepkgs, overwriteFiles));
        }

        public void TransCancel() => _alpmUtils.TransCancel("");

        public void QuitDaemon() { }

        public Task<bool> SnapTransRunAsync(List<string> toInstall, List<string> toRemove) => Task.FromResult(true);
        public Task<bool> SnapSwitchChannelAsync(string snapName, string channel) => Task.FromResult(true);
        public Task<bool> FlatpakTransRunAsync(List<string> toInstall, List<string> toRemove, List<string> toUpgrade) => Task.FromResult(true);

        public event Action<string>? EmitAction;
        public event Action<string, string, double>? EmitActionProgress;
        public event Action<string>? StartDownloading;
        public event Action<string>? StopDownloading;
        public event Action<string>? StartWaiting;
        public event Action<string>? StopWaiting;
        public event Action<string, string, double>? EmitDownloadProgress;
        public event Action<string, string, string, double>? EmitHookProgress;
        public event Action<string>? EmitScriptOutput;
        public event Action<string>? EmitWarning;
        public event Action<string, List<string>>? EmitError;
        public event Action<bool>? ImportantDetailsOutput;
        public event Action<string>? GenerateMirrorsListData;
    }
}
