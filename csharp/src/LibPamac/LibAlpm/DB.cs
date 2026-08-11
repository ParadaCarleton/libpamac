using System;
using System.Runtime.InteropServices;

namespace Pamac.LibAlpm
{
    internal static partial class Native
    {
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_db_get_name(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_db_get_pkgcache(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_db_get_groupcache(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_db_get_pkg(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_db_add_server(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_db_set_usage(IntPtr db, int usage);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_db_get_usage(IntPtr db, out int usage);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_db_get_lck(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_db_get_group(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    }

    /// <summary>Wrapper over alpm_db_t.</summary>
    public sealed class AlpmDB
    {
        internal IntPtr _p;

        public AlpmDB(IntPtr p) => _p = p;

        public bool IsNull => _p == IntPtr.Zero;

        public string? Name => Marshal.PtrToStringUTF8(Native.alpm_db_get_name(_p));

        public AlpmList Pkgcache => new AlpmList(Native.alpm_db_get_pkgcache(_p));
        public AlpmList Groupcache => new AlpmList(Native.alpm_db_get_groupcache(_p));

        public AlpmPkg? GetPkg(string name) => AlpmPkg.FromPointer(Native.alpm_db_get_pkg(_p, name));

        public void AddServer(string url) => Native.alpm_db_add_server(_p, url);

        public DBUsage Usage
        {
            get { int u; Native.alpm_db_get_usage(_p, out u); return (DBUsage)u; }
            set => Native.alpm_db_set_usage(_p, (int)value);
        }

        public string? Lck => Marshal.PtrToStringUTF8(Native.alpm_db_get_lck(_p));
    }
}
