// libpamac — C# port
//
// PamacConfigDaemon: applies a pamac.conf settings dictionary written by a
// client through the daemon. Faithful port of src/pamac_config_daemon.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO;
using Pamac.Compat;
using static Pamac.Compat.Log;

namespace Pamac
{
	internal static class PamacConfigDaemon
	{
		/// <summary>Apply <paramref name="newPamacConf"/> onto <paramref name="config"/> and persist it.</summary>
		public static void WriteConfig(Config config, Dictionary<string, object> newPamacConf)
		{
			if (newPamacConf.TryGetValue("RemoveUnrequiredDeps", out var v)) config.Recurse = ToBool(v);
			if (newPamacConf.TryGetValue("RefreshPeriod", out v)) config.RefreshPeriod = ToUlong(v);
			if (newPamacConf.TryGetValue("NoUpdateHideIcon", out v)) config.NoUpdateHideIcon = ToBool(v);
			if (newPamacConf.TryGetValue("DownloadUpdates", out v)) config.DownloadUpdates = ToBool(v);
			if (newPamacConf.TryGetValue("OfflineUpgrade", out v)) config.OfflineUpgrade = ToBool(v);
			if (newPamacConf.TryGetValue("EnableDowngrade", out v)) config.EnableDowngrade = ToBool(v);
			if (newPamacConf.TryGetValue("SimpleInstall", out v)) config.SimpleInstall = ToBool(v);
			if (newPamacConf.TryGetValue("MaxParallelDownloads", out v)) config.MaxParallelDownloads = ToUlong(v);
			if (newPamacConf.TryGetValue("KeepNumPackages", out v)) config.CleanKeepNumPkgs = ToUlong(v);
			if (newPamacConf.TryGetValue("OnlyRmUninstalled", out v)) config.CleanRmOnlyUninstalled = ToBool(v);
			if (newPamacConf.TryGetValue("EnableAUR", out v)) config.EnableAur = ToBool(v);
			if (newPamacConf.TryGetValue("KeepBuiltPkgs", out v)) config.KeepBuiltPkgs = ToBool(v);
			if (newPamacConf.TryGetValue("CheckAURUpdates", out v)) config.CheckAurUpdates = ToBool(v);
			if (newPamacConf.TryGetValue("CheckAURVCSUpdates", out v)) config.CheckAurVcsUpdates = ToBool(v);
			if (newPamacConf.TryGetValue("BuildDirectory", out v) && v is string s) config.AurBuildDir = s;
			if (newPamacConf.TryGetValue("EnableSnap", out v)) config.EnableSnap = ToBool(v);
			if (newPamacConf.TryGetValue("EnableFlatpak", out v)) config.EnableFlatpak = ToBool(v);
			if (newPamacConf.TryGetValue("CheckFlatpakUpdates", out v)) config.CheckFlatpakUpdates = ToBool(v);
			WritePamacConfFile(config.ConfPath, config);
		}

		static bool ToBool(object v) => v is bool b && b;
		static ulong ToUlong(object v) => Convert.ToUInt64(v);

		/// <summary>Rewrite /etc/pamac.conf with the current settings.</summary>
		public static void WritePamacConfFile(string path, Config config)
		{
			var sb = new System.Text.StringBuilder();
			sb.Append("# Remove unrequired dependencies after package removal\n");
			sb.Append(config.Recurse ? "RemoveUnrequiredDeps\n" : "#RemoveUnrequiredDeps\n");
			sb.Append("\n# Enable the downgrade option\n");
			sb.Append(config.EnableDowngrade ? "EnableDowngrade\n" : "#EnableDowngrade\n");
			sb.Append("\n# Simple install (no ask for confirmation)\n");
			sb.Append(config.SimpleInstall ? "SimpleInstall\n" : "#SimpleInstall\n");
			sb.Append("\n# Check updates every X hours (default 6)\n");
			sb.Append($"RefreshPeriod = {config.RefreshPeriod}\n");
			sb.Append("\n# Hide the update icon if no updates are available\n");
			sb.Append(config.NoUpdateHideIcon ? "NoUpdateHideIcon\n" : "#NoUpdateHideIcon\n");
			sb.Append("\n# Download updates in background\n");
			sb.Append(config.DownloadUpdates ? "DownloadUpdates\n" : "#DownloadUpdates\n");
			sb.Append("\n# Upgrade with downloaded updates\n");
			sb.Append(config.OfflineUpgrade ? "OfflineUpgrade\n" : "#OfflineUpgrade\n");
			sb.Append("\n# Enable AUR support\n");
			sb.Append(config.EnableAur ? "EnableAUR\n" : "#EnableAUR\n");
			sb.Append("\n# Keep built packages\n");
			sb.Append(config.KeepBuiltPkgs ? "KeepBuiltPkgs\n" : "#KeepBuiltPkgs\n");
			sb.Append("\n# Check updates from AUR\n");
			sb.Append(config.CheckAurUpdates ? "CheckAURUpdates\n" : "#CheckAURUpdates\n");
			sb.Append("\n# Check AUR development packages updates\n");
			sb.Append(config.CheckAurVcsUpdates ? "CheckAURVCSUpdates\n" : "#CheckAURVCSUpdates\n");
			sb.Append("\n# Build directory for AUR packages\n");
			sb.Append($"BuildDirectory = {config.AurBuildDir}\n");
			sb.Append("\n# Enable Snap support\n");
			sb.Append(config.EnableSnap ? "EnableSnap\n" : "#EnableSnap\n");
			sb.Append("\n# Enable Flatpak support\n");
			sb.Append(config.EnableFlatpak ? "EnableFlatpak\n" : "#EnableFlatpak\n");
			sb.Append("\n# Check updates from Flatpak\n");
			sb.Append(config.CheckFlatpakUpdates ? "CheckFlatpakUpdates\n" : "#CheckFlatpakUpdates\n");
			sb.Append("\n# Maximum number of parallel downloads (default 1)\n");
			sb.Append($"MaxParallelDownloads = {config.MaxParallelDownloads}\n");
			sb.Append("\n# Number of old packages to keep in cache (default 3)\n");
			sb.Append($"KeepNumPackages = {config.CleanKeepNumPkgs}\n");
			sb.Append("\n# Only remove uninstalled packages in cache\n");
			sb.Append(config.CleanRmOnlyUninstalled ? "OnlyRmUninstalled\n" : "#OnlyRmUninstalled\n");
			try
			{
				File.WriteAllText(path, sb.ToString());
			}
			catch (Exception e)
			{
				Warning("write pamac config error: {0}", e.Message);
			}
		}
	}
}
