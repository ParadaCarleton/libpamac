// libpamac — C# port
//
// Managed model for the libalpm (Arch Linux Package Manager) C library.
// This mirrors the surface of `vapi/libalpm.vapi` that libpamac uses.
// In a real deployment this is backed by the libalpm shared library; the
// managed model below preserves the exact member names so the ported code
// reads identically to the Vala original.
//
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;

namespace LibAlpm
{
	public enum Errno
	{
		Ok = 0,
		DB_VERSION = -100,
		HandleLock = -1,
		TransDupTarget = -11,
		PkgIgnored = -10,
		PkgNotFound = -2,
		PkgInvalidArch = -12,
		UnsatisfiedDeps = -13,
		ConflictingDeps = -14,
		FileConflicts = -15,
		PkgInvalidCheckSum = -17,
		PkgInvalid = -18,
		PkgInvalidSig = -19,
		// (subset, see libalpm source)
	}

	public static class Alpm
	{
		public static string Strerror(Errno error) => error.ToString();

		/// <summary>alpm_pkg_vercmp: compare two package versions, returns -1, 0 or 1.</summary>
		public static int PkgVerCmp(string? a, string? b)
		{
			a ??= string.Empty;
			b ??= string.Empty;
			return string.CompareOrdinal(a, b) switch
			{
				< 0 => -1,
				> 0 => 1,
				_ => 0
			};
		}

		public static Package? FindSatisfier(List<Package>? pkgcache, string depstring)
		{
			if (pkgcache == null) return null;
			foreach (var pkg in pkgcache)
			{
				if (pkg.ProvidesSatisfies(depstring) || string.Equals(pkg.Name, depstring, StringComparison.Ordinal))
					return pkg;
			}
			return null;
		}
	}

	public enum PackageFrom
	{
		Unknown = 0,
		LocalDb,
		SyncDb,
		File
	}

	public enum PackageReason
	{
		Explicit = 0,
		Depend = 1
	}

	[Flags]
	public enum PackageValidation
	{
		None = 0,
		Md5Sum = 1 << 0,
		Sha256Sum = 1 << 1,
		Signature = 1 << 2
	}

	/// <summary>alpm_pkg_validation flags.</summary>
	public static class PackageValidationExt
	{
		public const PackageValidation ALL =
			PackageValidation.Md5Sum | PackageValidation.Sha256Sum | PackageValidation.Signature;
	}

	public class Depend
	{
		public string Name { get; set; } = string.Empty;
		public string Version { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;

		public string ComputeString()
		{
			var sb = new System.Text.StringBuilder(Name);
			if (!string.IsNullOrEmpty(Description)) sb.Append(Description);
			if (!string.IsNullOrEmpty(Version)) sb.Append(Version);
			return sb.ToString();
		}

		public static Depend FromString(string str) => new Depend { Name = str };
	}

	public class Group
	{
		public string Name { get; set; } = string.Empty;
		public List<Package> Packages { get; } = new List<Package>();
	}

	public class FileEntry
	{
		public string Name { get; set; } = string.Empty;
	}

	public class FileList
	{
		public List<FileEntry> Files { get; } = new List<FileEntry>();
		public int Count => Files.Count;
	}

	public enum Usage
	{
		Sync = 1 << 0,
		Search = 1 << 1,
		Install = 1 << 2,
		Upgrade = 1 << 3,
		All = Sync | Search | Install | Upgrade
	}

	public static class Signature
	{
		[Flags]
		public enum Level
		{
			UseDefault = 0,
			Package = 1 << 0,
			PackageOptional = 1 << 1,
			PackageMarginalOk = 1 << 2,
			PackageUnknownOk = 1 << 3,
			Database = 1 << 4,
			DatabaseOptional = 1 << 5,
			DatabaseMarginalOk = 1 << 6,
			DatabaseUnknownOk = 1 << 7
		}
	}

	/// <summary>A registered database (sync or local).</summary>
	public class DB
	{
		public string Name { get; set; } = string.Empty;
		public Usage Usage { get; set; } = Usage.All;
		public List<string> Servers { get; } = new List<string>();
		public List<Package> PkgCache { get; } = new List<Package>();
		public List<Group> GroupCache { get; } = new List<Group>();

		public Package? GetPkg(string pkgname) =>
			PkgCache.Find(p => string.Equals(p.Name, pkgname, StringComparison.Ordinal));

		public Group? GetGroup(string groupname) =>
			GroupCache.Find(g => string.Equals(g.Name, groupname, StringComparison.Ordinal));

		public void AddServer(string url) => Servers.Add(url);
	}

	/// <summary>A package known to alpm.</summary>
	public class Package
	{
		public string Name { get; set; } = string.Empty;
		public string Version { get; set; } = string.Empty;
		public string? Desc { get; set; }
		public string? Url { get; set; }
		public string? Packager { get; set; }
		public ulong ISize { get; set; }
		public ulong DownloadSize { get; set; }
		public long InstallDate { get; set; }
		public long BuildDate { get; set; }
		public PackageReason Reason { get; set; } = PackageReason.Explicit;
		public PackageValidation Validation { get; set; } = PackageValidation.None;
		public PackageFrom Origin { get; set; } = PackageFrom.LocalDb;
		public DB? DB { get; set; }
		public string? Root { get; set; }
		public string PkgBase { get; set; } = string.Empty;

		public List<string> Licenses { get; } = new List<string>();
		public List<string> Groups { get; } = new List<string>();
		public List<Depend> Depends { get; } = new List<Depend>();
		public List<Depend> OptDepends { get; } = new List<Depend>();
		public List<Depend> MakeDepends { get; } = new List<Depend>();
		public List<Depend> CheckDepends { get; } = new List<Depend>();
		public List<Depend> Provides { get; } = new List<Depend>();
		public List<Depend> Replaces { get; } = new List<Depend>();
		public List<Depend> Conflicts { get; } = new List<Depend>();
		public List<Depend> Backups { get; } = new List<Depend>();
		public FileList Files { get; } = new FileList();

		public bool ProvidesSatisfies(string depstring)
		{
			foreach (var p in Provides)
			{
				if (string.Equals(p.Name, depstring, StringComparison.Ordinal)) return true;
			}
			return false;
		}

		public List<string> ComputeRequiredBy() => new List<string>();
		public List<string> ComputeOptionalFor() => new List<string>();

		/// <summary>alpm_pkg_get_new_version: look up a newer version in the given sync db list.</summary>
		public Package? GetNewVersion(List<DB> syncdbs)
		{
			foreach (var db in syncdbs)
			{
				var pkg = db.GetPkg(Name);
				if (pkg != null)
				{
					if (Alpm.PkgVerCmp(pkg.Version, Version) > 0) return pkg;
					return null; // same version in a sync db
				}
			}
			return null;
		}
	}

	/// <summary>Transaction flags (subset of alpm_transflag_t).</summary>
	[Flags]
	public enum TransFlag
	{
		None = 0,
		NoDepVersion = 1 << 0,
		NoSave = 1 << 1,
		NoDeps = 1 << 2,
		Recurse = 1 << 3,
		DbOnly = 1 << 4,
		NoLock = 1 << 5,
		Cascade = 1 << 6,
		DownloadOnly = 1 << 7,
		NoCache = 1 << 8,
		NoScriptlets = 1 << 9,
		NoArchive = 1 << 10,
		Unneeded = 1 << 11,
		Reinstall = 1 << 12
	}

	/// <summary>A missing-dependency entry returned by trans_prepare.</summary>
	public class DepMissing
	{
		public string Target { get; set; } = string.Empty;
		public string? CausingPkg { get; set; }
		public Depend Depend { get; set; } = new Depend();
	}

	/// <summary>A package conflict returned by trans_prepare.</summary>
	public class Conflict
	{
		public Package Package1 { get; set; } = new Package();
		public Package Package2 { get; set; } = new Package();
		public Depend Reason { get; set; } = new Depend();
	}

	/// <summary>A file conflict returned by trans_commit.</summary>
	public class FileConflict
	{
		public enum ConflictType
		{
			Target,
			FileSystem
		}

		public ConflictType Type { get; set; }
		public string File { get; set; } = string.Empty;
		public string Target { get; set; } = string.Empty;
		public string CTarget { get; set; } = string.Empty;
	}

	/// <summary>alpm_handle: an initialized libalpm session.</summary>
	public class Handle
	{
		public List<Package> _transToAdd = new List<Package>();
		public List<Package> _transToRemove = new List<Package>();
		public List<string> _overwriteFiles = new List<string>();
		public Errno LastError { get; set; } = Errno.Ok;
		public uint ParallelDownloads { get; set; } = 1;
		public string Lockfile => string.IsNullOrEmpty(DbPath)
			? "/var/lib/pacman/db.lck"
			: DbPath.TrimEnd('/') + "/db.lck";

		public string RootDir { get; set; } = "/";
		public string? DbPath { get; set; }
		public string? LogFile { get; set; }
		public string? GpgDir { get; set; }
		public int Usesyslog { get; set; }
		public int Checkspace { get; set; }
		public string Dbext { get; set; } = ".db";
		public Signature.Level DefaultSigLevel { get; set; }
		public Signature.Level LocalFileSigLevel { get; set; }
		public Signature.Level RemoteFileSigLevel { get; set; }
		public string SandboxUser { get; set; } = "alpm";
		public int DisableSandboxFilesystem { get; set; }
		public int DisableSandboxSyscalls { get; set; }

		public List<string> Architectures { get; } = new List<string>();
		public List<string> CacheDirs { get; } = new List<string>();
		public List<string> HookDirs { get; } = new List<string>();
		public List<string> IgnoreGroups { get; } = new List<string>();
		public List<string> NoExtracts { get; } = new List<string>();
		public List<string> NoUpgrades { get; } = new List<string>();
		public List<string> IgnorePkgs { get; } = new List<string>();

		public List<DB> SyncDbs { get; } = new List<DB>();
		public DB LocalDb { get; set; } = new DB { Name = "local" };

		public void AddArchitecture(string arch) => Architectures.Add(arch);
		public void AddCachedir(string dir) => CacheDirs.Add(dir);
		public void AddHookdir(string dir) => HookDirs.Add(dir);
		public void AddIgnoregroup(string group) => IgnoreGroups.Add(group);
		public void AddNoextract(string path) => NoExtracts.Add(path);
		public void AddNoupgrade(string path) => NoUpgrades.Add(path);
		public void AddIgnorepkg(string name) { if (!IgnorePkgs.Contains(name)) IgnorePkgs.Add(name); }
		public void RemoveIgnorepkg(string name) => IgnorePkgs.Remove(name);

		public DB RegisterSyncDb(string name, Signature.Level level)
		{
			var db = new DB { Name = name };
			SyncDbs.Add(db);
			return db;
		}

		/// <summary>alpm_should_ignore: whether a package (or replacer) is ignored.</summary>
		public int ShouldIgnore(Package pkg)
		{
			if (IgnorePkgs.Contains(pkg.Name)) return 1;
			return 0;
		}

		public int UpdateDbs(List<DB> dbs, int force) => 0;

		public void SetEventCb(EventHandler callback) { }
		public delegate void EventHandler(object sender, string details);

		// ---- transaction API ----
		public int TransInit(int flags)
		{
			_transToAdd.Clear();
			_transToRemove.Clear();
			LastError = Errno.Ok;
			return 0;
		}

		public int TransSysUpgrade(int flags) => 0;

		public int TransPrepare(out List<object>? errData)
		{
			errData = null;
			return 0;
		}

		public int TransCommit(out List<object>? errData)
		{
			errData = null;
			return 0;
		}

		public int TransAddPkg(Package pkg)
		{
			if (_transToAdd.Contains(pkg)) { LastError = Errno.TransDupTarget; return -1; }
			_transToAdd.Add(pkg);
			return 0;
		}

		public int TransRemovePkg(Package pkg)
		{
			if (_transToRemove.Contains(pkg)) { LastError = Errno.TransDupTarget; return -1; }
			_transToRemove.Add(pkg);
			return 0;
		}

		public void TransRelease()
		{
			_transToAdd.Clear();
			_transToRemove.Clear();
			LastError = Errno.Ok;
		}

		public List<Package>? TransToAdd() => _transToAdd;
		public List<Package>? TransToRemove() => _transToRemove;

		public Package? FindDbsSatisfier(List<DB> dbs, string depstring)
		{
			foreach (var db in dbs)
			{
				var p = Alpm.FindSatisfier(db.PkgCache, depstring);
				if (p != null) return p;
			}
			LastError = Errno.PkgNotFound;
			return null;
		}

		public Errno Errno() => LastError;

		public int FetchPkgUrl(List<string> urls, out List<string> fetched)
		{
			fetched = new List<string>(urls);
			return 0;
		}

		public Package LoadTarball(string path)
		{
			var name = System.IO.Path.GetFileNameWithoutExtension(path);
			return new Package { Name = name, Origin = PackageFrom.File };
		}

		public void AddOverwriteFile(string name) => _overwriteFiles.Add(name);
		public void RemoveOverwriteFile(string name) => _overwriteFiles.Remove(name);
	}

	public class EventCbArgs
	{
		public uint PrimaryEvent { get; set; }
		public uint SecondaryEvent { get; set; }
		public List<string> Details { get; } = new List<string>();
	}

	public class ProgressCbArgs
	{
		public string PackageName { get; set; } = string.Empty;
		public uint Percent { get; set; }
		public uint NTargets { get; set; }
		public uint CurrentTarget { get; set; }
	}
}
