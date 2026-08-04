// libpamac — C# port
//
// Config: pamac.conf handling, plugin loading, and interaction with the
// system daemon to persist settings (save()).
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using Pamac.Compat;
using static Pamac.Compat.Log;

namespace Pamac
{
	public class Config : GObject
	{
		readonly Dictionary<string, string> _environmentVariablesPriv = new Dictionary<string, string>();
		IDaemon? _systemDaemon;

		bool _supportAur;
		bool _supportAppstream;
		bool _supportSnap;
		bool _supportFlatpak;
		bool _enableAur;
		bool _enableAppstream;
		bool _enableSnap;
		bool _enableFlatpak;
		bool _checkAurUpdates;
		bool _downloadUpdates;

		internal AlpmConfig AlpmConfig { get; private set; } = null!;

		PluginLoader<IAurPlugin> _aurPluginLoader = null!;
		PluginLoader<IAppstreamPlugin> _appstreamPluginLoader = null!;
		PluginLoader<ISnapPlugin> _snapPluginLoader = null!;
		PluginLoader<IFlatpakPlugin> _flatpakPluginLoader = null!;

		public string ConfPath { get; }
		public bool Recurse { get; set; }
		public bool KeepBuiltPkgs { get; set; }
		public bool EnableDowngrade { get; set; }
		public bool SimpleInstall { get; set; }
		public ulong RefreshPeriod { get; set; }
		public bool NoUpdateHideIcon { get; set; }

		public bool SupportAur
		{
			get => _supportAur;
			private set
			{
				_supportAur = value;
				if (!_supportAur) EnableAur = false;
			}
		}

		public bool EnableAur
		{
			get => _enableAur;
			set
			{
				_enableAur = _supportAur && value;
				if (!_enableAur) CheckAurUpdates = false;
			}
		}

		public bool SupportAppstream
		{
			get => _supportAppstream;
			private set
			{
				_supportAppstream = value;
				if (!_supportAppstream) EnableAppstream = false;
			}
		}

		public bool EnableAppstream
		{
			get => _enableAppstream;
			set => _enableAppstream = _supportAppstream && value;
		}

		public bool SupportSnap
		{
			get => _supportSnap;
			private set
			{
				_supportSnap = value;
				if (!_supportSnap) EnableSnap = false;
			}
		}

		public bool EnableSnap
		{
			get => _enableSnap;
			set => _enableSnap = _supportSnap && value;
		}

		public bool SupportFlatpak
		{
			get => _supportFlatpak;
			set
			{
				_supportFlatpak = value;
				if (!_supportFlatpak) EnableFlatpak = false;
			}
		}

		public bool EnableFlatpak
		{
			get => _enableFlatpak;
			set
			{
				_enableFlatpak = _supportFlatpak && value;
				if (!_enableFlatpak) CheckFlatpakUpdates = false;
			}
		}

		public bool CheckFlatpakUpdates { get; set; }
		public string AurBuildDir { get; set; } = "/var/tmp";

		public bool CheckAurUpdates
		{
			get => _checkAurUpdates;
			set
			{
				_checkAurUpdates = value;
				if (!_checkAurUpdates) CheckAurVcsUpdates = false;
			}
		}

		public bool CheckAurVcsUpdates { get; set; }

		public bool DownloadUpdates
		{
			get => _downloadUpdates;
			set
			{
				_downloadUpdates = value;
				if (!_downloadUpdates) OfflineUpgrade = false;
			}
		}

		public bool OfflineUpgrade { get; set; }
		public ulong MaxParallelDownloads { get; set; }
		public ulong CleanKeepNumPkgs { get; set; }
		public bool CleanRmOnlyUninstalled { get; set; }

		public IReadOnlyDictionary<string, string> EnvironmentVariables => _environmentVariablesPriv;

		public string DbPath => AlpmConfig.DbPath!;
		public bool CheckSpace { get => AlpmConfig.CheckSpace; set => AlpmConfig.CheckSpace = value; }
		public HashSet<string> IgnorePkgs => AlpmConfig.IgnorePkgs;

		public Config(string confPath)
		{
			ConfPath = confPath;
			Construct();
		}

		void Construct()
		{
			_environmentVariablesPriv.Clear();
			AlpmConfig = new AlpmConfig("/etc/pacman.conf");
			AddEnvironmentVariable("http_proxy");
			AddEnvironmentVariable("https_proxy");
			AddEnvironmentVariable("ftp_proxy");
			AddEnvironmentVariable("socks_proxy");
			AddEnvironmentVariable("no_proxy");
			RefreshPeriod = 6;
			// load aur plugin
			SupportAur = false;
			_aurPluginLoader = new PluginLoader<IAurPlugin>("Pamac.Aur");
			if (_aurPluginLoader.Load()) SupportAur = true;
			// load appstream plugin
			SupportAppstream = false;
			_appstreamPluginLoader = new PluginLoader<IAppstreamPlugin>("Pamac.Appstream");
			if (_appstreamPluginLoader.Load()) SupportAppstream = true;
			// load snap plugin
			SupportSnap = false;
			_snapPluginLoader = new PluginLoader<ISnapPlugin>("Pamac.Snap");
			if (_snapPluginLoader.Load()) SupportSnap = true;
			// load flatpak plugin
			SupportFlatpak = false;
			_flatpakPluginLoader = new PluginLoader<IFlatpakPlugin>("Pamac.Flatpak");
			if (_flatpakPluginLoader.Load()) SupportFlatpak = true;
			Reload();
		}

		void AddEnvironmentVariable(string name)
		{
			string? value = Environment.GetEnvironmentVariable(name);
			if (value != null) _environmentVariablesPriv[name] = value;
		}

		public void AddIgnorepkg(string name) => AlpmConfig.IgnorePkgs.Add(name);

		public void RemoveIgnorepkg(string name) => AlpmConfig.IgnorePkgs.Remove(name);

		public void Reload()
		{
			AlpmConfig.Reload();
			Recurse = false;
			KeepBuiltPkgs = false;
			EnableDowngrade = false;
			SimpleInstall = false;
			RefreshPeriod = 6;
			NoUpdateHideIcon = false;
			EnableAur = false;
			EnableAppstream = true;
			EnableSnap = false;
			EnableFlatpak = false;
			AurBuildDir = "/var/tmp";
			DownloadUpdates = false;
			MaxParallelDownloads = 1;
			CleanKeepNumPkgs = 3;
			CleanRmOnlyUninstalled = false;
			ParseFile(ConfPath);
			if (MaxParallelDownloads > 10) MaxParallelDownloads = 10;
			if (RefreshPeriod > 168) RefreshPeriod = 168;
		}

		internal IAurPlugin? GetAurPlugin() =>
			SupportAur ? _aurPluginLoader.GetPlugin() : null;

		internal IAppstreamPlugin? GetAppstreamPlugin() =>
			SupportAppstream ? _appstreamPluginLoader.GetPlugin() : null;

		internal ISnapPlugin? GetSnapPlugin() =>
			SupportSnap ? _snapPluginLoader.GetPlugin() : null;

		internal IFlatpakPlugin? GetFlatpakPlugin() =>
			SupportFlatpak ? _flatpakPluginLoader.GetPlugin() : null;

		void ParseFile(string path)
		{
			var file = GFile.NewForPath(path);
			if (!file.QueryExists())
			{
				Warning("File '{0}' doesn't exist.", path);
				return;
			}
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
					splitted = line.Split('=', 2);
					string key = splitted[0].Trim();
					switch (key)
					{
						case "RemoveUnrequiredDeps": Recurse = true; break;
						case "EnableDowngrade": EnableDowngrade = true; break;
						case "SimpleInstall": SimpleInstall = true; break;
						case "RefreshPeriod":
							if (splitted.Length == 2) RefreshPeriod = ulong.Parse(splitted[1].Trim());
							break;
						case "KeepNumPackages":
							if (splitted.Length == 2) CleanKeepNumPkgs = ulong.Parse(splitted[1].Trim());
							break;
						case "OnlyRmUninstalled": CleanRmOnlyUninstalled = true; break;
						case "NoUpdateHideIcon": NoUpdateHideIcon = true; break;
						case "EnableAUR": EnableAur = true; break;
						case "KeepBuiltPkgs": KeepBuiltPkgs = true; break;
						case "EnableSnap": EnableSnap = true; break;
						case "EnableFlatpak": EnableFlatpak = true; break;
						case "CheckFlatpakUpdates": CheckFlatpakUpdates = true; break;
						case "BuildDirectory":
							if (splitted.Length == 2) AurBuildDir = splitted[1].Trim();
							break;
						case "CheckAURUpdates": CheckAurUpdates = true; break;
						case "CheckAURVCSUpdates": CheckAurVcsUpdates = true; break;
						case "DownloadUpdates": DownloadUpdates = true; break;
						case "OfflineUpgrade": OfflineUpgrade = true; break;
						case "MaxParallelDownloads":
							if (splitted.Length == 2) MaxParallelDownloads = ulong.Parse(splitted[1].Trim());
							break;
					}
				}
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
		}

		/// <summary>Write settings back through the system daemon.</summary>
		public void Save()
		{
			if (_systemDaemon == null)
			{
				_systemDaemon = ConnectSystemDaemon();
				if (_systemDaemon == null)
				{
					Warning("save pamac config error: cannot connect to daemon");
					return;
				}
			}
			var newPamacConf = new Dictionary<string, object>
			{
				["RemoveUnrequiredDeps"] = Recurse,
				["RefreshPeriod"] = RefreshPeriod,
				["NoUpdateHideIcon"] = NoUpdateHideIcon,
				["DownloadUpdates"] = DownloadUpdates,
				["OfflineUpgrade"] = OfflineUpgrade,
				["EnableDowngrade"] = EnableDowngrade,
				["SimpleInstall"] = SimpleInstall,
				["MaxParallelDownloads"] = MaxParallelDownloads,
				["KeepNumPackages"] = CleanKeepNumPkgs,
				["OnlyRmUninstalled"] = CleanRmOnlyUninstalled,
				["EnableAUR"] = EnableAur,
				["KeepBuiltPkgs"] = KeepBuiltPkgs,
				["CheckAURUpdates"] = CheckAurUpdates,
				["CheckAURVCSUpdates"] = CheckAurVcsUpdates,
				["BuildDirectory"] = AurBuildDir,
				["EnableSnap"] = EnableSnap,
				["EnableFlatpak"] = EnableFlatpak,
				["CheckFlatpakUpdates"] = CheckFlatpakUpdates
			};
			try
			{
				_systemDaemon.StartWritePamacConfig(newPamacConf);
			}
			catch (Exception e)
			{
				Warning("save pamac config error: {0}", e.Message);
			}

			var newAlpmConf = new Dictionary<string, object>
			{
				["CheckSpace"] = CheckSpace,
				["IgnorePkg"] = string.Join(" ", IgnorePkgs)
			};
			try
			{
				_systemDaemon.StartWriteAlpmConfig(newAlpmConf);
			}
			catch (Exception e)
			{
				Warning("save pamac config error: {0}", e.Message);
			}
		}

		static IDaemon? ConnectSystemDaemon()
		{
			// Connected by the daemon project's DBus bridge in a real build.
			return null;
		}
	}
}
