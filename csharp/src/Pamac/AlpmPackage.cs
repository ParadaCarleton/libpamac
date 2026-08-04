// libpamac — C# port
//
// AlpmPackage and friends: package wrappers around libalpm, the AUR package
// types, plus the TransactionSummary and Updates result containers.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Text;
using LibAlpm;
using Pamac.Compat;
using static Pamac.Compat.Gettext;

namespace Pamac
{
	public abstract class AlpmPackage : Package
	{
		// AlpmPackage
		public abstract DateTime? BuildDate { get; }
		public abstract string? Packager { get; }
		public abstract string? Reason { get; }
		public abstract List<string> Validations { get; }
		public abstract List<string> Groups { get; }
		public abstract List<string> Depends { get; set; }
		public abstract List<string> OptDepends { get; }
		public abstract List<string> MakeDepends { get; }
		public abstract List<string> CheckDepends { get; }
		public abstract List<string> RequiredBy { get; }
		public abstract List<string> OptionalFor { get; }
		public abstract List<string> Provides { get; set; }
		public abstract List<string> Replaces { get; set; }
		public abstract List<string> Conflicts { get; set; }
		public abstract List<string> Backups { get; }

		public abstract List<string> GetFiles();
	}

	internal class AlpmPackageLinked : AlpmPackage
	{
		// Package backing fields
		string _name = null!;
		string _id = null!;
		string? _version;
		string? _installedVersion;
		string? _desc;
		string? _repo;
		string? _license;
		string? _url;
		ulong _installedSize;
		ulong _downloadSize;
		DateTime? _installDate;
		// App
		App? _app;
		string? _appName;
		string? _appId;
		string? _longDesc;
		string? _launchable;
		string? _icon;
		List<string> _screenshots = null!;
		// AlpmPackage
		Database _database = null!;
		Package? _alpmPkg;
		Package? _localPkg;
		Package? _syncPkg;
		bool _localPkgSet;
		bool _syncPkgSet;
		bool _versionSet;
		bool _installedVersionSet;
		bool _repoSet;
		bool _licenseSet;
		bool _installDateSet;
		bool _downloadSizeSet;
		bool _installedSizeSet;
		bool _reasonSet;
		DateTime? _buildDate;
		string? _packager;
		string? _reason;
		List<string> _validations = null!;
		List<string> _groups = null!;
		List<string> _depends = null!;
		List<string> _optDepends = null!;
		List<string> _makeDepends = null!;
		List<string> _checkDepends = null!;
		List<string> _requiredBy = null!;
		List<string> _optionalFor = null!;
		List<string> _provides = null!;
		List<string> _replaces = null!;
		List<string> _conflicts = null!;
		List<string> _backups = null!;
		List<string> _files = null!;

		internal AlpmPackageLinked(Package alpmPkg, Database database)
		{
			_alpmPkg = alpmPkg;
			_database = database;
		}

		// ---- Package ----
		public override string Name
		{
			get
			{
				if (_name == null) _name = _alpmPkg!.Name;
				return _name;
			}
			set => _name = value;
		}

		public override string Id
		{
			get
			{
				if (_id == null)
				{
					_id = _app != null ? AppId! : Name;
				}
				return _id;
			}
			set => _id = value;
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
			set => _version = value;
		}

		public override string? InstalledVersion
		{
			get
			{
				if (!_installedVersionSet)
				{
					_installedVersionSet = true;
					FoundLocalPkg();
					_installedVersion = _localPkg?.Version;
				}
				return _installedVersion;
			}
			set => _installedVersion = value;
		}

		public override string? Desc
		{
			get
			{
				if (_desc == null)
				{
					if (_app != null)
					{
						_desc = _app.Desc ?? _alpmPkg!.Desc;
					}
					else
					{
						_desc = _alpmPkg!.Desc;
					}
				}
				return _desc;
			}
			set => _desc = value;
		}

		public override string? Repo
		{
			get
			{
				if (!_repoSet)
				{
					_repoSet = true;
					FoundSyncPkg();
					_repo = _syncPkg?.DB?.Name;
				}
				return _repo;
			}
			set => _repo = value;
		}

		public override string? License
		{
			get
			{
				if (!_licenseSet)
				{
					_licenseSet = true;
					if (_alpmPkg!.Licenses.Count > 0)
					{
						var sb = new StringBuilder(_alpmPkg.Licenses[0]);
						for (int i = 1; i < _alpmPkg.Licenses.Count; i++)
						{
							sb.Append(' ').Append(_alpmPkg.Licenses[i]);
						}
						_license = sb.ToString();
					}
					else
					{
						_license = DGettext(null, "Unknown");
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
					_installedSize = _alpmPkg!.ISize;
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
					_downloadSize = _alpmPkg!.DownloadSize;
				}
				return _downloadSize;
			}
		}

		public override DateTime? InstallDate
		{
			get
			{
				if (!_installDateSet)
				{
					_installDateSet = true;
					FoundLocalPkg();
					if (_localPkg != null)
					{
						_installDate = DateTimeCompat.FromUnixLocal(_localPkg.InstallDate);
					}
				}
				return _installDate;
			}
		}

		// ---- App ----
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
				{
					_screenshots = _app != null ? _app.Screenshots : new List<string>();
				}
				return _screenshots;
			}
		}

		// ---- AlpmPackage ----
		public override DateTime? BuildDate
		{
			get
			{
				if (_buildDate == null) _buildDate = DateTimeCompat.FromUnixLocal(_alpmPkg!.BuildDate);
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
						if (_localPkg.Reason == PackageReason.Explicit)
						{
							_reason = DGettext(null, "Explicitly installed");
						}
						else if (_localPkg.Reason == PackageReason.Depend)
						{
							_reason = DGettext(null, "Installed as a dependency for another package");
						}
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
					PackageValidation validation = _alpmPkg!.Validation;
					if (validation != 0)
					{
						if ((validation & PackageValidation.None) != 0)
						{
							_validations.Add(DGettext(null, "None"));
						}
						else
						{
							if ((validation & PackageValidation.Md5Sum) != 0)
								_validations.Add(DGettext(null, "MD5 Sum"));
							if ((validation & PackageValidation.Sha256Sum) != 0)
								_validations.Add(DGettext(null, "SHA-256 Sum"));
							if ((validation & PackageValidation.Signature) != 0)
								_validations.Add(DGettext(null, "Signature"));
						}
					}
					else
					{
						_validations.Add(DGettext(null, "Unknown"));
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
					_groups = new List<string>(_alpmPkg!.Groups);
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
					foreach (var d in _alpmPkg!.Depends) _depends.Add(d.ComputeString());
				}
				return _depends;
			}
			set => _depends = value;
		}

		public override List<string> OptDepends
		{
			get
			{
				if (_optDepends == null)
				{
					_optDepends = new List<string>();
					foreach (var d in _alpmPkg!.OptDepends) _optDepends.Add(d.ComputeString());
				}
				return _optDepends;
			}
		}

		public override List<string> MakeDepends
		{
			get
			{
				if (_makeDepends == null)
				{
					_makeDepends = new List<string>();
					foreach (var d in _alpmPkg!.MakeDepends) _makeDepends.Add(d.ComputeString());
				}
				return _makeDepends;
			}
		}

		public override List<string> CheckDepends
		{
			get
			{
				if (_checkDepends == null)
				{
					_checkDepends = new List<string>();
					foreach (var d in _alpmPkg!.CheckDepends) _checkDepends.Add(d.ComputeString());
				}
				return _checkDepends;
			}
		}

		public override List<string> RequiredBy
		{
			get
			{
				if (_requiredBy == null)
				{
					_requiredBy = new List<string>();
					if (_localPkg != null) _requiredBy.AddRange(_localPkg.ComputeRequiredBy());
				}
				return _requiredBy;
			}
		}

		public override List<string> OptionalFor
		{
			get
			{
				if (_optionalFor == null)
				{
					_optionalFor = new List<string>();
					if (_localPkg != null) _optionalFor.AddRange(_localPkg.ComputeOptionalFor());
				}
				return _optionalFor;
			}
		}

		public override List<string> Provides
		{
			get
			{
				if (_provides == null)
				{
					_provides = new List<string>();
					foreach (var d in _alpmPkg!.Provides) _provides.Add(d.ComputeString());
				}
				return _provides;
			}
			set => _provides = value;
		}

		public override List<string> Replaces
		{
			get
			{
				if (_replaces == null)
				{
					_replaces = new List<string>();
					foreach (var d in _alpmPkg!.Replaces) _replaces.Add(d.ComputeString());
				}
				return _replaces;
			}
			set => _replaces = value;
		}

		public override List<string> Conflicts
		{
			get
			{
				if (_conflicts == null)
				{
					_conflicts = new List<string>();
					foreach (var d in _alpmPkg!.Conflicts) _conflicts.Add(d.ComputeString());
				}
				return _conflicts;
			}
			set => _conflicts = value;
		}

		public override List<string> Backups
		{
			get
			{
				if (_backups == null)
				{
					_backups = new List<string>();
					foreach (var b in _alpmPkg!.Backups)
					{
						_backups.Add("/" + b.Name);
					}
				}
				return _backups;
			}
		}

		internal void FoundSyncPkg()
		{
			if (_syncPkg == null && !_syncPkgSet)
			{
				_syncPkgSet = true;
				_syncPkg = _database.InternGetSyncPkg(Name);
			}
		}

		internal void FoundLocalPkg()
		{
			if (_localPkg == null && !_localPkgSet)
			{
				_localPkgSet = true;
				_localPkg = _database.InternGetLocalPkg(Name);
			}
		}

		internal void SetSyncPkg(Package? syncPkg)
		{
			_syncPkg = syncPkg;
			_syncPkgSet = true;
		}

		internal void SetLocalPkg(Package? localPkg)
		{
			_localPkg = localPkg;
			_localPkgSet = true;
		}

		internal void SetApp(App? app) => _app = app;

		public override List<string> GetFiles()
		{
			if (_files == null)
			{
				if (_localPkg == null)
				{
					_files = new List<string>();
				}
				else
				{
					_files = _database.GetPkgFiles(_localPkg.Name, _localPkg);
				}
			}
			return _files;
		}
	}

	/// <summary>Static alpm package used for update lists (it is a sync package).</summary>
	internal class AlpmPackageStatic : AlpmPackageLinked
	{
		string? _desc;
		Package _syncPkg;
		Package _localPkg;

		public AlpmPackageStatic(Package? localPkg, Package? syncPkg) : base(syncPkg!, null!)
		{
			_syncPkg = syncPkg!;
			_localPkg = localPkg!;
			base.SetSyncPkg(_syncPkg);
			base.SetLocalPkg(_localPkg);
		}

		public override string? Desc
		{
			get => _desc ?? base.Desc;
			set => _desc = value;
		}
	}

	/// <summary>An AUR package.</summary>
	public abstract class AURPackage : AlpmPackage
	{
		public abstract string? PackageBase { get; set; }
		public abstract string? Maintainer { get; }
		public abstract double Popularity { get; }
		public abstract DateTime? LastModified { get; }
		public abstract DateTime? OutOfDate { get; }
		public abstract DateTime? FirstSubmitted { get; }
		public abstract ulong NumVotes { get; }
	}

	internal class AURPackageLinked : AURPackage
	{
		AURInfos? _aurInfos;
		Package? _localPkg;
		Database _database = null!;
		bool _isUpdate;

		// Package
		string? _name;
		string? _id;
		string? _version;
		string? _desc;
		string? _repo;
		string? _license;
		string? _url;
		DateTime? _installDate;
		string? _packager;
		string? _reason;
		List<string> _validations = null!;
		List<string> _groups = null!;
		List<string> _depends = null!;
		List<string> _optDepends = null!;
		List<string> _makeDepends = null!;
		List<string> _checkDepends = null!;
		List<string> _requiredBy = null!;
		List<string> _optionalFor = null!;
		List<string> _provides = null!;
		List<string> _replaces = null!;
		List<string> _conflicts = null!;
		List<string> _backups = null!;
		List<string> _files = null!;
		// AURInfos
		string? _packageBase;
		string? _maintainer;
		double _popularity;
		DateTime? _lastModified;
		DateTime? _outOfDate;
		DateTime? _firstSubmitted;
		ulong _numVotes;

		internal AURPackageLinked() { }

		internal void InitialiseFromAurInfos(AURInfos? aurInfos, Package? localPkg, Database database, bool isUpdate = false)
		{
			_aurInfos = aurInfos;
			_localPkg = localPkg;
			_database = database;
			_isUpdate = isUpdate;
		}

		// ---- Package ----
		public override string Name
		{
			get
			{
				if (_name == null) _name = _aurInfos?.Name ?? _localPkg?.Name ?? string.Empty;
				return _name;
			}
			set => _name = value;
		}

		public override string Id
		{
			get
			{
				if (_id == null) _id = Name;
				return _id;
			}
			set => _id = value;
		}

		public override string Version
		{
			get
			{
				if (_version == null) _version = _aurInfos?.Version ?? _localPkg?.Version ?? string.Empty;
				return _version!;
			}
			set => _version = value;
		}

		public override string? InstalledVersion
		{
			get => _localPkg?.Version;
			set { }
		}

		public override string? Desc
		{
			get
			{
				if (_desc == null) _desc = _aurInfos?.Desc ?? _localPkg?.Desc;
				return _desc;
			}
			set => _desc = value;
		}

		public override string? Repo
		{
			get
			{
				if (_repo == null) _repo = _aurInfos != null ? DGettext(null, "AUR") : null;
				return _repo;
			}
			set => _repo = value;
		}

		public override string? License
		{
			get
			{
				if (_license == null) _license = _aurInfos?.License;
				return _license;
			}
		}

		public override string? Url
		{
			get
			{
				if (_url == null) _url = _aurInfos?.Url;
				return _url;
			}
		}

		public override DateTime? InstallDate
		{
			get
			{
				if (_installDate == null && _localPkg != null)
					_installDate = DateTimeCompat.FromUnixLocal(_localPkg.InstallDate);
				return _installDate;
			}
		}

		public override string? AppName => null;
		public override string? AppId => null;
		public override string? LongDesc => null;
		public override string? Launchable => null;
		public override string? Icon => null;
		public override List<string> Screenshots => new List<string>();

		// ---- AlpmPackage ----
		public override DateTime? BuildDate => null;
		public override string? Packager => null;

		public override string? Reason
		{
			get
			{
				if (_reason == null && _localPkg != null)
				{
					_reason = _localPkg.Reason == PackageReason.Explicit
						? DGettext(null, "Explicitly installed")
						: DGettext(null, "Installed as a dependency for another package");
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
							if ((validation & PackageValidation.None) != 0) _validations.Add(DGettext(null, "None"));
							else
							{
								if ((validation & PackageValidation.Md5Sum) != 0) _validations.Add(DGettext(null, "MD5 Sum"));
								if ((validation & PackageValidation.Sha256Sum) != 0) _validations.Add(DGettext(null, "SHA-256 Sum"));
								if ((validation & PackageValidation.Signature) != 0) _validations.Add(DGettext(null, "Signature"));
							}
						}
						else _validations.Add(DGettext(null, "Unknown"));
					}
					else _validations.Add(DGettext(null, "Unknown"));
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
					if (!_isUpdate && _localPkg != null) _groups.AddRange(_localPkg.Groups);
					else if (_aurInfos != null) _groups = new List<string>(_aurInfos.Groups);
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
					if (!_isUpdate && _localPkg != null)
					{
						foreach (var d in _localPkg.Depends) _depends.Add(d.ComputeString());
					}
					else if (_aurInfos != null) _depends = new List<string>(_aurInfos.Depends);
				}
				return _depends;
			}
			set => _depends = value;
		}

		public override List<string> OptDepends
		{
			get
			{
				if (_optDepends == null)
				{
					_optDepends = new List<string>();
					if (!_isUpdate && _localPkg != null)
					{
						foreach (var d in _localPkg.OptDepends) _optDepends.Add(d.ComputeString());
					}
					else if (_aurInfos != null) _optDepends = new List<string>(_aurInfos.OptDepends);
				}
				return _optDepends;
			}
		}

		public override List<string> MakeDepends
		{
			get
			{
				if (_makeDepends == null)
				{
					_makeDepends = new List<string>();
					if (_aurInfos != null) _makeDepends = new List<string>(_aurInfos.MakeDepends);
				}
				return _makeDepends;
			}
		}

		public override List<string> CheckDepends
		{
			get
			{
				if (_checkDepends == null)
				{
					_checkDepends = new List<string>();
					if (_aurInfos != null) _checkDepends = new List<string>(_aurInfos.CheckDepends);
				}
				return _checkDepends;
			}
		}

		public override List<string> RequiredBy
		{
			get
			{
				if (_requiredBy == null)
				{
					_requiredBy = new List<string>();
					if (!_isUpdate && _localPkg != null) _requiredBy.AddRange(_localPkg.ComputeRequiredBy());
				}
				return _requiredBy;
			}
		}

		public override List<string> OptionalFor
		{
			get
			{
				if (_optionalFor == null)
				{
					_optionalFor = new List<string>();
					if (!_isUpdate && _localPkg != null) _optionalFor.AddRange(_localPkg.ComputeOptionalFor());
				}
				return _optionalFor;
			}
		}

		public override List<string> Provides
		{
			get
			{
				if (_provides == null)
				{
					_provides = new List<string>();
					if (!_isUpdate && _localPkg != null)
					{
						foreach (var d in _localPkg.Provides) _provides.Add(d.ComputeString());
					}
					else if (_aurInfos != null) _provides = new List<string>(_aurInfos.Provides);
				}
				return _provides;
			}
			set => _provides = value;
		}

		public override List<string> Replaces
		{
			get
			{
				if (_replaces == null)
				{
					_replaces = new List<string>();
					if (!_isUpdate && _localPkg != null)
					{
						foreach (var d in _localPkg.Replaces) _replaces.Add(d.ComputeString());
					}
					else if (_aurInfos != null) _replaces = new List<string>(_aurInfos.Replaces);
				}
				return _replaces;
			}
			set => _replaces = value;
		}

		public override List<string> Conflicts
		{
			get
			{
				if (_conflicts == null)
				{
					_conflicts = new List<string>();
					if (!_isUpdate && _localPkg != null)
					{
						foreach (var d in _localPkg.Conflicts) _conflicts.Add(d.ComputeString());
					}
					else if (_aurInfos != null) _conflicts = new List<string>(_aurInfos.Conflicts);
				}
				return _conflicts;
			}
			set => _conflicts = value;
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
						foreach (var b in _localPkg.Backups) _backups.Add("/" + b.Name);
					}
				}
				return _backups;
			}
		}

		// ---- AURInfos ----
		public override string? PackageBase
		{
			get
			{
				if (_packageBase == null && _aurInfos != null) _packageBase = _aurInfos.PackageBase;
				return _packageBase;
			}
			set => _packageBase = value;
		}

		public override string? Maintainer
		{
			get
			{
				if (_maintainer == null && _aurInfos != null) _maintainer = _aurInfos.Maintainer;
				return _maintainer;
			}
		}

		public override double Popularity
		{
			get
			{
				if (_popularity == 0 && _aurInfos != null) _popularity = _aurInfos.Popularity;
				return _popularity;
			}
		}

		public override DateTime? LastModified
		{
			get
			{
				if (_lastModified == null && _aurInfos != null) _lastModified = _aurInfos.LastModified;
				return _lastModified;
			}
		}

		public override DateTime? OutOfDate
		{
			get
			{
				if (_outOfDate == null && _aurInfos != null) _outOfDate = _aurInfos.OutOfDate;
				return _outOfDate;
			}
		}

		public override DateTime? FirstSubmitted
		{
			get
			{
				if (_firstSubmitted == null && _aurInfos != null) _firstSubmitted = _aurInfos.FirstSubmitted;
				return _firstSubmitted;
			}
		}

		public override ulong NumVotes
		{
			get
			{
				if (_numVotes == 0 && _aurInfos != null) _numVotes = _aurInfos.NumVotes;
				return _numVotes;
			}
		}

		public override List<string> GetFiles()
		{
			if (_files == null)
			{
				if (_localPkg == null) _files = new List<string>();
				else _files = _database.GetPkgFiles(_localPkg.Name, _localPkg);
			}
			return _files;
		}
	}

	/// <summary>Static AUR package (used for update lists).</summary>
	internal class AURPackageStatic : AURPackageLinked
	{
		public override string? Desc { get; set; }
		public override string? PackageBase { get; set; }
	}

	/// <summary>Summary of a pending transaction, used by the UI to ask for confirmation.</summary>
	public class TransactionSummary
	{
		public List<Package> ToInstall { get; internal set; } = new List<Package>();
		public List<Package> ToUpgrade { get; internal set; } = new List<Package>();
		public List<Package> ToDowngrade { get; internal set; } = new List<Package>();
		public List<Package> ToReinstall { get; internal set; } = new List<Package>();
		public List<Package> ToRemove { get; internal set; } = new List<Package>();
		public List<Package> ConflictsToRemove { get; internal set; } = new List<Package>();
		public List<Package> ToBuild { get; internal set; } = new List<Package>();
		public List<string> AurPkgBasesToBuild { get; internal set; } = new List<string>();
		public List<string> ToLoad { get; internal set; } = new List<string>();
	}

	/// <summary>Result of a check-updates run, split by kind and ignore state.</summary>
	public class Updates
	{
		public List<AlpmPackage> ReposUpdates { get; internal set; } = new List<AlpmPackage>();
		public List<AlpmPackage> IgnoredReposUpdates { get; internal set; } = new List<AlpmPackage>();
		public List<AURPackage> AurUpdates { get; internal set; } = new List<AURPackage>();
		public List<AURPackage> IgnoredAurUpdates { get; internal set; } = new List<AURPackage>();
		public List<AURPackage> OutOfDate { get; internal set; } = new List<AURPackage>();
		public List<FlatpakPackage> FlatpakUpdates { get; internal set; } = new List<FlatpakPackage>();
	}
}
