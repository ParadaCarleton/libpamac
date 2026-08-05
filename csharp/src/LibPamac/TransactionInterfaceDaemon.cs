using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pamac
{
    /// <summary>
    /// Forwards transactions to the pamac system daemon over D-Bus
    /// (equivalent of transaction_interface_daemon.vala). Uses
    /// <see cref="DaemonProxy"/> as the transport.
    /// </summary>
    internal class TransactionInterfaceDaemon : TransactionInterface
    {
        private readonly DaemonProxy _daemon;

        public TransactionInterfaceDaemon(Config config)
        {
            _daemon = DaemonProxy.Connect() ?? throw new InvalidOperationException("cannot connect to pamac daemon");
            if (_daemon == null) throw new InvalidOperationException("cannot connect to pamac daemon");
            WireEvents();
        }

        private void WireEvents()
        {
            _daemon.EmitAction += (sender, action) => EmitAction?.Invoke(action);
            _daemon.EmitActionProgress += (sender, action, status, progress) => EmitActionProgress?.Invoke(action, status, progress);
            _daemon.EmitDownloadProgress += (sender, action, status, progress) => EmitDownloadProgress?.Invoke(action, status, progress);
            _daemon.EmitHookProgress += (sender, action, details, status, progress) => EmitHookProgress?.Invoke(action, details, status, progress);
            _daemon.EmitScriptOutput += (sender, message) => EmitScriptOutput?.Invoke(message);
            _daemon.EmitWarning += (sender, message) => EmitWarning?.Invoke(message);
            _daemon.EmitError += (sender, message, details) => EmitError?.Invoke(message, new List<string>(details));
            _daemon.ImportantDetailsOutput += (sender, mustShow) => ImportantDetailsOutput?.Invoke(mustShow);
            _daemon.StartDownloading += (sender) => StartDownloading?.Invoke(sender);
            _daemon.StopDownloading += (sender) => StopDownloading?.Invoke(sender);
            _daemon.StartWaiting += (sender) => StartWaiting?.Invoke(sender);
            _daemon.StopWaiting += (sender) => StopWaiting?.Invoke(sender);
            _daemon.GenerateMirrorsListData += (sender, line) => GenerateMirrorsListData?.Invoke(line);
        }

        public async Task<bool> GetAuthorizationAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<string, bool>? handler = null;
            handler = (sender, authorized) => { tcs.TrySetResult(authorized); _daemon.GetAuthorizationFinished -= handler; };
            _daemon.GetAuthorizationFinished += handler;
            _daemon.StartGetAuthorization();
            return await tcs.Task;
        }

        public void RemoveAuthorization() => _daemon.RemoveAuthorization();

        public async Task GenerateMirrorsListAsync(string country)
        {
            var tcs = new TaskCompletionSource();
            Action<string>? handler = null;
            handler = (sender) => { tcs.TrySetResult(); _daemon.GenerateMirrorsListFinished -= handler; };
            _daemon.GenerateMirrorsListFinished += handler;
            _daemon.StartGenerateMirrorsList(country);
            await tcs.Task;
        }

        public async Task<bool> CleanCacheAsync(List<string> filenames)
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<string, bool>? handler = null;
            handler = (sender, success) => { tcs.TrySetResult(success); _daemon.CleanCacheFinished -= handler; };
            _daemon.CleanCacheFinished += handler;
            _daemon.StartCleanCache(filenames.ToArray());
            return await tcs.Task;
        }

        public async Task<bool> CleanBuildFilesAsync(string aurBuildDir)
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<string, bool>? handler = null;
            handler = (sender, success) => { tcs.TrySetResult(success); _daemon.CleanBuildFilesFinished -= handler; };
            _daemon.CleanBuildFilesFinished += handler;
            _daemon.StartCleanBuildFiles(aurBuildDir);
            return await tcs.Task;
        }

        public async Task<bool> SetPkgreasonAsync(string pkgname, uint reason)
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<string, bool>? handler = null;
            handler = (sender, success) => { tcs.TrySetResult(success); _daemon.SetPkgreasonFinished -= handler; };
            _daemon.SetPkgreasonFinished += handler;
            _daemon.StartSetPkgreason(pkgname, reason);
            return await tcs.Task;
        }

        public async Task<bool> DownloadUpdatesAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<string, bool>? handler = null;
            handler = (sender, success) => { tcs.TrySetResult(success); _daemon.DownloadUpdatesFinished -= handler; };
            _daemon.DownloadUpdatesFinished += handler;
            _daemon.StartDownloadUpdates();
            return await tcs.Task;
        }

        public async Task<List<string>> DownloadPkgsAsync(List<string> urls)
        {
            var tcs = new TaskCompletionSource<List<string>>();
            Action<string, string[]>? handler = null;
            handler = (sender, paths) => { tcs.TrySetResult(new List<string>(paths)); _daemon.DownloadPkgsFinished -= handler; };
            _daemon.DownloadPkgsFinished += handler;
            _daemon.StartDownloadPkgs(urls.ToArray());
            return await tcs.Task;
        }

        public Task<bool> TransRefreshAsync(bool force) => AwaitBool(s => _daemon.TransRefreshFinished += s, h => _daemon.TransRefreshFinished -= h, () => _daemon.StartTransRefresh(force));
        public Task<bool> TransRefreshFilesAsync(bool force) => AwaitBool(s => _daemon.TransRefreshFilesFinished += s, h => _daemon.TransRefreshFilesFinished -= h, () => _daemon.StartTransRefreshFiles(force));
        public Task<bool> TransRefreshAurAsync(bool force) => AwaitBool(s => _daemon.TransRefreshAurFinished += s, h => _daemon.TransRefreshAurFinished -= h, () => _daemon.StartTransRefreshAur(force));

        public Task<bool> TransRunAsync(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
            int transFlags, List<string> toInstall, List<string> toRemove, List<string> toLoadLocal,
            List<string> toLoadRemote, List<string> toInstallAsDep, List<string> ignorepkgs, List<string> overwriteFiles)
        {
            return AwaitBool(s => _daemon.TransRunFinished += s, h => _daemon.TransRunFinished -= h, () =>
                _daemon.StartTransRun(sysupgrade, enableDowngrade, simpleInstall, keepBuiltPkgs, transFlags,
                    toInstall.ToArray(), toRemove.ToArray(), toLoadLocal.ToArray(), toLoadRemote.ToArray(),
                    toInstallAsDep.ToArray(), ignorepkgs.ToArray(), overwriteFiles.ToArray()));
        }

        public void TransCancel() => _daemon.TransCancel();

        public void QuitDaemon() => _daemon.Quit();

        public Task<bool> SnapTransRunAsync(List<string> toInstall, List<string> toRemove)
            => AwaitBool(s => _daemon.SnapTransRunFinished += s, h => _daemon.SnapTransRunFinished -= h, () => _daemon.StartSnapTransRun(toInstall.ToArray(), toRemove.ToArray()));

        public Task<bool> SnapSwitchChannelAsync(string snapName, string channel)
            => AwaitBool(s => _daemon.SnapSwitchChannelFinished += s, h => _daemon.SnapSwitchChannelFinished -= h, () => _daemon.StartSnapSwitchChannel(snapName, channel));

        public Task<bool> FlatpakTransRunAsync(List<string> toInstall, List<string> toRemove, List<string> toUpgrade)
            => AwaitBool(s => _daemon.FlatpakTransRunFinished += s, h => _daemon.FlatpakTransRunFinished -= h, () => _daemon.StartFlatpakTransRun(toInstall.ToArray(), toRemove.ToArray(), toUpgrade.ToArray()));

        private static async Task<bool> AwaitBool(Action<Action<string, bool>> subscribe, Action<Action<string, bool>> unsubscribe, Action start)
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<string, bool>? handler = null;
            handler = (sender, success) => { tcs.TrySetResult(success); unsubscribe(handler); };
            subscribe(handler);
            start();
            return await tcs.Task;
        }

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
