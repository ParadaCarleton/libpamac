using System;
using System.Runtime.InteropServices;

namespace Pamac.LibAlpm
{
    internal static partial class Native
    {
        // ---- Handle options ----
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_root(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_dbpath(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_lockfile(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_logfile(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_logfile(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_gpgdir(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_gpgdir(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_sandboxuser(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_sandboxuser(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_disable_sandbox(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_disable_sandbox(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_disable_sandbox_filesystem(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_disable_sandbox_filesystem(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_disable_sandbox_syscalls(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_disable_sandbox_syscalls(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_usesyslog(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_usesyslog(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_checkspace(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_checkspace(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_dbext(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_dbext(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern uint alpm_option_get_parallel_downloads(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_parallel_downloads(IntPtr h, uint v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_default_siglevel(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_default_siglevel(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_local_file_siglevel(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_local_file_siglevel(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_get_remote_file_siglevel(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_remote_file_siglevel(IntPtr h, int v);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_download_timeout(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_download_timeout(IntPtr h, long v);

        // ---- list options (architectures, cachedirs, hookdirs, ...) ----
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_architectures(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_architecture(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_architecture(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_cachedirs(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_cachedir(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_cachedir(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_hookdirs(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_hookdir(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_hookdir(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_overwrite_files(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_overwrite_file(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_overwrite_file(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_noupgrades(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_noupgrade(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_noupgrade(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_noextracts(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_noextract(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_noextract(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_match_noextract(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_ignorepkgs(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_ignorepkg(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_ignorepkg(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_option_get_ignoregroups(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_add_ignoregroup(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_remove_ignoregroup(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);

        // ---- callbacks ----
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_logcb(IntPtr h, LogCallback cb, IntPtr ctx);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_dlcb(IntPtr h, DownloadCallback cb, IntPtr ctx);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_fetchcb(IntPtr h, FetchCallback cb, IntPtr ctx);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_eventcb(IntPtr h, EventCallback cb, IntPtr ctx);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_questioncb(IntPtr h, QuestionCallback cb, IntPtr ctx);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_option_set_progresscb(IntPtr h, ProgressCallback cb, IntPtr ctx);

        // ---- databases ----
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_get_localdb(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_get_syncdbs(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_register_syncdb(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int level);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_unregister_all_syncdbs(IntPtr h);

        // ---- transactions ----
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_init(IntPtr h, int flags, EventCallback cb_evt, QuestionCallback cb_conv, ProgressCallback cb_progress);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_prepare(IntPtr h, out IntPtr data);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_commit(IntPtr h, out IntPtr data);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_interrupt(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_cancel(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_release(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_get_flags(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_add_pkg(IntPtr h, IntPtr pkg);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_remove_pkg(IntPtr h, IntPtr pkg);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_trans_get_add(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_trans_get_remove(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_trans_sysupgrade(IntPtr h, int enable_downgrade);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_sync_sysupgrade(IntPtr h, int enable_downgrade);

        // ---- misc ----
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_db_update(IntPtr h, IntPtr dbs, int force);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_should_ignore(IntPtr h, IntPtr pkg);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_handle_get_errno(IntPtr h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_fetch_pkgurl(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_load_tarball(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int full, IntPtr level, out IntPtr pkg);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_find_dbs_satisfier(IntPtr h, IntPtr dbs, [MarshalAs(UnmanagedType.LPUTF8Str)] string depstring);
    }

    /// <summary>A handle to the alpm library (a thin wrapper over alpm_handle_t).</summary>
    public sealed class AlpmHandle : IDisposable
    {
        internal IntPtr _p;

        public AlpmHandle(IntPtr p) => _p = p;

        public static AlpmHandle Initialize(string root, string dbpath, out Errno err)
        {
            int e;
            IntPtr h;
            try
            {
                h = Native.alpm_initialize(root, dbpath, out e);
            }
            catch (DllNotFoundException)
            {
                // libalpm is not installed on this system; mirror the original's
                // graceful "failed to initialize" path rather than crashing.
                err = Pamac.LibAlpm.Errno.HANDLE_NULL;
                return null;
            }
            err = (Errno)e;
            return h == IntPtr.Zero ? null! : new AlpmHandle(h);
        }

        public bool IsNull => _p == IntPtr.Zero;

        public void Release()
        {
            if (_p != IntPtr.Zero)
            {
                Native.alpm_release(_p);
                _p = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        public string Root => Marshal.PtrToStringUTF8(Native.alpm_option_get_root(_p))!;
        public string Dbpath => Marshal.PtrToStringUTF8(Native.alpm_option_get_dbpath(_p))!;
        public string Lockfile => Marshal.PtrToStringUTF8(Native.alpm_option_get_lockfile(_p))!;

        public string? Logfile
        {
            get => Marshal.PtrToStringUTF8(Native.alpm_option_get_logfile(_p));
            set => Native.alpm_option_set_logfile(_p, value);
        }
        public string? Gpgdir
        {
            get => Marshal.PtrToStringUTF8(Native.alpm_option_get_gpgdir(_p));
            set => Native.alpm_option_set_gpgdir(_p, value);
        }
        public string? Sandboxuser
        {
            get => Marshal.PtrToStringUTF8(Native.alpm_option_get_sandboxuser(_p));
            set => Native.alpm_option_set_sandboxuser(_p, value);
        }
        public int DisableSandbox
        {
            get => Native.alpm_option_get_disable_sandbox(_p);
            set => Native.alpm_option_set_disable_sandbox(_p, value);
        }
        public int DisableSandboxFilesystem
        {
            get => Native.alpm_option_get_disable_sandbox_filesystem(_p);
            set => Native.alpm_option_set_disable_sandbox_filesystem(_p, value);
        }
        public int DisableSandboxSyscalls
        {
            get => Native.alpm_option_get_disable_sandbox_syscalls(_p);
            set => Native.alpm_option_set_disable_sandbox_syscalls(_p, value);
        }
        public int Usesyslog
        {
            get => Native.alpm_option_get_usesyslog(_p);
            set => Native.alpm_option_set_usesyslog(_p, value);
        }
        public int Checkspace
        {
            get => Native.alpm_option_get_checkspace(_p);
            set => Native.alpm_option_set_checkspace(_p, value);
        }
        public string? Dbext
        {
            get => Marshal.PtrToStringUTF8(Native.alpm_option_get_dbext(_p));
            set => Native.alpm_option_set_dbext(_p, value);
        }
        public uint ParallelDownloads
        {
            get => Native.alpm_option_get_parallel_downloads(_p);
            set => Native.alpm_option_set_parallel_downloads(_p, value);
        }
        public int Defaultsiglevel
        {
            get => Native.alpm_option_get_default_siglevel(_p);
            set => Native.alpm_option_set_default_siglevel(_p, value);
        }
        public int Localfilesiglevel
        {
            get => Native.alpm_option_get_local_file_siglevel(_p);
            set => Native.alpm_option_set_local_file_siglevel(_p, value);
        }
        public int Remotefilesiglevel
        {
            get => Native.alpm_option_get_remote_file_siglevel(_p);
            set => Native.alpm_option_set_remote_file_siglevel(_p, value);
        }

        public void AddArchitecture(string s) => Native.alpm_option_add_architecture(_p, s);
        public void AddCachedir(string s) => Native.alpm_option_add_cachedir(_p, s);
        public void AddHookdir(string s) => Native.alpm_option_add_hookdir(_p, s);
        public void AddOverwriteFile(string s) => Native.alpm_option_add_overwrite_file(_p, s);
        public void RemoveOverwriteFile(string s) => Native.alpm_option_remove_overwrite_file(_p, s);
        public void AddNoupgrade(string s) => Native.alpm_option_add_noupgrade(_p, s);
        public void AddNoextract(string s) => Native.alpm_option_add_noextract(_p, s);
        public void AddIgnorepkg(string s) => Native.alpm_option_add_ignorepkg(_p, s);
        public void RemoveIgnorepkg(string s) => Native.alpm_option_remove_ignorepkg(_p, s);
        public void AddIgnoregroup(string s) => Native.alpm_option_add_ignoregroup(_p, s);

        public void SetEventcb(EventCallback cb, IntPtr ctx) => Native.alpm_option_set_eventcb(_p, cb, ctx);
        public void SetProgresscb(ProgressCallback cb, IntPtr ctx) => Native.alpm_option_set_progresscb(_p, cb, ctx);
        public void SetQuestioncb(QuestionCallback cb, IntPtr ctx) => Native.alpm_option_set_questioncb(_p, cb, ctx);
        public void SetDlcb(DownloadCallback cb, IntPtr ctx) => Native.alpm_option_set_dlcb(_p, cb, ctx);
        public void SetLogcb(LogCallback cb, IntPtr ctx) => Native.alpm_option_set_logcb(_p, cb, ctx);

        public AlpmDB LocalDb => new AlpmDB(Native.alpm_get_localdb(_p));
        public AlpmList Syncdbs => new AlpmList(Native.alpm_get_syncdbs(_p));
        public AlpmDB RegisterSyncdb(string name, SignatureLevel level) => new AlpmDB(Native.alpm_register_syncdb(_p, name, (int)level));

        public int Errno => Native.alpm_handle_get_errno(_p);
        public int ShouldIgnore(AlpmPkg pkg) => Native.alpm_should_ignore(_p, pkg._p);

        // Transactions
        public int TransInit(TransFlag flags) =>
            Native.alpm_trans_init(_p, (int)flags, null, null, null);
        public int TransPrepare() { IntPtr data; return Native.alpm_trans_prepare(_p, out data); }
        public int TransCommit() { IntPtr data; return Native.alpm_trans_commit(_p, out data); }
        public int TransInterrupt() => Native.alpm_trans_interrupt(_p);
        public int TransCancel() => Native.alpm_trans_cancel(_p);
        public int TransRelease() => Native.alpm_trans_release(_p);
        public int TransAddPkg(AlpmPkg pkg) => Native.alpm_trans_add_pkg(_p, pkg._p);
        public int TransRemovePkg(AlpmPkg pkg) => Native.alpm_trans_remove_pkg(_p, pkg._p);
        public int TransSysupgrade(bool enable_downgrade) => Native.alpm_trans_sysupgrade(_p, enable_downgrade ? 1 : 0);
        public AlpmList TransToAdd() => new AlpmList(Native.alpm_trans_get_add(_p));
        public AlpmList TransToRemove() => new AlpmList(Native.alpm_trans_get_remove(_p));

        public int UpdateDbs(AlpmList dbs, bool force) => Native.alpm_db_update(_p, dbs.Head, force ? 1 : 0);
        public void Unlock() => Native.alpm_release(_p); // no-op keepalive placeholder; real unlock via handle
        public AlpmPkg? FindDbsSatisfier(string depstring) =>
            AlpmPkg.FromPointer(Native.alpm_find_dbs_satisfier(_p, _syncdbsOrLocal, depstring));

        private IntPtr _syncdbsOrLocal = IntPtr.Zero;
        public AlpmHandle ForDbsSatisfier(IntPtr dbs) { _syncdbsOrLocal = dbs; return this; }
    }
}
