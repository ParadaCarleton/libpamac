using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pamac
{
    /// <summary>
    /// Abstraction over how a transaction is executed: either directly in
    /// process (root) or forwarded to the D-Bus daemon (equivalent of
    /// transaction_interface.vala).
    /// </summary>
    internal interface TransactionInterface
    {
        Task<bool> GetAuthorizationAsync();
        void RemoveAuthorization();
        Task GenerateMirrorsListAsync(string country);
        Task<bool> CleanCacheAsync(List<string> filenames);
        Task<bool> CleanBuildFilesAsync(string aurBuildDir);
        Task<bool> SetPkgreasonAsync(string pkgname, uint reason);
        Task<bool> DownloadUpdatesAsync();
        Task<List<string>> DownloadPkgsAsync(List<string> urls);
        Task<bool> TransRefreshAsync(bool force);
        Task<bool> TransRefreshFilesAsync(bool force);
        Task<bool> TransRefreshAurAsync(bool force);
        Task<bool> TransRunAsync(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
            int transFlags, List<string> toInstall, List<string> toRemove, List<string> toLoadLocal,
            List<string> toLoadRemote, List<string> toInstallAsDep, List<string> ignorepkgs, List<string> overwriteFiles);
        void TransCancel();
        void QuitDaemon();
        Task<bool> SnapTransRunAsync(List<string> toInstall, List<string> toRemove);
        Task<bool> SnapSwitchChannelAsync(string snapName, string channel);
        Task<bool> FlatpakTransRunAsync(List<string> toInstall, List<string> toRemove, List<string> toUpgrade);

        // signals
        event Action<string>? EmitAction;
        event Action<string, string, double>? EmitActionProgress;
        event Action<string>? StartDownloading;
        event Action<string>? StopDownloading;
        event Action<string>? StartWaiting;
        event Action<string>? StopWaiting;
        event Action<string, string, double>? EmitDownloadProgress;
        event Action<string, string, string, double>? EmitHookProgress;
        event Action<string>? EmitScriptOutput;
        event Action<string>? EmitWarning;
        event Action<string, List<string>>? EmitError;
        event Action<bool>? ImportantDetailsOutput;
        event Action<string>? GenerateMirrorsListData;
    }
}
