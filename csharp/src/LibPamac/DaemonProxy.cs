namespace Pamac
{
    /// <summary>
    /// Managed proxy for the pamac system D-Bus daemon
    /// (equivalent of the `Daemon` interface in daemon_interface.vala,
    /// exposed over the bus `org.manjaro.pamac.daemon`).
    ///
    /// The methods/signals mirror the daemon D-Bus interface. The default
    /// implementation here is a swappable transport: set <see cref="Transport"/>
    /// to connect to a real bus. Without a transport the daemon features are
    /// simply reported as failures, so the rest of the library still compiles
    /// and runs in-process.
    /// </summary>
    public interface IDaemonTransport
    {
        void StartGetAuthorization();
        void RemoveAuthorization();
        void StartWriteAlpmConfig(Dictionary<string, object> newAlpmConf);
        void StartWritePamacConfig(Dictionary<string, object> newPamacConf);
        void StartGenerateMirrorsList(string country);
        void StartCleanCache(string[] filenames);
        void StartCleanBuildFiles(string aurBuildDir);
        void StartSetPkgreason(string pkgname, uint reason);
        void StartDownloadUpdates();
        void StartDownloadPkgs(string[] urls);
        void StartTransRefresh(bool force);
        void StartTransRefreshFiles(bool force);
        void StartTransRefreshAur(bool force);
        void StartTransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
            int transFlags, string[] toInstall, string[] toRemove, string[] toLoadLocal, string[] toLoadRemote,
            string[] toInstallAsDep, string[] ignorepkgs, string[] overwriteFiles);
        void TransCancel();
        void Quit();
        void StartSnapTransRun(string[] toInstall, string[] toRemove);
        void StartSnapSwitchChannel(string snapName, string channel);
        void StartFlatpakTransRun(string[] toInstall, string[] toRemove, string[] toUpgrade);
    }

    public class DaemonProxy
    {
        public static IDaemonTransport? Transport { get; set; }

        public static DaemonProxy? Connect()
        {
            if (Transport == null)
            {
                // No bus transport configured: daemon features unavailable.
                Console.Error.WriteLine("pamac daemon transport not configured");
                return null;
            }
            return new DaemonProxy(Transport);
        }

        private readonly IDaemonTransport _t;

        private DaemonProxy(IDaemonTransport transport) => _t = transport;

        // ---- method calls ----
        public void StartGetAuthorization() => _t.StartGetAuthorization();
        public void RemoveAuthorization() => _t.RemoveAuthorization();
        public void StartWriteAlpmConfig(Dictionary<string, object> c) => _t.StartWriteAlpmConfig(c);
        public void StartWritePamacConfig(Dictionary<string, object> c) => _t.StartWritePamacConfig(c);
        public void StartGenerateMirrorsList(string country) => _t.StartGenerateMirrorsList(country);
        public void StartCleanCache(string[] filenames) => _t.StartCleanCache(filenames);
        public void StartCleanBuildFiles(string dir) => _t.StartCleanBuildFiles(dir);
        public void StartSetPkgreason(string pkgname, uint reason) => _t.StartSetPkgreason(pkgname, reason);
        public void StartDownloadUpdates() => _t.StartDownloadUpdates();
        public void StartDownloadPkgs(string[] urls) => _t.StartDownloadPkgs(urls);
        public void StartTransRefresh(bool force) => _t.StartTransRefresh(force);
        public void StartTransRefreshFiles(bool force) => _t.StartTransRefreshFiles(force);
        public void StartTransRefreshAur(bool force) => _t.StartTransRefreshAur(force);
        public void StartTransRun(bool s, bool ed, bool si, bool kbp, int tf, string[] i, string[] r,
            string[] ll, string[] lr, string[] iad, string[] ig, string[] of)
            => _t.StartTransRun(s, ed, si, kbp, tf, i, r, ll, lr, iad, ig, of);
        public void TransCancel() => _t.TransCancel();
        public void Quit() => _t.Quit();
        public void StartSnapTransRun(string[] i, string[] r) => _t.StartSnapTransRun(i, r);
        public void StartSnapSwitchChannel(string n, string c) => _t.StartSnapSwitchChannel(n, c);
        public void StartFlatpakTransRun(string[] i, string[] r, string[] u) => _t.StartFlatpakTransRun(i, r, u);

        // ---- signals ----
        public event Action<string, string>? EmitAction;
        public event Action<string, string, string, double>? EmitActionProgress;
        public event Action<string, string, string, double>? EmitDownloadProgress;
        public event Action<string, string, string, string, double>? EmitHookProgress;
        public event Action<string, string>? EmitScriptOutput;
        public event Action<string, string>? EmitWarning;
        public event Action<string, string, string[]>? EmitError;
        public event Action<string, bool>? ImportantDetailsOutput;
        public event Action<string>? StartDownloading;
        public event Action<string>? StopDownloading;
        public event Action<string, bool>? SetPkgreasonFinished;
        public event Action<string>? StartWaiting;
        public event Action<string>? StopWaiting;
        public event Action<string, string[]>? DownloadPkgsFinished;
        public event Action<string, bool>? TransRefreshFinished;
        public event Action<string, bool>? TransRefreshFilesFinished;
        public event Action<string, bool>? TransRefreshAurFinished;
        public event Action<string, bool>? TransRunFinished;
        public event Action<string, bool>? DownloadUpdatesFinished;
        public event Action<string, bool>? GetAuthorizationFinished;
        public event Action<string>? WriteAlpmConfigFinished;
        public event Action<string>? WritePamacConfigFinished;
        public event Action<string, string>? GenerateMirrorsListData;
        public event Action<string>? GenerateMirrorsListFinished;
        public event Action<string, bool>? CleanCacheFinished;
        public event Action<string, bool>? CleanBuildFilesFinished;
        public event Action<string, bool>? SnapTransRunFinished;
        public event Action<string, bool>? SnapSwitchChannelFinished;
        public event Action<string, bool>? FlatpakTransRunFinished;
    }
}
