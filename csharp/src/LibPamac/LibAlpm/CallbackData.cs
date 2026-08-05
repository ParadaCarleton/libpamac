using System;
using System.Runtime.InteropServices;

// Union parsing for the alpm_event_t / alpm_question_t payloads passed to the
// event / question callbacks. Each member struct begins with its own `type`
// field and is laid out identically to the C definitions in alpm.h; the union
// sits right after the leading `type` int, so we PtrToStructure on base+4.
namespace Pamac.LibAlpm
{
    // ---- Event member structs (alpm_event_t union) ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvPkgOperation { public int type; public int operation; public IntPtr oldpkg; public IntPtr newpkg; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvScriptletInfo { public int type; public IntPtr line; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvDatabaseMissing { public int type; public IntPtr dbname; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvHook { public int type; public int when; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvHookRun { public int type; public int when; public IntPtr name; public IntPtr desc; public ulong position; public ulong total; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvOptdepRemoval { public int type; public IntPtr pkg; public IntPtr optdep; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvPkgRetrieve { public int type; public long total_size; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvPacnewCreated { public int type; public IntPtr file; public int from_noupgrade; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvPacsaveCreated { public int type; public IntPtr file; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct EvDatabaseUpgraded { public int type; public IntPtr dbname; public IntPtr from; public IntPtr to; }

    /// <summary>Read-only view of an alpm_event_t payload.</summary>
    public sealed class AlpmEventData
    {
        private readonly IntPtr _p;
        private readonly int _type;

        private AlpmEventData(IntPtr p, int type) { _p = p; _type = type; }

        public static AlpmEventData? FromPointer(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int type = Marshal.ReadInt32(p);
            return new AlpmEventData(p, type);
        }

        public EventType Type => (EventType)_type;
        private IntPtr Union => IntPtr.Add(_p, 4);

        public int HookWhen => Marshal.PtrToStructure<EvHook>(Union).when;
        public string? HookRunName => Marshal.PtrToStructure<EvHookRun>(Union).name.PtrToString();
        public string? HookRunDesc => Marshal.PtrToStructure<EvHookRun>(Union).desc.PtrToString();
        public ulong HookRunPosition => Marshal.PtrToStructure<EvHookRun>(Union).position;
        public ulong HookRunTotal => Marshal.PtrToStructure<EvHookRun>(Union).total;

        public int PackageOperation => Marshal.PtrToStructure<EvPkgOperation>(Union).operation;
        public AlpmPkg? PackageOperationOldpkg => AlpmPkg.FromPointer(Marshal.PtrToStructure<EvPkgOperation>(Union).oldpkg);
        public AlpmPkg? PackageOperationNewpkg => AlpmPkg.FromPointer(Marshal.PtrToStructure<EvPkgOperation>(Union).newpkg);

        public string? ScriptletInfoLine => Marshal.PtrToStructure<EvScriptletInfo>(Union).line.PtrToString();
        public string? DatabaseMissingDbname => Marshal.PtrToStructure<EvDatabaseMissing>(Union).dbname.PtrToString();
        public long PkgRetrieveTotalSize => Marshal.PtrToStructure<EvPkgRetrieve>(Union).total_size;
        public AlpmPkg? OptdepRemovalPkg => AlpmPkg.FromPointer(Marshal.PtrToStructure<EvOptdepRemoval>(Union).pkg);
        public AlpmDepend? OptdepRemovalOptdep => AlpmDepend.FromPointer(Marshal.PtrToStructure<EvOptdepRemoval>(Union).optdep);
        public string? PacnewCreatedFile => Marshal.PtrToStructure<EvPacnewCreated>(Union).file.PtrToString();
        public string? PacsaveCreatedFile => Marshal.PtrToStructure<EvPacsaveCreated>(Union).file.PtrToString();
    }

    // ---- Question member structs (alpm_question_t union) ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct QAny { public int type; public int answer; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QInstallIgnorepkg { public int type; public int install; public IntPtr pkg; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QReplace { public int type; public int replace; public IntPtr oldpkg; public IntPtr newpkg; public IntPtr newdb; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QConflict { public int type; public int remove; public IntPtr dep; public IntPtr file; public IntPtr package; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QCorrupted { public int type; public int remove; public IntPtr filepath; public int reason; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QRemovePkgs { public int type; public int skip; public IntPtr packages; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QSelectProvider { public int type; public int use_index; public IntPtr providers; public IntPtr depend; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct QImportKey { public int type; public int import; public long key; }

    /// <summary>Mutable view of an alpm_question_t payload, used to answer questions.</summary>
    public sealed class AlpmQuestionData
    {
        private readonly IntPtr _p;
        private readonly int _type;

        private AlpmQuestionData(IntPtr p, int type) { _p = p; _type = type; }

        public static AlpmQuestionData? FromPointer(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            return new AlpmQuestionData(p, Marshal.ReadInt32(p));
        }

        public QuestionType Type => (QuestionType)_type;
        private IntPtr Union => IntPtr.Add(_p, 4);

        public int AnyAnswer
        {
            get => Marshal.PtrToStructure<QAny>(Union).answer;
            set { var q = Marshal.PtrToStructure<QAny>(Union); q.answer = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public int InstallIgnorepkgInstall
        {
            set { var q = Marshal.PtrToStructure<QInstallIgnorepkg>(Union); q.install = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public int ReplaceReplace
        {
            set { var q = Marshal.PtrToStructure<QReplace>(Union); q.replace = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public int ConflictRemove
        {
            set { var q = Marshal.PtrToStructure<QConflict>(Union); q.remove = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public int CorruptedRemove
        {
            set { var q = Marshal.PtrToStructure<QCorrupted>(Union); q.remove = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public int RemovePkgsSkip
        {
            get => Marshal.PtrToStructure<QRemovePkgs>(Union).skip;
            set { var q = Marshal.PtrToStructure<QRemovePkgs>(Union); q.skip = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public AlpmList RemovePkgsPackages => new AlpmList(Marshal.PtrToStructure<QRemovePkgs>(Union).packages);
        public AlpmList SelectProviderProviders => new AlpmList(Marshal.PtrToStructure<QSelectProvider>(Union).providers);
        public AlpmDepend? SelectProviderDepend => AlpmDepend.FromPointer(Marshal.PtrToStructure<QSelectProvider>(Union).depend);
        public int SelectProviderUseIndex
        {
            get => Marshal.PtrToStructure<QSelectProvider>(Union).use_index;
            set { var q = Marshal.PtrToStructure<QSelectProvider>(Union); q.use_index = value; Marshal.StructureToPtr(q, Union, false); }
        }
        public int ImportKeyImport
        {
            set { var q = Marshal.PtrToStructure<QImportKey>(Union); q.import = value; Marshal.StructureToPtr(q, Union, false); }
        }
    }

    internal static class PtrExt
    {
        public static string? PtrToString(this IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
    }
}
