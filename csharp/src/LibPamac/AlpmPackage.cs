using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pamac.LibAlpm;

namespace Pamac
{
    /// <summary>Base class for packages backed by libalpm (equivalent of AlpmPackage in alpm_package.vala).</summary>
    public abstract class AlpmPackage : Package
    {
        public abstract DateTimeOffset? BuildDate { get; }
        public abstract string? Packager { get; }
        public abstract string? Reason { get; }
        public abstract List<string> Validations { get; }
        public abstract List<string> Groups { get; }
        public abstract List<string> Depends { get; internal set; }
        public abstract List<string> Optdepends { get; }
        public abstract List<string> Makedepends { get; }
        public abstract List<string> Checkdepends { get; }
        public abstract List<string> Requiredby { get; }
        public abstract List<string> Optionalfor { get; }
        public abstract List<string> Provides { get; internal set; }
        public abstract List<string> Replaces { get; internal set; }
        public abstract List<string> Conflicts { get; internal set; }
        public abstract List<string> Backups { get; }

        internal AlpmPackage() { }

        public abstract List<string> GetFiles();
        public abstract Task<List<string>> GetFilesAsync();
    }

    /// <summary>Lazily-resolved alpm package (equivalent of AlpmPackageLinked).</summary>
    internal class AlpmPackageLinked : AlpmPackage
    {
        // Package
        protected string _name = null!;
        protected string _id = null!;
        protected string? _version;
        protected string? _installedVersion;
        protected string? _desc;
        protected string? _repo;
        protected string? _license;
        protected string? _url;
        protected ulong _installedSize;
        protected ulong _downloadSize;
        protected DateTimeOffset? _installDate;
        // App
        protected App? _app;
        protected string? _appName;
        protected string? _appId;
        protected string? _longDesc;
        protected string? _launchable;
        protected string? _icon;
        protected List<string> _screenshots = null!;
        // AlpmPackage
        protected Database database = null!;
        protected AlpmPkg? _alpmPkg;
        protected AlpmPkg? _localPkg;
        protected AlpmPkg? _syncPkg;
        protected bool _localPkgSet;
        protected bool _syncPkgSet;
        protected bool _versionSet;
        protected bool _installedVersionSet;
        protected bool _repoSet;
        protected bool _licenseSet;
        protected bool _installDateSet;
        protected bool _downloadSizeSet;
        protected bool _installedSizeSet;
        protected bool _reasonSet;
        protected DateTimeOffset? _buildDate;
        protected string? _packager;
        protected string? _reason;
        protected List<string> _validations = null!;
        protected List<string> _groups = null!;
        protected List<string> _depends = null!;
        protected List<string> _optdepends = null!;
        protected List<string> _makedepends = null!;
        protected List<string> _checkdepends = null!;
        protected List<string> _requiredby = null!;
        protected List<string> _optionalfor = null!;
        protected List<string> _provides = null!;
        protected List<string> _replaces = null!;
        protected List<string> _conflicts = null!;
        protected List<string> _backups = null!;
        protected List<string> _files = null!;

        internal AlpmPackageLinked() { }

        internal AlpmPackageLinked(AlpmPkg? alpmPkg, Database database)
        {
            _alpmPkg = alpmPkg;
            this.database = database;
        }

        internal AlpmPkg? AlpmPkg => _alpmPkg;

        // ---- Package ----
        public override string Name
        {
            get
            {
                if (_name == null) _name = _alpmPkg!.Name;
                return _name;
            }
            internal set => _name = value;
        }
        public override string Id
        {
            get
            {
                if (_id == null) _id = _app != null ? AppId! : Name;
                return _id;
            }
            internal set => _id = value;
        }
        public override string Version
        {
            get
            {
                if (!_versionSet)
                {
                    _versionSet = true;
                    FoundSyncPkg();
                    _version = _syncPkg != null ? _syncPkg.Version : _alpmPkg!.Version;
                }
                return _version!;
            }
            internal set => _version = value;
        }
        public override string? InstalledVersion
        {
            get
            {
                if (!_installedVersionSet)
                {
                    _installedVersionSet = true;
                    FoundLocalPkg();
                    if (_localPkg != null) _installedVersion = _localPkg.Version;
                }
                return _installedVersion;
            }
            internal set => _installedVersion = value;
        }
        public override string? Desc
        {
            get
            {
                if (_desc == null)
                {
                    if (_app != null)
                    {
                        string? summary = _app.Desc;
                        _desc = summary ?? _alpmPkg!.Desc;
                    }
                    else
                    {
                        _desc = _alpmPkg!.Desc;
                    }
                }
                return _desc;
            }
            internal set => _desc = value;
        }
        public override string? Repo
        {
            get
            {
                if (!_repoSet)
                {
                    _repoSet = true;
                    FoundSyncPkg();
                    var db = _syncPkg?.Db;
                    if (db != null) _repo = db.Name;
                }
                return _repo;
            }
            internal set => _repo = value;
        }
        public override string? License
        {
            get
            {
                if (_license == null && !_licenseSet)
                {
                    _licenseSet = true;
                    var list = _alpmPkg!.Licenses;
                    if (!list.IsEmpty)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (IntPtr s in list)
                        {
                            if (s == IntPtr.Zero) continue;
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(s));
                        }
                        _license = sb.ToString();
                    }
                    else
                    {
                        _license = "Unknown";
                    }
                }
                return _license;
            }
        }
        public override string? Url
        {
            get
            {
                if (_url == null) _url = _alpmPkg!.Url;
                return _url;
            }
        }
        public override ulong InstalledSize
        {
            get
            {
                if (_installedSize == 0 && !_installedSizeSet)
                {
                    _installedSizeSet = true;
                    _installedSize = _alpmPkg!.Isize;
                }
                return _installedSize;
            }
        }
        public override ulong DownloadSize
        {
            get
            {
                if (!_downloadSizeSet)
                {
                    _downloadSizeSet = true;
                    _downloadSize = _alpmPkg!.DownloadSize(database.GetHandleForDownload());
                }
                return _downloadSize;
            }
        }
        public override DateTimeOffset? InstallDate
        {
            get
            {
                if (!_installDateSet)
                {
                    _installDateSet = true;
                    FoundLocalPkg();
                    if (_localPkg != null)
                        _installDate = DateTimeOffset.FromUnixTimeSeconds(_localPkg.Installdate);
                }
                return _installDate;
            }
        }
        // App
        public override string? AppName
        {
            get
            {
                if (_appName == null && _app != null) _appName = _app.Name;
                return _appName;
            }
        }
        public override string? AppId
        {
            get
            {
                if (_appId == null && _app != null) _appId = _app.Id;
                return _appId;
            }
        }
        public override string? LongDesc
        {
            get
            {
                if (_longDesc == null && _app != null) _longDesc = _app.LongDesc;
                return _longDesc;
            }
        }
        public override string? Launchable
        {
            get
            {
                if (_launchable == null && _app != null) _launchable = _app.Launchable;
                return _launchable;
            }
        }
        public override string? Icon
        {
            get
            {
                if (_icon == null && _app != null) _icon = _app.Icon;
                return _icon;
            }
        }
        public override List<string> Screenshots
        {
            get
            {
                if (_screenshots == null)
                    _screenshots = _app != null ? _app.Screenshots : new List<string>();
                return _screenshots;
            }
        }
        // AlpmPackage
        public override DateTimeOffset? BuildDate
        {
            get
            {
                if (_buildDate == null)
                    _buildDate = DateTimeOffset.FromUnixTimeSeconds(_alpmPkg!.Builddate);
                return _buildDate;
            }
        }
        public override string? Packager
        {
            get
            {
                if (_packager == null) _packager = _alpmPkg!.Packager;
                return _packager;
            }
        }
        public override string? Reason
        {
            get
            {
                if (!_reasonSet)
                {
                    _reasonSet = true;
                    FoundLocalPkg();
                    if (_localPkg != null)
                    {
                        if (_localPkg.Reason == PackageReason.EXPLICIT) _reason = "Explicitly installed";
                        else if (_localPkg.Reason == PackageReason.DEPEND) _reason = "Installed as a dependency for another package";
                    }
                }
                return _reason;
            }
        }
        public override List<string> Validations
        {
            get
            {
                if (_validations == null)
                {
                    _validations = new List<string>();
                    var validation = _alpmPkg!.Validation;
                    if (validation != 0)
                    {
                        if ((validation & PackageValidation.NONE) != 0) _validations.Add("None");
                        else
                        {
                            if ((validation & PackageValidation.MD5SUM) != 0) _validations.Add("MD5 Sum");
                            if ((validation & PackageValidation.SHA256SUM) != 0) _validations.Add("SHA-256 Sum");
                            if ((validation & PackageValidation.SIGNATURE) != 0) _validations.Add("Signature");
                        }
                    }
                    else
                    {
                        _validations.Add("Unknown");
                    }
                }
                return _validations;
            }
        }
        public override List<string> Groups
        {
            get
            {
                if (_groups == null)
                {
                    _groups = new List<string>();
                    foreach (IntPtr s in _alpmPkg!.Groups)
                        if (s != IntPtr.Zero) _groups.Add(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(s)!);
                }
                return _groups;
            }
        }
        public override List<string> Depends
        {
            get
            {
                if (_depends == null)
                {
                    _depends = new List<string>();
                    foreach (IntPtr d in _alpmPkg!.Depends)
                        _depends.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _depends;
            }
            internal set => _depends = value;
        }
        public override List<string> Optdepends
        {
            get
            {
                if (_optdepends == null)
                {
                    _optdepends = new List<string>();
                    foreach (IntPtr d in _alpmPkg!.Optdepends)
                        _optdepends.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _optdepends;
            }
        }
        public override List<string> Makedepends
        {
            get
            {
                if (_makedepends == null)
                {
                    _makedepends = new List<string>();
                    if (_syncPkg != null)
                        foreach (IntPtr d in _syncPkg.Makedepends)
                            _makedepends.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _makedepends;
            }
        }
        public override List<string> Checkdepends
        {
            get
            {
                if (_checkdepends == null)
                {
                    _checkdepends = new List<string>();
                    if (_syncPkg != null)
                        foreach (IntPtr d in _syncPkg.Checkdepends)
                            _checkdepends.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _checkdepends;
            }
        }
        public override List<string> Requiredby
        {
            get
            {
                if (_requiredby == null)
                {
                    _requiredby = new List<string>();
                    FoundLocalPkg();
                    if (_localPkg != null)
                        _requiredby.AddRange(_localPkg.ComputeRequiredby());
                }
                return _requiredby;
            }
        }
        public override List<string> Optionalfor
        {
            get
            {
                if (_optionalfor == null)
                {
                    _optionalfor = new List<string>();
                    FoundLocalPkg();
                    if (_localPkg != null)
                        _optionalfor.AddRange(_localPkg.ComputeOptionalfor());
                }
                return _optionalfor;
            }
        }
        public override List<string> Provides
        {
            get
            {
                if (_provides == null)
                {
                    _provides = new List<string>();
                    foreach (IntPtr d in _alpmPkg!.Provides)
                        _provides.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _provides;
            }
            internal set => _provides = value;
        }
        public override List<string> Replaces
        {
            get
            {
                if (_replaces == null)
                {
                    _replaces = new List<string>();
                    foreach (IntPtr d in _alpmPkg!.Replaces)
                        _replaces.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _replaces;
            }
            internal set => _replaces = value;
        }
        public override List<string> Conflicts
        {
            get
            {
                if (_conflicts == null)
                {
                    _conflicts = new List<string>();
                    foreach (IntPtr d in _alpmPkg!.Conflicts)
                        _conflicts.Add(AlpmDepend.FromPointer(d).ComputeString());
                }
                return _conflicts;
            }
            internal set => _conflicts = value;
        }
        public override List<string> Backups
        {
            get
            {
                if (_backups == null)
                {
                    _backups = new List<string>();
                    FoundLocalPkg();
                    if (_localPkg != null)
                    {
                        // Backup.name is stored as a char* in alpm_backup_t
                        foreach (IntPtr b in _localPkg.Backups)
                        {
                            // alpm_backup_t { char *name; time_t modified; }
                            IntPtr namePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(b);
                            if (namePtr != IntPtr.Zero)
                                _backups.Add("/" + System.Runtime.InteropServices.Marshal.PtrToStringUTF8(namePtr));
                        }
                    }
                }
                return _backups;
            }
        }

        internal void SetApp(App? app) => _app = app;
        internal void SetAlpmPkg(AlpmPkg? alpmPkg) => _alpmPkg = alpmPkg;
        internal void SetLocalPkg(AlpmPkg? localPkg)
        {
            _localPkg = localPkg;
            _localPkgSet = true;
        }
        internal void SetSyncPkg(AlpmPkg? syncPkg)
        {
            _syncPkg = syncPkg;
            _syncPkgSet = true;
        }

        protected void FoundLocalPkg()
        {
            if (!_localPkgSet)
            {
                _localPkgSet = true;
                if (_alpmPkg!.Origin == PackageFrom.LOCALDB) _localPkg = _alpmPkg;
                else if (_alpmPkg.Origin == PackageFrom.SYNCDB) _localPkg = database.InternGetLocalPkg(_alpmPkg.Name);
            }
        }

        protected void FoundSyncPkg()
        {
            if (!_syncPkgSet)
            {
                _syncPkgSet = true;
                if (_alpmPkg!.Origin == PackageFrom.LOCALDB) _syncPkg = database.InternGetSyncpkg(_alpmPkg.Name);
                else if (_alpmPkg.Origin == PackageFrom.SYNCDB) _syncPkg = _alpmPkg;
            }
        }

        public override List<string> GetFiles()
        {
            if (_files == null)
            {
                FoundLocalPkg();
                _files = database.GetPkgFiles(Name, _localPkg);
            }
            return _files;
        }

        public override async Task<List<string>> GetFilesAsync()
        {
            if (_files == null)
            {
                FoundLocalPkg();
                _files = await database.GetPkgFilesAsync(Name, _localPkg);
            }
            return _files;
        }
    }

    /// <summary>Eagerly-resolved alpm package (equivalent of AlpmPackageStatic).</summary>
    internal class AlpmPackageStatic : AlpmPackageLinked
    {
        // Package
        protected new string _version = null!;
        protected new string? _installedVersion;
        protected new string? _desc;
        protected new string? _repo;
        protected string? _url;
        // AlpmPackage
        protected new string? _packager;

        // Package
        public override string Version { get => _version; internal set => _version = value; }
        public override string? InstalledVersion { get => _installedVersion; internal set => _installedVersion = value; }
        public override string? Desc { get => _desc; internal set => _desc = value; }
        public override string? Repo { get => _repo; internal set => _repo = value; }
        public override string? Url { get => _url; }
        // AlpmPackage
        public override string? Packager => _packager;

        internal AlpmPackageStatic(AlpmPkg alpmPkg, AlpmPkg? localPkg, AlpmPkg? syncPkg, Database database)
        {
            this.database = database;
            _version = alpmPkg.Version;
            _desc = alpmPkg.Desc;
            _packager = alpmPkg.Packager;
            SetAlpmPkg(alpmPkg);
            SetLocalPkg(localPkg);
            SetSyncPkg(syncPkg);
            // force resolution of all lazy fields
            _ = Name; _ = Id; _ = License; _ = InstalledSize; _ = DownloadSize; _ = BuildDate;
            if (localPkg != null)
            {
                _installedVersion = localPkg.Version;
                _ = InstallDate; _ = Reason;
                _ = Requiredby; _ = Optionalfor; _ = Backups;
            }
            if (syncPkg != null)
            {
                _version = syncPkg.Version;
                _repo = syncPkg.Db?.Name;
                _ = Makedepends; _ = Checkdepends;
            }
            _ = Groups; _ = Validations; _ = Depends; _ = Optdepends; _ = Provides; _ = Replaces; _ = Conflicts;
            SetAlpmPkg(null);
            SetLocalPkg(null);
            SetSyncPkg(null);
        }

        internal AlpmPackageStatic(AlpmPkg alpmPkg, AlpmPkg? localPkg, AlpmPkg? syncPkg, Database database, bool transaction)
        {
            this.database = database;
            SetAlpmPkg(alpmPkg);
            SetLocalPkg(localPkg);
            SetSyncPkg(syncPkg);
            _ = Name;
            _version = alpmPkg.Version;
            _desc = alpmPkg.Desc;
            _ = InstalledSize; _ = DownloadSize;
            if (localPkg != null)
            {
                _installedVersion = localPkg.Version;
                _ = InstallDate;
            }
            if (syncPkg != null)
            {
                _repo = syncPkg.Db?.Name == "pamac_aur" ? "AUR" : syncPkg.Db?.Name;
            }
            _ = Provides;
            SetAlpmPkg(null);
            SetLocalPkg(null);
            SetSyncPkg(null);
        }
    }

    /// <summary>Base class for AUR packages (equivalent of AURPackage in alpm_package.vala).</summary>
    public abstract class AURPackage : AlpmPackage
    {
        public abstract string? Packagebase { get; internal set; }
        public abstract string? Maintainer { get; }
        public abstract double Popularity { get; }
        public abstract DateTimeOffset? Lastmodified { get; }
        public abstract DateTimeOffset? Outofdate { get; }
        public abstract DateTimeOffset? Firstsubmitted { get; }
        public abstract ulong Numvotes { get; }

        internal AURPackage() { }
    }

    /// <summary>Lazily-resolved AUR package (equivalent of AURPackageLinked).</summary>
    internal class AURPackageLinked : AURPackage
    {
        // Package
        private bool _installedVersionSet, _installDateSet, _installedSizeSet, _downloadSizeSet, _licenseSet, _reasonSet, _packagerSet, _buildDateSet;
        private string? _name;
        private string? _id;
        private string? _version;
        private string? _installedVersion;
        private string? _desc;
        private string? _repo;
        private string? _license;
        private string? _url;
        private ulong _installedSize;
        private ulong _downloadSize;
        private DateTimeOffset? _installDate;
        // AlpmPackage
        private AlpmPkg? _localPkg;
        private Database database = null!;
        private DateTimeOffset? _buildDate;
        private string? _packager;
        private string? _reason;
        private List<string> _validations = null!;
        private List<string> _groups = null!;
        private List<string> _depends = null!;
        private List<string> _optdepends = null!;
        private List<string> _makedepends = null!;
        private List<string> _checkdepends = null!;
        private List<string> _requiredby = null!;
        private List<string> _optionalfor = null!;
        private List<string> _provides = null!;
        private List<string> _replaces = null!;
        private List<string> _conflicts = null!;
        private List<string> _backups = null!;
        private List<string> _files = null!;
        private List<string> _screenshots = null!;
        // AURInfos
        private AURInfos? aurInfos;
        private bool isUpdate;
        private string? _packagebase;
        private string? _maintainer;
        private double _popularity;
        private DateTimeOffset? _lastmodified;
        private DateTimeOffset? _outofdate;
        private DateTimeOffset? _firstsubmitted;
        private ulong _numvotes;

        public override string Name
        {
            get { if (_name == null && aurInfos != null) _name = aurInfos.Name; return _name!; }
            internal set => _name = value;
        }
        public override string Id
        {
            get { if (_id == null && aurInfos != null) _id = aurInfos.Name; return _id!; }
            internal set => _id = value;
        }
        public override string Version
        {
            get { if (_version == null && aurInfos != null) _version = aurInfos.Version; return _version!; }
            internal set => _version = value;
        }
        public override string? InstalledVersion
        {
            get
            {
                if (!_installedVersionSet) { _installedVersionSet = true; if (_localPkg != null) _installedVersion = _localPkg.Version; }
                return _installedVersion;
            }
            internal set => _installedVersion = value;
        }
        public override string? Desc
        {
            get
            {
                if (_desc == null)
                {
                    if (!isUpdate && _localPkg != null) _desc = _localPkg.Desc;
                    else if (aurInfos != null) _desc = aurInfos.Desc;
                }
                return _desc;
            }
            internal set => _desc = value;
        }
        public override string? Repo
        {
            get { if (_repo == null) _repo = "AUR"; return _repo; }
            internal set => _repo = value;
        }
        public override string? License
        {
            get
            {
                if (_license == null && !_licenseSet)
                {
                    _licenseSet = true;
                    if (!isUpdate && _localPkg != null)
                    {
                        var list = _localPkg.Licenses;
                        if (!list.IsEmpty)
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (IntPtr s in list)
                            {
                                if (s == IntPtr.Zero) continue;
                                if (sb.Length > 0) sb.Append(' ');
                                sb.Append(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(s));
                            }
                            _license = sb.ToString();
                        }
                        else _license = "Unknown";
                    }
                    else if (aurInfos != null) _license = aurInfos.License;
                }
                return _license;
            }
        }
        public override string? Url
        {
            get
            {
                if (_url == null)
                {
                    if (!isUpdate && _localPkg != null) _url = _localPkg.Url;
                    else if (aurInfos != null) _url = aurInfos.Url;
                }
                return _url;
            }
        }
        public override ulong InstalledSize
        {
            get { if (!_installedSizeSet) { _installedSizeSet = true; if (_localPkg != null) _installedSize = _localPkg.Isize; } return _installedSize; }
        }
        public override ulong DownloadSize
        {
            get { if (!_downloadSizeSet) { _downloadSizeSet = true; if (_localPkg != null) _downloadSize = _localPkg.DownloadSize(database.GetHandleForDownload()); } return _downloadSize; }
        }
        public override DateTimeOffset? InstallDate
        {
            get { if (!_installDateSet) { _installDateSet = true; if (_localPkg != null) _installDate = DateTimeOffset.FromUnixTimeSeconds(_localPkg.Installdate); } return _installDate; }
        }
        // App
        public override string? AppName => null;
        public override string? AppId => null;
        public override string? LongDesc => null;
        public override string? Launchable => null;
        public override string? Icon => null;
        public override List<string> Screenshots => _screenshots ??= new List<string>();
        // AlpmPackage
        public override DateTimeOffset? BuildDate
        {
            get { if (!_buildDateSet) { _buildDateSet = true; if (_localPkg != null) _buildDate = DateTimeOffset.FromUnixTimeSeconds(_localPkg.Builddate); } return _buildDate; }
        }
        public override string? Packager
        {
            get { if (!_packagerSet) { _packagerSet = true; if (_localPkg != null) _packager = _localPkg.Packager; } return _packager; }
        }
        public override string? Reason
        {
            get
            {
                if (!_reasonSet)
                {
                    _reasonSet = true;
                    if (_localPkg != null)
                    {
                        if (_localPkg.Reason == PackageReason.EXPLICIT) _reason = "Explicitly installed";
                        else if (_localPkg.Reason == PackageReason.DEPEND) _reason = "Installed as a dependency for another package";
                    }
                }
                return _reason;
            }
        }
        public override List<string> Validations
        {
            get
            {
                if (_validations == null)
                {
                    _validations = new List<string>();
                    if (_localPkg != null)
                    {
                        var validation = _localPkg.Validation;
                        if (validation != 0)
                        {
                            if ((validation & PackageValidation.NONE) != 0) _validations.Add("None");
                            else
                            {
                                if ((validation & PackageValidation.MD5SUM) != 0) _validations.Add("MD5 Sum");
                                if ((validation & PackageValidation.SHA256SUM) != 0) _validations.Add("SHA-256 Sum");
                                if ((validation & PackageValidation.SIGNATURE) != 0) _validations.Add("Signature");
                            }
                        }
                        else _validations.Add("Unknown");
                    }
                    else _validations.Add("Unknown");
                }
                return _validations;
            }
        }
        public override List<string> Groups
        {
            get
            {
                if (_groups == null)
                {
                    _groups = new List<string>();
                    if (!isUpdate && _localPkg != null)
                    {
                        foreach (IntPtr s in _localPkg.Groups)
                            if (s != IntPtr.Zero) _groups.Add(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(s)!);
                    }
                    else if (aurInfos != null) _groups = aurInfos.Groups;
                }
                return _groups;
            }
        }
        public override List<string> Depends
        {
            get
            {
                if (_depends == null)
                {
                    _depends = new List<string>();
                    if (!isUpdate && _localPkg != null)
                    {
                        foreach (IntPtr d in _localPkg.Depends) _depends.Add(AlpmDepend.FromPointer(d).ComputeString());
                    }
                    else if (aurInfos != null) _depends = aurInfos.Depends;
                }
                return _depends;
            }
            internal set => _depends = value;
        }
        public override List<string> Optdepends
        {
            get
            {
                if (_optdepends == null)
                {
                    _optdepends = new List<string>();
                    if (!isUpdate && _localPkg != null)
                    {
                        foreach (IntPtr d in _localPkg.Optdepends) _optdepends.Add(AlpmDepend.FromPointer(d).ComputeString());
                    }
                    else if (aurInfos != null) _optdepends = aurInfos.Optdepends;
                }
                return _optdepends;
            }
        }
        public override List<string> Makedepends
        {
            get
            {
                if (_makedepends == null) _makedepends = aurInfos != null ? aurInfos.Makedepends : new List<string>();
                return _makedepends;
            }
        }
        public override List<string> Checkdepends
        {
            get
            {
                if (_checkdepends == null) _checkdepends = aurInfos != null ? aurInfos.Checkdepends : new List<string>();
                return _checkdepends;
            }
        }
        public override List<string> Requiredby
        {
            get
            {
                if (_requiredby == null)
                {
                    _requiredby = new List<string>();
                    if (!isUpdate && _localPkg != null) _requiredby.AddRange(_localPkg.ComputeRequiredby());
                }
                return _requiredby;
            }
        }
        public override List<string> Optionalfor
        {
            get
            {
                if (_optionalfor == null)
                {
                    _optionalfor = new List<string>();
                    if (!isUpdate && _localPkg != null) _optionalfor.AddRange(_localPkg.ComputeOptionalfor());
                }
                return _optionalfor;
            }
        }
        public override List<string> Provides
        {
            get
            {
                if (_provides == null)
                {
                    _provides = new List<string>();
                    if (!isUpdate && _localPkg != null)
                    {
                        foreach (IntPtr d in _localPkg.Provides) _provides.Add(AlpmDepend.FromPointer(d).ComputeString());
                    }
                    else if (aurInfos != null) _provides = aurInfos.Provides;
                }
                return _provides;
            }
            internal set => _provides = value;
        }
        public override List<string> Replaces
        {
            get
            {
                if (_replaces == null)
                {
                    _replaces = new List<string>();
                    if (!isUpdate && _localPkg != null)
                    {
                        foreach (IntPtr d in _localPkg.Replaces) _replaces.Add(AlpmDepend.FromPointer(d).ComputeString());
                    }
                    else if (aurInfos != null) _replaces = aurInfos.Replaces;
                }
                return _replaces;
            }
            internal set => _replaces = value;
        }
        public override List<string> Conflicts
        {
            get
            {
                if (_conflicts == null)
                {
                    _conflicts = new List<string>();
                    if (!isUpdate && _localPkg != null)
                    {
                        foreach (IntPtr d in _localPkg.Conflicts) _conflicts.Add(AlpmDepend.FromPointer(d).ComputeString());
                    }
                    else if (aurInfos != null) _conflicts = aurInfos.Conflicts;
                }
                return _conflicts;
            }
            internal set => _conflicts = value;
        }
        public override List<string> Backups
        {
            get
            {
                if (_backups == null)
                {
                    _backups = new List<string>();
                    if (_localPkg != null)
                    {
                        foreach (IntPtr b in _localPkg.Backups)
                        {
                            IntPtr namePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(b);
                            if (namePtr != IntPtr.Zero)
                                _backups.Add("/" + System.Runtime.InteropServices.Marshal.PtrToStringUTF8(namePtr));
                        }
                    }
                }
                return _backups;
            }
        }
        // AURInfos
        public override string? Packagebase
        {
            get { if (_packagebase == null && aurInfos != null) _packagebase = aurInfos.Packagebase; return _packagebase; }
            internal set => _packagebase = value;
        }
        public override string? Maintainer
        {
            get { if (_maintainer == null && aurInfos != null) _maintainer = aurInfos.Maintainer; return _maintainer; }
        }
        public override double Popularity
        {
            get { if (_popularity == 0 && aurInfos != null) _popularity = aurInfos.Popularity; return _popularity; }
        }
        public override DateTimeOffset? Lastmodified
        {
            get { if (_lastmodified == null && aurInfos != null) _lastmodified = aurInfos.Lastmodified; return _lastmodified; }
        }
        public override DateTimeOffset? Outofdate
        {
            get { if (_outofdate == null && aurInfos != null) _outofdate = aurInfos.Outofdate; return _outofdate; }
        }
        public override DateTimeOffset? Firstsubmitted
        {
            get { if (_firstsubmitted == null && aurInfos != null) _firstsubmitted = aurInfos.Firstsubmitted; return _firstsubmitted; }
        }
        public override ulong Numvotes
        {
            get { if (_numvotes == 0) _numvotes = aurInfos!.Numvotes; return _numvotes; }
        }

        internal AURPackageLinked() { }

        internal void InitialiseFromAurInfos(AURInfos? aurInfos, AlpmPkg? localPkg, Database database, bool isUpdate = false)
        {
            this.aurInfos = aurInfos;
            _localPkg = localPkg;
            this.database = database;
            this.isUpdate = isUpdate;
        }

        public override List<string> GetFiles()
        {
            if (_files == null)
            {
                if (_localPkg == null) _files = new List<string>();
                else _files = database.GetPkgFiles(_localPkg.Name, _localPkg);
            }
            return _files;
        }

        public override async Task<List<string>> GetFilesAsync()
        {
            if (_files == null)
            {
                if (_localPkg == null) _files = new List<string>();
                else _files = await database.GetPkgFilesAsync(_localPkg.Name, _localPkg);
            }
            return _files;
        }
    }

    /// <summary>Statically-resolved AUR package (equivalent of AURPackageStatic).</summary>
    internal class AURPackageStatic : AURPackageLinked
    {
        // Package
        protected new string? _desc;
        // AURPackage
        protected new string? _packagebase;

        public override string? Desc { get => _desc; internal set => _desc = value; }
        public override string? Packagebase { get => _packagebase; internal set => _packagebase = value; }

        internal AURPackageStatic() { }
    }
}
