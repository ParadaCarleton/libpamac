using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// P/Invoke bindings for libalpm (alpm.h), mirroring vapi/libalpm.vapi.
//
// libalpm objects are owned by the library and are represented here as thin
// IntPtr wrappers. Strings returned from libalpm are UTF-8 `const char*` and
// are borrowed (owned by alpm), so we convert them on the fly and never free.
namespace Pamac.LibAlpm
{
    public enum Errno
    {
        OK = 0, MEMORY, SYSTEM, BADPERMS, NOT_A_FILE, NOT_A_DIR, WRONG_ARGS, DISK_SPACE,
        HANDLE_NULL, HANDLE_NOT_NULL, HANDLE_LOCK,
        DB_OPEN, DB_CREATE, DB_NULL, DB_NOT_NULL, DB_NOT_FOUND, DB_INVALID, DB_INVALID_SIG,
        DB_VERSION, DB_WRITE, DB_REMOVE,
        SERVER_BAD_URL, SERVER_NONE,
        TRANS_NOT_NULL, TRANS_NULL, TRANS_DUP_TARGET, TRANS_NOT_INITIALIZED, TRANS_NOT_PREPARED,
        TRANS_ABORT, TRANS_TYPE, TRANS_NOT_LOCKED, TRANS_HOOK_FAILED,
        PKG_NOT_FOUND, PKG_IGNORED, PKG_INVALID, PKG_INVALID_CHECKSUM, PKG_INVALID_SIG,
        PKG_MISSING_SIG, PKG_OPEN, PKG_CANT_REMOVE, PKG_INVALID_NAME, PKG_INVALID_ARCH,
        PKG_REPO_NOT_FOUND,
        SIG_MISSING, SIG_INVALID,
        UNSATISFIED_DEPS, CONFLICTING_DEPS, FILE_CONFLICTS,
        RETRIEVE, INVALID_REGEX,
        LIBARCHIVE, LIBCURL, EXTERNAL_DOWNLOAD, GPGME,
        MISSING_CAPABILITY_SIGNATURES
    }

    [Flags]
    public enum TransFlag
    {
        NONE = 0,
        NODEPS = 1,
        NOSAVE = (1 << 2),
        NODEPVERSION = (1 << 3),
        CASCADE = (1 << 4),
        RECURSE = (1 << 5),
        DBONLY = (1 << 6),
        ALLDEPS = (1 << 8),
        DOWNLOADONLY = (1 << 9),
        NOSCRIPTLET = (1 << 10),
        NOCONFLICTS = (1 << 11),
        NEEDED = (1 << 13),
        ALLEXPLICIT = (1 << 14),
        UNNEEDED = (1 << 15),
        RECURSEALL = (1 << 16),
        NOLOCK = (1 << 17)
    }

    [Flags]
    public enum SignatureLevel
    {
        NONE = 0,
        PACKAGE = (1 << 0),
        PACKAGE_OPTIONAL = (1 << 1),
        PACKAGE_MARGINAL_OK = (1 << 2),
        PACKAGE_UNKNOWN_OK = (1 << 3),
        DATABASE = (1 << 10),
        DATABASE_OPTIONAL = (1 << 11),
        DATABASE_MARGINAL_OK = (1 << 12),
        DATABASE_UNKNOWN_OK = (1 << 13),
        USE_DEFAULT = (1 << 14)
    }

    public enum DBUsage
    {
        NONE = 0,
        SYNC = (1 << 0),
        SEARCH = (1 << 1),
        INSTALL = (1 << 2),
        UPGRADE = (1 << 3),
        ALL = (1 << 4)
    }

    public enum PackageFrom
    {
        ANY = 1,
        FILE = 2,
        LOCALDB = 3,
        SYNCDB = 4
    }

    public enum PackageReason
    {
        EXPLICIT = 0,
        DEPEND = 1
    }

    [Flags]
    public enum PackageValidation
    {
        NONE = 0,
        MD5SUM = (1 << 0),
        SHA256SUM = (1 << 1),
        SIGNATURE = (1 << 2)
    }

    public enum PackageOperation
    {
        INSTALL = 1,
        UPGRADE = 2,
        DOWNGRADE = 4,
        REINSTALL = 8,
        REMOVE = 16
    }

    public enum LogLevel
    {
        ERROR = 1,
        WARNING = (1 << 1),
        DEBUG = (1 << 2),
        FUNCTION = (1 << 3)
    }

    public enum Progress
    {
        ADD_START, UPGRADE_START, DOWNGRADE_START, REINSTALL_START, REMOVE_START,
        CONFLICTS_START, DISKSPACE_START, INTEGRITY_START, LOAD_START, KEYRING_START
    }

    public enum HookWhen
    {
        PRE_TRANSACTION = 0,
        POST_TRANSACTION = 1
    }

    // ---- Callback types ----
    public enum EventType
    {
        DATABASE_MISSING = 1,
        PKGDOWNLOAD_START = 4,
        PKGDOWNLOAD_DONE = 5,
        OPTDEP_REMOVAL = 10,
        SCRIPTLET_INFO = 12,
        DATABASE_UPGRADED = 32,
        TRANS_START = 16,
        TRANS_DONE = 17,
        PKG_RETRIEVE_START = 22,
        PACNEW_CREATED = 23,
        PACSAVE_CREATED = 24,
        HOOK_START = 33,
        HOOK_DONE = 34,
        HOOK_RUN_START = 35,
        HOOK_RUN_DONE = 36,
        PACKAGE_OPERATION_START = 40,
        PACKAGE_OPERATION_DONE = 41,
        PACKAGE_OPERATION_ABORT = 42
    }

    public enum QuestionType
    {
        INSTALL_IGNOREPKG = 1,
        REPLACE_PKG = 2,
        CONFLICT_PKG = 3,
        CORRUPTED_PKG = 4,
        REMOVE_PKGS = 5,
        SELECT_PROVIDER = 6,
        IMPORT_KEY = 7
    }

    public enum DownloadEventType
    {
        INIT = 0,
        PROGRESS = 1,
        COMPLETED = 2
    }

    // Signatures match libalpm alpm.h: the user context pointer is the LAST
    // argument in every callback.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void EventCallback(IntPtr eventData, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void QuestionCallback(IntPtr question, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProgressCallback(int progress, IntPtr pkgname, int percent, ulong n_targets, ulong current_target, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DownloadCallback(IntPtr filename, int event_type, IntPtr event_data, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallback(int level, IntPtr fmt, IntPtr args, IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int FetchCallback(IntPtr url, IntPtr localpath, int force, IntPtr ctx);

    /// <summary>
    /// Raw DllImport declarations. The wrapper types in this namespace are
    /// declared in the individual files under LibAlpm/.
    /// </summary>
    internal static partial class Native
    {
        internal const string Lib = "libalpm";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int alpm_capabilities();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_strerror(int err);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_initialize(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string root,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string dbpath,
            out int err);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int alpm_release(IntPtr handle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int alpm_pkg_vercmp(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string a,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string b);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_strerror_via_errno();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_find_satisfier(IntPtr pkgs,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string depstring);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_pkg_find(IntPtr haystack,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string needle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_find_group_pkgs(IntPtr dbs,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_list_next(IntPtr list);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_list_getdata(IntPtr list);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint alpm_list_count(IntPtr list);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr alpm_list_add(IntPtr list, IntPtr data);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void alpm_list_free(IntPtr list);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void alpm_list_free_inner(IntPtr list, IntPtr freefn);

        // Option getters/setters are declared per-wrapper in Handle.cs.
    }

    /// <summary>Global libalpm entry points (equivalent of the top-level Alpm namespace functions).</summary>
    public static class AlpmApi
    {
        public static string Version()
        {
            try { return Marshal.PtrToStringUTF8(Native.alpm_version())!; }
            catch (DllNotFoundException) { return "unknown"; }
        }
        public static int Capabilities()
        {
            try { return Native.alpm_capabilities(); }
            catch (DllNotFoundException) { return 0; }
        }
        public static string Strerror(Errno err)
        {
            try { return Marshal.PtrToStringUTF8(Native.alpm_strerror((int)err))!; }
            catch (DllNotFoundException) { return $"alpm error {(int)err}"; }
        }
    }

    /// <summary>Helper to iterate an alpm_list_t linked list of raw pointers.</summary>
    public sealed class AlpmList : IEnumerable<IntPtr>, IDisposable
    {
        private IntPtr _head;

        public AlpmList(IntPtr head)
        {
            _head = head;
        }

        public IntPtr Head => _head;
        public bool IsEmpty => _head == IntPtr.Zero;
        public int Count => (int)Native.alpm_list_count(_head);

        public IEnumerator<IntPtr> GetEnumerator()
        {
            IntPtr node = _head;
            while (node != IntPtr.Zero)
            {
                IntPtr data = Native.alpm_list_getdata(node);
                yield return data;
                node = Native.alpm_list_next(node);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            // Only free when this list is owned (produced by a *_get or allocation).
            Free();
            GC.SuppressFinalize(this);
        }

        public void Free() => Native.alpm_list_free(_head);
    }
}
