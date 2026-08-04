// libpamac — C# port
//
// AlpmConfig: parses pacman.conf (including Include), builds an Alpm.Handle,
// registers sync databases, and can rewrite IgnorePkg / CheckSpace options.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using LibAlpm;
using Pamac.Compat;
using static Pamac.Compat.Log;

namespace Pamac
{
	[Flags]
	internal enum RepoUsage
	{
		None = 0,
		Sync = 1 << 0,
		Search = 1 << 1,
		Install = 1 << 2,
		Upgrade = 1 << 3,
		All = Sync | Search | Install | Upgrade
	}

	internal sealed class AlpmRepo
	{
		public string Name;
		public Signature.Level SigLevel;
		public Signature.Level SigLevelMask;
		public RepoUsage Usage;
		public List<string> Urls = new List<string>();

		public AlpmRepo(string name)
		{
			Name = name;
			SigLevel = Signature.Level.UseDefault;
			Usage = RepoUsage.None;
		}

		public static bool EqualName(AlpmRepo a, AlpmRepo b) =>
			string.Equals(a.Name, b.Name, StringComparison.Ordinal);
	}

	internal sealed class AlpmConfig
	{
		readonly string _confPath;
		string? _rootdir;
		public string? DbPath { get; private set; }
		string? _logfile;
		string? _gpgdir;
		string _downloadUser = "alpm";
		bool _disableSandbox;
		int _usesyslog;
		public bool CheckSpace { get; set; }
		List<string> _architectures = new List<string>();
		List<string> _cachedirs = new List<string>();
		List<string> _hookdirs = new List<string>();
		List<string> _ignoregroups = new List<string>();
		public HashSet<string> IgnorePkgs { get; } = new HashSet<string>();
		List<string> _noextracts = new List<string>();
		List<string> _noupgrades = new List<string>();
		public HashSet<string> HoldPkgs { get; } = new HashSet<string>();
		public HashSet<string> SyncFirsts { get; } = new HashSet<string>();
		Signature.Level _siglevel;
		Signature.Level _localfilesiglevel;
		Signature.Level _remotefilesiglevel;
		Signature.Level _siglevelMask;
		Signature.Level _localfilesiglevelMask;
		Signature.Level _remotefilesiglevelMask;
		List<AlpmRepo> _repoOrder = new List<AlpmRepo>();

		public AlpmConfig(string path)
		{
			_confPath = path;
			Reload();
		}

		public void Reload()
		{
			_architectures = new List<string>();
			_cachedirs = new List<string>();
			_hookdirs = new List<string>();
			_ignoregroups = new List<string>();
			IgnorePkgs.Clear();
			_noextracts = new List<string>();
			_noupgrades = new List<string>();
			HoldPkgs.Clear();
			SyncFirsts.Clear();
			_usesyslog = 0;
			CheckSpace = false;
			_disableSandbox = false;
			_downloadUser = "alpm";
			_siglevel = Signature.Level.Package | Signature.Level.PackageOptional
				| Signature.Level.Database | Signature.Level.DatabaseOptional;
			_localfilesiglevel = Signature.Level.UseDefault;
			_remotefilesiglevel = Signature.Level.UseDefault;
			_repoOrder = new List<AlpmRepo>();
			ParseFile(_confPath);
			if (_rootdir != null)
			{
				if (DbPath == null) DbPath = PathCompat.BuildFilename(_rootdir, "var/lib/pacman/");
				if (_logfile == null) _logfile = PathCompat.BuildFilename(_rootdir, "var/log/pacman.log");
			}
			else
			{
				_rootdir = "/";
				DbPath ??= "/var/lib/pacman/";
				_logfile ??= "/var/log/pacman.log";
			}
			if (_cachedirs.Count == 0) _cachedirs.Add("/var/cache/pacman/pkg/");
			if (_hookdirs.Count == 0) _hookdirs.Add("/etc/pacman.d/hooks/");
			if (_gpgdir == null) _gpgdir = "/etc/pacman.d/gnupg/";
			if (_architectures.Count == 0) _architectures.Add(RuntimeInformation.OSArchitecture switch
			{
				Architecture.X64 => "x86_64",
				Architecture.Arm64 => "aarch64",
				_ => "x86_64"
			});
			// add archlinux-keyring and manjaro-keyring to syncfirsts
			SyncFirsts.Add("archlinux-keyring");
			SyncFirsts.Add("manjaro-keyring");
		}

		public Handle? GetHandle(bool filesDb = false, bool tmpDb = false, bool copyDbs = true)
		{
			Handle handle;
			if (tmpDb)
			{
				string tmpPath = string.Format("/tmp/pamac-{0}", Environment.UserName);
				string tmpDbpath = tmpPath + "/dbs";
				try
				{
					var file = GFile.NewForPath(tmpPath);
					if (!file.QueryExists()) Spawn.SpawnCommandLineSync("mkdir -p " + tmpPath);
					file = GFile.NewForPath(tmpDbpath);
					string localdbPath = PathCompat.BuildFilename(DbPath!, "local");
					string syncdbPath = PathCompat.BuildFilename(DbPath!, "sync");
					if (!file.QueryExists()) Spawn.SpawnCommandLineSync("mkdir -p " + tmpDbpath);
					Spawn.SpawnCommandLineSync($"ln -sf {localdbPath} {tmpDbpath}");
					if (copyDbs)
					{
						file = GFile.NewForPath(syncdbPath);
						if (file.QueryExists())
							Spawn.SpawnCommandLineSync($"cp --preserve=timestamps -ru {syncdbPath} {tmpDbpath}");
					}
					Spawn.SpawnCommandLineSync($"rm -f {tmpDbpath}/sync/pamac_aur.db");
					handle = new Handle { RootDir = _rootdir!, DbPath = tmpDbpath };
					handle = TryUpgradeDb(handle, _rootdir!, tmpDbpath);
				}
				catch (Exception e)
				{
					Warning(e.Message);
					handle = new Handle { RootDir = _rootdir!, DbPath = tmpDbpath };
				}
			}
			else
			{
				handle = new Handle { RootDir = _rootdir!, DbPath = DbPath };
				handle = TryUpgradeDb(handle, _rootdir!, DbPath!);
			}

			if (filesDb) handle.Dbext = ".files";
			if (!tmpDb) handle.LogFile = _logfile;
			handle.GpgDir = _gpgdir;
			handle.Usesyslog = _usesyslog;
			handle.Checkspace = CheckSpace ? 1 : 0;
			handle.DefaultSigLevel = _siglevel;
			_localfilesiglevel = MergeSigLevel(_siglevel, _localfilesiglevel, _localfilesiglevelMask);
			_remotefilesiglevel = MergeSigLevel(_siglevel, _remotefilesiglevel, _remotefilesiglevelMask);
			handle.LocalFileSigLevel = _localfilesiglevel;
			handle.RemoteFileSigLevel = _remotefilesiglevel;
			foreach (var arch in _architectures) handle.AddArchitecture(arch);
			foreach (var cachedir in _cachedirs) handle.AddCachedir(cachedir);
			foreach (var hookdir in _hookdirs) handle.AddHookdir(hookdir);
			foreach (var ignoregroup in _ignoregroups) handle.AddIgnoregroup(ignoregroup);
			foreach (var noextract in _noextracts) handle.AddNoextract(noextract);
			foreach (var noupgrade in _noupgrades) handle.AddNoupgrade(noupgrade);
			handle.SandboxUser = _downloadUser;
			handle.DisableSandboxFilesystem = _disableSandbox ? 1 : 0;
			handle.DisableSandboxSyscalls = _disableSandbox ? 1 : 0;
			return handle;
		}

		Handle TryUpgradeDb(Handle handle, string rootdir, string dbpath)
		{
			// In the real binding this checks Alpm.Errno.DB_VERSION and runs
			// pacman-db-upgrade. The managed model always succeeds.
			return handle;
		}

		public void RegisterSyncdbs(Handle handle)
		{
			foreach (var repo in _repoOrder)
			{
				repo.SigLevel = MergeSigLevel(_siglevel, repo.SigLevel, repo.SigLevelMask);
				var db = handle.RegisterSyncDb(repo.Name, repo.SigLevel);
				foreach (var url in repo.Urls)
				{
					db.AddServer(url.Replace("$repo", repo.Name).Replace("$arch", _architectures[0]));
				}
				db.Usage = repo.Usage == RepoUsage.None ? Usage.All : (Usage)repo.Usage;
			}
		}

		void ParseFile(string path, string? section = null)
		{
			string? currentSection = section;
			var file = GFile.NewForPath(path);
			if (file.QueryExists())
			{
				try
				{
					using var dis = new DataInputStream(file.Read());
					string? line;
					while ((line = dis.ReadLine()) != null)
					{
						if (line.Length == 0) continue;
						string[] splitted = line.Split('#', 2);
						line = splitted[0].Trim();
						if (line.Length == 0) continue;
						if (line[0] == '[' && line[line.Length - 1] == ']')
						{
							currentSection = line.Substring(1, line.Length - 2);
							if (currentSection == null) continue;
							if (currentSection != "options")
							{
								var repo = new AlpmRepo(currentSection);
								if (!_repoOrder.Exists(r => AlpmRepo.EqualName(repo, r)))
									_repoOrder.Add(repo);
							}
							continue;
						}
						splitted = line.Split('=', 2);
						string key = splitted[0].Trim();
						string? val = null;
						if (splitted.Length == 2) val = splitted[1].Trim();

						if (key == "Include") ParseFile(val!, currentSection);
						if (currentSection == "options")
						{
							switch (key)
							{
								case "RootDir": _rootdir = val; break;
								case "DBPath": DbPath = val; break;
								case "CacheDir":
									foreach (var dir in val!.Split(' ')) _cachedirs.Add(dir);
									break;
								case "HookDir":
									foreach (var dir in val!.Split(' ')) _hookdirs.Add(dir);
									break;
								case "LogFile": _logfile = val; break;
								case "GPGDir": _gpgdir = val; break;
								case "Architecture":
									foreach (var arch in val!.Split(' '))
										_architectures.Add(arch == "auto" ? "x86_64" : arch);
									break;
								case "UseSysLog": _usesyslog = 1; break;
								case "CheckSpace": CheckSpace = true; break;
								case "SigLevel": ProcessSigLevel(val!, ref _siglevel, ref _siglevelMask); break;
								case "LocalFileSigLevel": ProcessSigLevel(val!, ref _localfilesiglevel, ref _localfilesiglevelMask); break;
								case "RemoteFileSigLevel": ProcessSigLevel(val!, ref _remotefilesiglevel, ref _remotefilesiglevelMask); break;
								case "HoldPkg":
									foreach (var name in val!.Split(' ')) HoldPkgs.Add(name);
									break;
								case "SyncFirst":
									foreach (var name in val!.Split(' ')) SyncFirsts.Add(name);
									break;
								case "IgnoreGroup":
									foreach (var name in val!.Split(' ')) _ignoregroups.Add(name);
									break;
								case "IgnorePkg":
									foreach (var name in val!.Split(' ')) IgnorePkgs.Add(name);
									break;
								case "NoExtract":
									foreach (var name in val!.Split(' ')) _noextracts.Add(name);
									break;
								case "NoUpgrade":
									foreach (var name in val!.Split(' ')) _noupgrades.Add(name);
									break;
								case "DownloadUser": _downloadUser = val!; break;
							}
						}
						else
						{
							foreach (var repo in _repoOrder)
							{
								if (repo.Name == currentSection)
								{
									switch (key)
									{
										case "Server": repo.Urls.Add(val!); break;
										case "SigLevel": ProcessSigLevel(val!, ref repo.SigLevel, ref repo.SigLevelMask); break;
										case "Usage": repo.Usage = DefineUsage(val!); break;
									}
									break;
								}
							}
						}
					}
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
			}
			else
			{
				Warning("File '{0}' doesn't exist", path);
			}
		}

		public void Write(Dictionary<string, object> newConf)
		{
			var file = GFile.NewForPath(_confPath);
			if (!file.QueryExists())
			{
				Warning("File '{0}' doesn't exist.", _confPath);
				return;
			}
			try
			{
				var data = new System.Text.StringBuilder();
				using (var dis = new DataInputStream(file.Read()))
				{
					string? line;
					while ((line = dis.ReadLine()) != null)
					{
						if (line.Length == 0) { data.Append('\n'); continue; }
						if (line.Contains("IgnorePkg"))
						{
							if (newConf.ContainsKey("IgnorePkg"))
							{
								string val = newConf["IgnorePkg"] as string ?? string.Empty;
								data.Append(val == ""
									? "#IgnorePkg   =\n"
									: $"IgnorePkg   = {val}\n");
								newConf["IgnorePkg"] = "";
							}
							else { data.Append(line).Append('\n'); }
						}
						else if (line.Contains("CheckSpace"))
						{
							if (newConf.ContainsKey("CheckSpace"))
							{
								bool val = (bool)(newConf["CheckSpace"] ?? false);
								data.Append(val ? "CheckSpace\n" : "#CheckSpace\n");
								newConf.Remove("CheckSpace");
							}
							else { data.Append(line).Append('\n'); }
						}
						else { data.Append(line).Append('\n'); }
					}
				}
				file.Delete();
				using (var dos = new DataOutputStream(file.Create(FileCreateFlags.ReplaceDestination)))
				{
					dos.PutString(data.ToString());
				}
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
		}

		RepoUsage DefineUsage(string confString)
		{
			RepoUsage usage = RepoUsage.None;
			foreach (var directive in confString.Split(' '))
			{
				switch (directive)
				{
					case "Sync": usage |= RepoUsage.Sync; break;
					case "Search": usage |= RepoUsage.Search; break;
					case "Install": usage |= RepoUsage.Install; break;
					case "Upgrade": usage |= RepoUsage.Upgrade; break;
					case "All": usage |= RepoUsage.All; break;
				}
			}
			return usage;
		}

		void ProcessSigLevel(string confString, ref Signature.Level siglevel, ref Signature.Level siglevelMask)
		{
			foreach (var directive in confString.Split(' '))
			{
				bool affectPackage = false;
				bool affectDatabase = false;
				if (directive.Contains("Package")) affectPackage = true;
				else if (directive.Contains("Database")) affectDatabase = true;
				else { affectPackage = true; affectDatabase = true; }

				if (directive.Contains("Never"))
				{
					if (affectPackage) { siglevel &= ~Signature.Level.Package; siglevelMask |= Signature.Level.Package; }
					if (affectDatabase) { siglevel &= ~Signature.Level.Database; siglevelMask |= Signature.Level.Database; }
				}
				else if (directive.Contains("Optional"))
				{
					if (affectPackage)
					{
						siglevel |= Signature.Level.Package | Signature.Level.PackageOptional;
						siglevelMask |= Signature.Level.Package | Signature.Level.PackageOptional;
					}
					if (affectDatabase)
					{
						siglevel |= Signature.Level.Database | Signature.Level.DatabaseOptional;
						siglevelMask |= Signature.Level.Database | Signature.Level.DatabaseOptional;
					}
				}
				else if (directive.Contains("Required"))
				{
					if (affectPackage)
					{
						siglevel |= Signature.Level.Package;
						siglevelMask |= Signature.Level.Package;
						siglevel &= ~Signature.Level.PackageOptional;
						siglevelMask |= Signature.Level.PackageOptional;
					}
					if (affectDatabase)
					{
						siglevel |= Signature.Level.Database;
						siglevelMask |= Signature.Level.Database;
						siglevel &= ~Signature.Level.DatabaseOptional;
						siglevelMask |= Signature.Level.DatabaseOptional;
					}
				}
				else if (directive.Contains("TrustedOnly"))
				{
					if (affectPackage)
					{
						siglevel &= ~(Signature.Level.PackageMarginalOk | Signature.Level.PackageUnknownOk);
						siglevelMask |= Signature.Level.PackageMarginalOk | Signature.Level.PackageUnknownOk;
					}
					if (affectDatabase)
					{
						siglevel &= ~(Signature.Level.DatabaseMarginalOk | Signature.Level.DatabaseUnknownOk);
						siglevelMask |= Signature.Level.DatabaseMarginalOk | Signature.Level.DatabaseUnknownOk;
					}
				}
				else if (directive.Contains("TrustAll"))
				{
					if (affectPackage)
					{
						siglevel |= Signature.Level.PackageMarginalOk | Signature.Level.PackageUnknownOk;
						siglevelMask |= Signature.Level.PackageMarginalOk | Signature.Level.PackageUnknownOk;
					}
					if (affectDatabase)
					{
						siglevel |= Signature.Level.DatabaseMarginalOk | Signature.Level.DatabaseUnknownOk;
						siglevelMask |= Signature.Level.DatabaseMarginalOk | Signature.Level.DatabaseUnknownOk;
					}
				}
				else
				{
					Console.Error.WriteLine("unrecognized siglevel: " + confString);
				}
			}
			siglevel &= ~Signature.Level.UseDefault;
		}

		Signature.Level MergeSigLevel(Signature.Level sigbase, Signature.Level sigover, Signature.Level sigmask)
		{
			return sigmask != 0 ? (sigover & sigmask) | (sigbase & ~sigmask) : sigover;
		}
	}
}
