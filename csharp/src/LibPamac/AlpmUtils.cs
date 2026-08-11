using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Pamac.LibAlpm;

namespace Pamac
{
    /// <summary>
    /// The alpm transaction engine (equivalent of alpm_utils.vala). Runs
    /// refresh/install/remove transactions against a libalpm handle and reports
    /// progress via the signal-like events on this class.
    /// </summary>
    internal class AlpmUtils
    {
        // ---- callback ctx management ----
        private static readonly object CtxLock = new();
        private static int _ctxCounter;
        private static readonly Dictionary<IntPtr, AlpmUtils> CtxMap = new();

        private string _sender = "";
        private readonly Config _config;
        private readonly string _tmpPath;
        public AlpmHandle? AlpmHandle;
        private readonly object _handleLock = new();
        private CancellationTokenSource? _cancelCts = new();

        // run transaction data
        public CancellationToken CancellationToken => _cancelCts!.Token;

        // progress data
        public string CurrentFilename = "";
        public string CurrentAction = "";
        public double CurrentProgress;
        public List<string> Unresolvables = new();

        // download data
        public ulong TotalDownload;
        public ulong AlreadyDownloaded;
        private readonly Dictionary<string, ulong> _multiProgress = new();
        private double _downloadRate;

        // ---- signals (the Vala signals, exposed as events) ----
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
        public event Action<string, bool>? ImportantDetailsOutput;

        public AlpmUtils(Config config)
        {
            _config = config;
            _tmpPath = $"/tmp/pamac-{Environment.UserName}";
            CheckOldLock();
        }

        // ---- do_* helpers ----
        public int DoChooseProvider(string depend, List<string> providers) => ChooseProvider?.Invoke(depend, providers) ?? 0;
        private void DoStartDownloading() => StartDownloading?.Invoke(_sender);
        private void DoStopDownloading() => StopDownloading?.Invoke(_sender);
        public void DoEmitAction(string action) => EmitAction?.Invoke(_sender, action);
        private void DoEmitActionProgress(string action, string status, double progress) => EmitActionProgress?.Invoke(_sender, action, status, progress);
        private void DoEmitDownloadProgress(string action, string status, double progress) => EmitDownloadProgress?.Invoke(_sender, action, status, progress);
        private void DoEmitHookProgress(string action, string details, string status, double progress) => EmitHookProgress?.Invoke(_sender, action, details, status, progress);
        public void DoEmitScriptOutput(string message) => EmitScriptOutput?.Invoke(_sender, message);
        private void DoEmitWarning(string message) => EmitWarning?.Invoke(_sender, message);
        private void DoEmitError(string message, List<string> details) => EmitError?.Invoke(_sender, message, details);
        private void DoImportantDetailsOutput(bool mustShow) => ImportantDetailsOutput?.Invoke(_sender, mustShow);

        private void CheckOldLock()
        {
            var handle = GetHandle(filesDb: false, tmpDb: false, callbacks: false);
            if (handle == null) return;
            string lockfile = handle.Lockfile;
            if (File.Exists(lockfile))
            {
                try
                {
                    long lockfileTime = new DateTimeOffset(File.GetLastWriteTimeUtc(lockfile)).ToUnixTimeSeconds();
                    long bootTime = GetBootTime();
                    if (bootTime > 0 && lockfileTime < bootTime)
                        File.Delete(lockfile);
                }
                catch { }
            }
            handle.Release();
        }

        private static long GetBootTime()
        {
            try
            {
                foreach (string line in File.ReadLines("/proc/stat"))
                {
                    if (line.StartsWith("btime "))
                    {
                        var split = line.Split(' ');
                        if (split.Length == 2 && long.TryParse(split[1], out long b)) return b;
                    }
                }
            }
            catch { }
            return -1;
        }

        public AlpmHandle? GetHandle(bool filesDb = false, bool tmpDb = false, bool callbacks = true)
        {
            var alpmConfig = _config.AlpmConfig;
            lock (_handleLock)
            {
                alpmConfig.Reload();
                var handle = alpmConfig.GetHandle(filesDb, tmpDb);
                if (handle == null)
                {
                    DoEmitError("Alpm Error", new List<string> { "Failed to initialize alpm library" });
                    return null;
                }
                if (callbacks) SetCallbacks(handle);
                alpmConfig.RegisterSyncdbs(handle);
                return handle;
            }
        }

        private void SetCallbacks(AlpmHandle handle)
        {
            IntPtr ctx = RegisterCtx(this);
            handle.SetEventcb(EventCb, ctx);
            handle.SetProgresscb(ProgressCb, ctx);
            handle.SetQuestioncb(QuestionCb, ctx);
            handle.SetDlcb(DownloadCb, ctx);
            handle.SetLogcb(LogCb, ctx);
        }

        private static IntPtr RegisterCtx(AlpmUtils instance)
        {
            lock (CtxLock)
            {
                IntPtr key = new IntPtr(++_ctxCounter);
                CtxMap[key] = instance;
                return key;
            }
        }
        private static AlpmUtils? FromCtx(IntPtr ctx)
        {
            lock (CtxLock) return CtxMap.TryGetValue(ctx, out var u) ? u : null;
        }

        // ---- simple operations ----
        public bool SetPkgreason(string sender, string pkgname, uint reason)
        {
            _sender = sender;
            var handle = GetHandle(callbacks: false);
            if (handle == null) return false;
            var pkg = handle.LocalDb.GetPkg(pkgname);
            if (pkg != null)
            {
                if (handle.TransInit(TransFlag.NONE) == 0)
                {
                    pkg.SetReason((PackageReason)reason);
                    handle.TransRelease();
                    handle.Release();
                    return true;
                }
            }
            handle.Release();
            return false;
        }

        public bool CleanCache(string[] filenames)
        {
            foreach (string filename in filenames)
            {
                try { if (File.Exists(filename)) File.Delete(filename); }
                catch (Exception e) { Console.Error.WriteLine(e.Message); }
            }
            return true;
        }

        internal bool CleanBuildFiles(string aurBuildDir)
        {
            if (!Directory.Exists(aurBuildDir)) return true;
            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(aurBuildDir))
                {
                    if (Path.GetFileName(entry) == "packages-meta-ext-v1.json.gz") continue;
                    if (Directory.Exists(entry)) Directory.Delete(entry, true);
                    else File.Delete(entry);
                }
                return true;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
            return false;
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

        private bool UpdateDbs(AlpmHandle handle, bool force)
        {
            return handle.UpdateDbs(handle.Syncdbs, force) == 0;
        }

        public bool TransRefresh(string sender, bool forceRefresh)
        {
            _sender = sender;
            DoStartDownloading();
            var handle = GetHandle();
            if (handle == null) return false;
            bool success = UpdateDbs(handle, forceRefresh);
            handle.Release();
            DoStopDownloading();
            return success;
        }

        public bool TransRefreshFiles(string sender, bool forceRefresh)
        {
            _sender = sender;
            DoStartDownloading();
            var handle = GetHandle(filesDb: true);
            if (handle == null) return false;
            bool success = UpdateDbs(handle, forceRefresh);
            handle.Release();
            DoStopDownloading();
            return success;
        }

        // ---- downloads ----
        public bool DownloadUpdates(string sender)
        {
            _sender = sender;
            var handle = GetHandle();
            if (handle == null) return false;
            bool success = true;
            try
            {
                var updates = new List<AlpmPkg>();
                foreach (IntPtr p in handle.LocalDb.Pkgcache)
                {
                    var installed = AlpmPkg.FromPointer(p)!;
                    var candidate = installed.GetNewVersion(handle.Syncdbs);
                    if (candidate != null && handle.ShouldIgnore(installed) == 0 && handle.ShouldIgnore(candidate) == 0)
                        updates.Add(candidate);
                }
                if (updates.Count > 0)
                {
                    if (handle.TransInit(TransFlag.DOWNLOADONLY | TransFlag.NOLOCK) == 0)
                    {
                        foreach (var u in updates) handle.TransAddPkg(u);
                        int prepare = handle.TransPrepare();
                        int commit = handle.TransCommit();
                        handle.TransRelease();
                        success = prepare == 0 && commit == 0;
                    }
                }
            }
            finally
            {
                handle.Release();
            }
            return success;
        }

        public List<string> DownloadPkgs(string sender, List<string> urls, ref List<string> dloadPaths)
        {
            _sender = sender;
            DoStartDownloading();
            foreach (string url in urls)
            {
                string filename = Path.GetFileName(url);
                string path = Path.Combine("/var/cache/pamac/", filename);
                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    var data = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, data);
                    dloadPaths.Add(path);
                }
                catch (Exception e)
                {
                    DoEmitWarning($"failed to download {url}: {e.Message}");
                }
            }
            DoStopDownloading();
            return dloadPaths;
        }

        public void TransCancel(string sender)
        {
            _sender = sender;
            _cancelCts?.Cancel();
            if (AlpmHandle != null) AlpmHandle.TransInterrupt();
        }

        // ---- the main transaction ----
        public bool TransRun(string sender, bool sysupgrade, bool enableDowngrade, bool simpleInstall,
            bool keepBuiltPkgs, int transFlags, List<string> toInstall, List<string> toRemove,
            List<string> toLoadLocal, List<string> toLoadRemote, List<string> toInstallAsDep,
            List<string> ignorepkgs, List<string> overwriteFiles)
        {
            _sender = sender;
            _cancelCts = new CancellationTokenSource();
            Unresolvables = new List<string>();
            var handle = GetHandle();
            if (handle == null) return false;
            AlpmHandle = handle;
            bool success;
            try
            {
                if (handle.TransInit((TransFlag)transFlags) != 0) return false;
                AddIgnorepkgs(handle, ignorepkgs);
                AddOverwriteFiles(handle, overwriteFiles);
                if (sysupgrade)
                {
                    if (handle.TransSysupgrade(enableDowngrade) != 0) return false;
                }
                // add installs
                foreach (string name in toInstall)
                {
                    var pkg = GetSyncpkg(handle, name);
                    if (pkg == null) return false;
                    if (handle.TransAddPkg(pkg) != 0) return false;
                }
                // load local package files
                foreach (string path in toLoadLocal)
                {
                    var pkg = LoadTarball(handle, path);
                    if (pkg == null) return false;
                    if (handle.TransAddPkg(pkg) != 0) return false;
                }
                // load remote urls
                foreach (string url in toLoadRemote)
                {
                    string path = Path.Combine("/var/cache/pamac/", Path.GetFileName(url));
                    var pkg = LoadTarball(handle, path);
                    if (pkg == null) return false;
                    if (handle.TransAddPkg(pkg) != 0) return false;
                }
                // removals
                foreach (string name in toRemove)
                {
                    var pkg = handle.LocalDb.GetPkg(name);
                    if (pkg != null && handle.TransRemovePkg(pkg) != 0) return false;
                }
                if (handle.TransPrepare() != 0)
                {
                    DoEmitError("Alpm Error", new List<string> { AlpmApi.Strerror((Errno)handle.Errno) });
                    return false;
                }
                int commit = handle.TransCommit();
                success = commit == 0;
                handle.TransRelease();
            }
            finally
            {
                RemoveIgnorepkgs(handle, ignorepkgs);
                RemoveOverwriteFiles(handle, overwriteFiles);
                handle.Release();
                AlpmHandle = null;
            }
            return success;
        }

        private static AlpmPkg? LoadTarball(AlpmHandle handle, string path)
        {
            // full=1, siglevel=0 (trust everything for our own downloads)
            IntPtr p;
            IntPtr res = Native_load_tarball(handle._p, path, 1, 0, out p);
            _ = res;
            return AlpmPkg.FromPointer(p);
        }

        [DllImport("libalpm", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr alpm_load_tarball(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int full, int level, out IntPtr pkg);

        private static IntPtr Native_load_tarball(IntPtr h, string p, int full, int level, out IntPtr pkg) => alpm_load_tarball(h, p, full, level, out pkg);

        private static void AddIgnorepkgs(AlpmHandle handle, List<string> ignorepkgs)
        {
            foreach (string n in ignorepkgs) handle.AddIgnorepkg(n);
        }
        private static void RemoveIgnorepkgs(AlpmHandle handle, List<string> ignorepkgs)
        {
            foreach (string n in ignorepkgs) handle.RemoveIgnorepkg(n);
        }
        private static void AddOverwriteFiles(AlpmHandle handle, List<string> overwriteFiles)
        {
            foreach (string f in overwriteFiles) handle.AddOverwriteFile(f);
        }
        private static void RemoveOverwriteFiles(AlpmHandle handle, List<string> overwriteFiles)
        {
            foreach (string f in overwriteFiles) handle.RemoveOverwriteFile(f);
        }

        // ---- emit helpers ----
        public void EmitEvent(uint primaryEvent, uint secondaryEvent, List<string> details)
        {
            switch (primaryEvent)
            {
                case 1: DoEmitAction("Checking dependencies..."); break;
                case 3: CurrentAction = "Checking file conflicts..."; break;
                case 5: DoEmitAction("Resolving dependencies..."); break;
                case 7: DoEmitAction("Checking inter-conflicts..."); break;
                case 11: // PACKAGE_OPERATION_START
                    if (details.Count >= 2)
                    {
                        CurrentFilename = details[0];
                        CurrentAction = secondaryEvent switch
                        {
                            1 => $"Installing {details[0]} ({details[1]})...",
                            2 when details.Count >= 3 => $"Upgrading {details[0]} ({details[1]} -> {details[2]})...",
                            3 => $"Reinstalling {details[0]} ({details[1]})...",
                            4 when details.Count >= 3 => $"Downgrading {details[0]} ({details[1]} -> {details[2]})...",
                            5 => $"Removing {details[0]} ({details[1]})...",
                            _ => CurrentAction,
                        };
                    }
                    break;
                case 13: CurrentAction = "Checking integrity..."; break;
                case 15: CurrentAction = "Loading packages files..."; break;
                case 17: // SCRIPTLET_INFO
                    if (details.Count > 0)
                    {
                        string msg = RemoveBashColors(details[0]).Replace("\n", "");
                        DoEmitScriptOutput(msg);
                        if (CurrentFilename != "")
                        {
                            string action = $"Configuring {CurrentFilename}...";
                            if (action != CurrentAction) CurrentAction = action;
                            if (msg.ToLowerInvariant().Contains("error"))
                            {
                                DoEmitWarning($"Error while configuring {CurrentFilename}");
                                DoImportantDetailsOutput(true);
                            }
                            else DoImportantDetailsOutput(false);
                        }
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
                    _multiProgress.Clear();
                    CurrentProgress = 0;
                    AlreadyDownloaded = 0;
                    TotalDownload = 0;
                    if (primaryEvent == 23) DoEmitWarning("failed to retrieve some files");
                    break;
                case 24: CurrentAction = "Checking available disk space..."; break;
                case 26:
                    if (details.Count >= 2) DoEmitWarning($"Warning: {details[0]} optionally requires {details[1]}");
                    break;
                case 28: CurrentAction = "Checking keyring..."; break;
                case 30: DoEmitAction("Downloading required keys..."); break;
                case 32:
                    if (details.Count > 0) DoEmitScriptOutput($"{details[0]} installed as {details[0]}.pacnew.");
                    break;
                case 33:
                    if (details.Count > 0) DoEmitScriptOutput($"{details[0]} installed as {details[0]}.pacsave.");
                    break;
                case 34: // HOOK_START
                    CurrentAction = secondaryEvent switch
                    {
                        1 => "Running pre-transaction hooks...",
                        2 => "Running post-transaction hooks...",
                        _ => CurrentAction,
                    };
                    if (secondaryEvent == 2) CurrentFilename = "";
                    break;
                case 36: // HOOK_RUN_START
                    if (details.Count >= 4)
                    {
                        double progress = int.Parse(details[2]) / (double)int.Parse(details[3]);
                        string status = $"{details[2]}/{details[3]}";
                        string desc = details[1] != "" ? details[1] : details[0];
                        CurrentProgress = progress;
                        DoEmitHookProgress(CurrentAction, desc, status, progress);
                        if (desc.ToLowerInvariant().Contains("error"))
                        {
                            DoEmitWarning("Error while running hooks");
                            DoImportantDetailsOutput(true);
                        }
                    }
                    break;
            }
        }

        public void EmitProgress(uint progress, string pkgname, uint percent, uint nTargets, uint currentTarget)
        {
            double fraction = progress switch
            {
                >= 0 and <= 4 => ((double)(currentTarget - 1) / nTargets) + ((double)percent / (100 * nTargets)),
                _ => (double)percent / 100,
            };
            string status = $"{currentTarget}/{nTargets}";
            bool changed = fraction != CurrentProgress || status != CurrentStatus;
            CurrentProgress = fraction;
            CurrentStatus = status;
            if (changed && CurrentAction != "")
                DoEmitActionProgress(CurrentAction, CurrentStatus, CurrentProgress);
        }

        public string CurrentStatus = "";
        public ulong AlreadyDownloadedXfer = 0;

        public void EmitDownload(ulong xfered, ulong total)
        {
            if (xfered == 0) return;
            if (total == 0) return;
            double fraction = (double)xfered / total;
            if (fraction > 1) fraction = 1;
            CurrentProgress = fraction;
            string status = $"{FormatSize(xfered)}/{FormatSize(total)}";
            CurrentStatus = status;
            DoEmitDownloadProgress(CurrentAction, CurrentStatus, CurrentProgress);
        }

        public void EmitTotaldownload(ulong total)
        {
            CurrentProgress = 0;
            AlreadyDownloaded = 0;
            CurrentStatus = "";
            TotalDownload = total;
        }

        public void EmitLog(uint level, string msg)
        {
            if (level == 1) // ERROR
            {
                string line = CurrentFilename != ""
                    ? $"Error: {CurrentFilename}: {msg}"
                    : $"Error: {msg}";
                DoEmitWarning(line.Trim());
            }
            else if (level == 2) // WARNING
            {
                DoEmitWarning(msg.Trim());
            }
        }

        internal static string FormatSize(ulong size)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double s = size;
            int u = 0;
            while (s >= 1024 && u < units.Length - 1) { s /= 1024; u++; }
            return u == 0 ? $"{size} {units[u]}" : $"{s:0.#} {units[u]}";
        }

        internal static string RemoveBashColors(string msg)
        {
            return System.Text.RegularExpressions.Regex.Replace(msg, @"\x1B\[[0-9;]*[mK]", "");
        }

        // ---- native callbacks ----
        private static readonly EventCallback EventCb = (data, ctx) =>
        {
            var ev = AlpmEventData.FromPointer(data);
            var utils = FromCtx(ctx);
            if (ev == null || utils == null) return;
            var details = new List<string>();
            uint secondary = 0;
            switch (ev.Type)
            {
                case EventType.HOOK_START:
                    secondary = (uint)ev.HookWhen;
                    break;
                case EventType.HOOK_RUN_START:
                    details.Add(ev.HookRunName ?? "");
                    details.Add(ev.HookRunDesc ?? "");
                    details.Add(ev.HookRunPosition.ToString());
                    details.Add(ev.HookRunTotal.ToString());
                    break;
                case EventType.PACKAGE_OPERATION_START:
                    int op = ev.PackageOperation;
                    var oldpkg = ev.PackageOperationOldpkg;
                    var newpkg = ev.PackageOperationNewpkg;
                    if (op == (int)PackageOperation.REMOVE && oldpkg != null) { details.Add(oldpkg.Name); details.Add(oldpkg.Version); secondary = (uint)PackageOperation.REMOVE; }
                    else if (op == (int)PackageOperation.INSTALL && newpkg != null) { details.Add(newpkg.Name); details.Add(newpkg.Version); secondary = (uint)PackageOperation.INSTALL; }
                    else if (op == (int)PackageOperation.REINSTALL && newpkg != null) { details.Add(newpkg.Name); details.Add(newpkg.Version); secondary = (uint)PackageOperation.REINSTALL; }
                    else if (newpkg != null && oldpkg != null) { details.Add(oldpkg.Name); details.Add(oldpkg.Version); details.Add(newpkg.Version); secondary = (uint)op; }
                    break;
                case EventType.SCRIPTLET_INFO:
                    details.Add(ev.ScriptletInfoLine ?? "");
                    break;
                case EventType.PKG_RETRIEVE_START:
                    utils.EmitTotaldownload((ulong)ev.PkgRetrieveTotalSize);
                    break;
                case EventType.OPTDEP_REMOVAL:
                    if (ev.OptdepRemovalPkg != null && ev.OptdepRemovalOptdep != null)
                    { details.Add(ev.OptdepRemovalPkg.Name); details.Add(ev.OptdepRemovalOptdep.ComputeString()); }
                    break;
                case EventType.DATABASE_MISSING:
                    details.Add(ev.DatabaseMissingDbname ?? "");
                    break;
                case EventType.PACNEW_CREATED:
                    details.Add(ev.PacnewCreatedFile ?? "");
                    break;
                case EventType.PACSAVE_CREATED:
                    details.Add(ev.PacsaveCreatedFile ?? "");
                    break;
            }
            utils.EmitEvent((uint)ev.Type, secondary, details);
        };

        private static readonly ProgressCallback ProgressCb = (progress, pkgname, percent, n, current, ctx) =>
        {
            var utils = FromCtx(ctx);
            if (utils == null) return;
            string name = pkgname == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pkgname)!;
            utils.EmitProgress((uint)progress, name, (uint)percent, (uint)n, (uint)current);
        };

        private static readonly QuestionCallback QuestionCb = (question, ctx) =>
        {
            var q = AlpmQuestionData.FromPointer(question);
            var utils = FromCtx(ctx);
            if (q == null || utils == null) return;
            switch (q.Type)
            {
                case QuestionType.INSTALL_IGNOREPKG:
                    q.InstallIgnorepkgInstall = 0;
                    break;
                case QuestionType.REPLACE_PKG:
                    q.ReplaceReplace = 1;
                    break;
                case QuestionType.CONFLICT_PKG:
                    q.ConflictRemove = 1;
                    break;
                case QuestionType.REMOVE_PKGS:
                    utils.Unresolvables = new List<string>();
                    foreach (IntPtr p in q.RemovePkgsPackages)
                    {
                        var pkg = AlpmPkg.FromPointer(p);
                        if (pkg != null) utils.Unresolvables.Add(pkg.Name);
                    }
                    q.RemovePkgsSkip = 0;
                    break;
                case QuestionType.SELECT_PROVIDER:
                    string dependStr = q.SelectProviderDepend?.ComputeString() ?? "";
                    var providers = new List<string>();
                    foreach (IntPtr p in q.SelectProviderProviders)
                    {
                        var pkg = AlpmPkg.FromPointer(p);
                        if (pkg != null) providers.Add(pkg.Name);
                    }
                    q.SelectProviderUseIndex = utils.DoChooseProvider(dependStr, providers);
                    break;
                case QuestionType.CORRUPTED_PKG:
                    q.CorruptedRemove = 1;
                    break;
                case QuestionType.IMPORT_KEY:
                    q.ImportKeyImport = 1;
                    break;
                default:
                    q.AnyAnswer = 0;
                    break;
            }
        };

        private static readonly DownloadCallback DownloadCb = (filename, eventType, eventData, ctx) =>
        {
            var utils = FromCtx(ctx);
            if (utils == null) return;
            string name = filename == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(filename)!;
            switch ((DownloadEventType)eventType)
            {
                case DownloadEventType.INIT:
                    utils.EmitDownload(0, 0);
                    break;
                case DownloadEventType.PROGRESS:
                    ulong downloaded = (ulong)Marshal.ReadInt64(eventData);
                    ulong total = (ulong)Marshal.ReadInt64(IntPtr.Add(eventData, 8));
                    utils.EmitDownload(downloaded, total);
                    break;
                case DownloadEventType.COMPLETED:
                    ulong totalSize = (ulong)Marshal.ReadInt64(eventData);
                    utils.EmitDownload(totalSize, totalSize);
                    break;
            }
        };

        private static readonly LogCallback LogCb = (level, fmt, args, ctx) =>
        {
            var utils = FromCtx(ctx);
            if (utils == null) return;
            if ((level & ((int)LogLevel.ERROR | (int)LogLevel.WARNING)) == 0) return;
            string msg = fmt == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(fmt)!;
            utils.EmitLog((uint)level, msg.TrimEnd('\n'));
        };
    }
}
