using System;
using System.Runtime.InteropServices;

namespace Pamac.LibAlpm
{
    internal static partial class Native
    {
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_filename(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_name(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_version(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_base(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_pkg_get_origin(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_desc(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_url(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern long alpm_pkg_get_builddate(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern long alpm_pkg_get_installdate(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_packager(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_pkg_get_reason(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_pkg_set_reason(IntPtr p, int reason);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong alpm_pkg_get_isize(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern long alpm_pkg_download_size(IntPtr handle, IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_db(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_licenses(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_groups(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_depends(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_optdepends(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_checkdepends(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_makedepends(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_conflicts(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_provides(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_replaces(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_files(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_get_backup(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int alpm_pkg_get_validation(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_compute_requiredby(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_pkg_compute_optionalfor(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_sync_get_new_version(IntPtr p, IntPtr dbs);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_dep_compute_string(IntPtr dep);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_dep_from_string([MarshalAs(UnmanagedType.LPUTF8Str)] string depstring);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void alpm_dep_free(IntPtr dep);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr alpm_dep_get_name(IntPtr dep);
    }

    /// <summary>Wrapper over alpm_pkg_t.</summary>
    public sealed class AlpmPkg
    {
        internal IntPtr _p;

        private AlpmPkg(IntPtr p) => _p = p;

        public static AlpmPkg? FromPointer(IntPtr p) => p == IntPtr.Zero ? null : new AlpmPkg(p);

        public bool IsNull => _p == IntPtr.Zero;

        public string Name => Marshal.PtrToStringUTF8(Native.alpm_pkg_get_name(_p))!;
        public string Version => Marshal.PtrToStringUTF8(Native.alpm_pkg_get_version(_p))!;
        public string? Desc => Marshal.PtrToStringUTF8(Native.alpm_pkg_get_desc(_p));
        public string? Url => Marshal.PtrToStringUTF8(Native.alpm_pkg_get_url(_p));
        public string? Packager => Marshal.PtrToStringUTF8(Native.alpm_pkg_get_packager(_p));
        public long Builddate => Native.alpm_pkg_get_builddate(_p);
        public long Installdate => Native.alpm_pkg_get_installdate(_p);
        public ulong Isize => Native.alpm_pkg_get_isize(_p);
        public ulong DownloadSize(AlpmHandle handle)
        {
            long s = Native.alpm_pkg_download_size(handle?._p ?? IntPtr.Zero, _p);
            return s < 0 ? 0 : (ulong)s;
        }
        public PackageFrom Origin => (PackageFrom)Native.alpm_pkg_get_origin(_p);
        public PackageReason Reason => (PackageReason)Native.alpm_pkg_get_reason(_p);
        public void SetReason(PackageReason r) => Native.alpm_pkg_set_reason(_p, (int)r);
        public PackageValidation Validation => (PackageValidation)Native.alpm_pkg_get_validation(_p);
        public AlpmDB? Db => Native.alpm_pkg_get_db(_p) == IntPtr.Zero ? null : new AlpmDB(Native.alpm_pkg_get_db(_p));

        public AlpmList Licenses => new AlpmList(Native.alpm_pkg_get_licenses(_p));
        public AlpmList Groups => new AlpmList(Native.alpm_pkg_get_groups(_p));
        public AlpmList Depends => new AlpmList(Native.alpm_pkg_get_depends(_p));
        public AlpmList Optdepends => new AlpmList(Native.alpm_pkg_get_optdepends(_p));
        public AlpmList Checkdepends => new AlpmList(Native.alpm_pkg_get_checkdepends(_p));
        public AlpmList Makedepends => new AlpmList(Native.alpm_pkg_get_makedepends(_p));
        public AlpmList Conflicts => new AlpmList(Native.alpm_pkg_get_conflicts(_p));
        public AlpmList Provides => new AlpmList(Native.alpm_pkg_get_provides(_p));
        public AlpmList Replaces => new AlpmList(Native.alpm_pkg_get_replaces(_p));
        public AlpmList Backups => new AlpmList(Native.alpm_pkg_get_backup(_p));
        public AlpmList FilesList => new AlpmList(Native.alpm_pkg_get_files(_p));

        public List<string> ComputeRequiredby() => ReadStringList(Native.alpm_pkg_compute_requiredby(_p));
        public List<string> ComputeOptionalfor() => ReadStringList(Native.alpm_pkg_compute_optionalfor(_p));

        public static List<string> ReadStringList(IntPtr list)
        {
            var result = new List<string>();
            var iter = new AlpmList(list);
            foreach (IntPtr s in iter)
            {
                if (s != IntPtr.Zero)
                    result.Add(Marshal.PtrToStringUTF8(s)!);
            }
            return result;
        }

        public static AlpmPkg? FindSatisfier(AlpmList pkgs, string depstring) =>
            FromPointer(Native.alpm_find_satisfier(pkgs.Head, depstring));

        public static int VerCmp(string a, string b) => Native.alpm_pkg_vercmp(a, b);

        public AlpmPkg? GetNewVersion(AlpmList dbs) =>
            FromPointer(Native.alpm_sync_get_new_version(_p, dbs.Head));

        [StructLayout(LayoutKind.Sequential)]
        private struct AlpmFile
        {
            public IntPtr name;
            public uint mode;
            public long size;
            public long modified;
        }

        /// <summary>Iterate the package file list (alpm_filelist_t) and return file names.</summary>
        public List<string> GetFileNames()
        {
            var result = new List<string>();
            IntPtr fl = Native.alpm_pkg_get_files(_p);
            if (fl == IntPtr.Zero) return result;
            // alpm_filelist_t { size_t count; alpm_file_t *files; }
            long count = Marshal.ReadInt64(fl);
            IntPtr filesPtr = Marshal.ReadIntPtr(fl, IntPtr.Size);
            if (filesPtr == IntPtr.Zero || count <= 0) return result;
            int elemSize = Marshal.SizeOf<AlpmFile>();
            for (long i = 0; i < count; i++)
            {
                IntPtr file = IntPtr.Add(filesPtr, (int)(i * elemSize));
                IntPtr namePtr = Marshal.ReadIntPtr(file);
                if (namePtr != IntPtr.Zero)
                    result.Add(Marshal.PtrToStringUTF8(namePtr)!);
            }
            return result;
        }
    }

    /// <summary>Wrapper over alpm_depend_t (borrowed from a package's lists).</summary>
    public sealed class AlpmDepend
    {
        internal IntPtr _p;

        private AlpmDepend(IntPtr p) => _p = p;

        public static AlpmDepend FromPointer(IntPtr p) => p == IntPtr.Zero ? null! : new AlpmDepend(p);

        public string? Name => Marshal.PtrToStringUTF8(Native.alpm_dep_get_name(_p));

        public string ComputeString()
        {
            IntPtr s = Native.alpm_dep_compute_string(_p);
            string r = Marshal.PtrToStringUTF8(s)!;
            return r;
        }

        public static AlpmDepend FromString(string depstring)
        {
            var dep = new AlpmDepend(Native.alpm_dep_from_string(depstring));
            return dep;
        }
    }
}
