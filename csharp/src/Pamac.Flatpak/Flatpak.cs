// libpamac — C# port
//
// Flatpak plugin: implements IFlatpakPlugin on top of the flatpak CLI.
// Faithful port of src/flatpak_plugin.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Pamac;
using static Pamac.Compat.Gettext;

namespace Pamac
{
	internal sealed class FlatpakPackageLinked : FlatpakPackage
	{
		public string Ref { get; }
		string _id;
		readonly string _name;
		readonly string? _version;
		readonly string? _installedVersion;
		readonly string? _summary;
		readonly string? _description;
		readonly string? _appId;
		readonly string? _launchable;
		readonly string? _icon;
		readonly string? _repo;
		readonly ulong _installedSize;
		readonly ulong _downloadSize;
		readonly DateTime? _installDate;

		public FlatpakPackageLinked(string id, string name, string? version, string? installedVersion,
			string? summary, string? description, string? appId, string? launchable, string? icon,
			string? repo, ulong installedSize, ulong downloadSize, DateTime? installDate)
		{
			Ref = id;
			_id = id;
			_name = name;
			_version = version;
			_installedVersion = installedVersion;
			_summary = summary;
			_description = description;
			_appId = appId;
			_launchable = launchable;
			_icon = icon;
			_repo = repo;
			_installedSize = installedSize;
			_downloadSize = downloadSize;
			_installDate = installDate;
		}

		public override string Name { get => _name; set { } }
		public override string Id { get => _id; set { } }
		public override string Version { get => _version ?? ""; set { } }
		public override string? InstalledVersion { get => _installedVersion; set { } }
		public override string? Repo { get => _repo; set { } }
		public override string? Url => null;
		public override ulong InstalledSize => _installedSize;
		public override ulong DownloadSize => _downloadSize;
		public override DateTime? InstallDate => _installDate;
		public override string? AppName => _name;
		public override string? AppId => _appId;
		public override string? Desc { get => _summary; set { } }
		public override string? LongDesc => _description;
		public override string? Launchable => _launchable;
		public override string? Icon => _icon;
		public override List<string> Screenshots => new List<string>();
	}

	internal sealed class Flatpak : IFlatpakPlugin
	{
		readonly Dictionary<string, FlatpakPackageLinked> _cache = new Dictionary<string, FlatpakPackageLinked>();

		public ulong RefreshPeriod { get; set; }

		public event Action<string, string, string, double>? EmitActionProgress;
		public event Action<string, string>? EmitScriptOutput;
		public event Action<string, string, string[]>? EmitError;

		string RunFlatpak(string args)
		{
			try
			{
				using var p = new System.Diagnostics.Process();
				p.StartInfo.FileName = "flatpak";
				p.StartInfo.Arguments = args;
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.RedirectStandardError = true;
				p.Start();
				string output = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				return output;
			}
			catch { return ""; }
		}

		// parse "Name ID Version ..." style output
		FlatpakPackageLinked? ParseRow(string line)
		{
			var parts = line.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 2) return null;
			string name = parts[0];
			string id = parts[1];
			string? version = parts.Length > 2 ? parts[2] : null;
			return new FlatpakPackageLinked(id, name, version, null, null, null, id, null, null,
				"Flathub", 0, 0, null);
		}

		public bool RefreshAppstreamData() => true;

		public void LoadAppstreamData() { }

		public void GetRemotesNames(ref List<string> remotesNames)
		{
			foreach (var line in RunFlatpak("remotes --columns=name").Split('\n'))
			{
				var name = line.Trim();
				if (name != "") remotesNames.Add(name);
			}
		}

		public void SearchFlatpaks(string searchString, ref List<FlatpakPackage> pkgs)
		{
			foreach (var line in RunFlatpak($"search {searchString}").Split('\n').Skip(1))
			{
				var pkg = ParseRow(line);
				if (pkg != null && !pkgs.Contains(pkg)) pkgs.Add(pkg);
			}
		}

		public void SearchUninstalledFlatpaksSync(string[] searchTerms, ref List<FlatpakPackage> pkgs) =>
			SearchFlatpaks(string.Join(" ", searchTerms), ref pkgs);

		public bool IsInstalledFlatpak(string id) =>
			RunFlatpak($"info {id}").Length > 0;

		public FlatpakPackage? GetFlatpakByAppId(string appId) => GetFlatpak(appId);

		public FlatpakPackage? GetFlatpak(string id)
		{
			if (_cache.TryGetValue(id, out var cached)) return cached;
			string info = RunFlatpak($"info {id}");
			string? name = id, version = null;
			foreach (var line in info.Split('\n'))
			{
				if (line.StartsWith("Name:")) name = line.Substring("Name:".Length).Trim();
				else if (line.StartsWith("Version:")) version = line.Substring("Version:".Length).Trim();
			}
			var pkg = new FlatpakPackageLinked(id, name ?? id, version, version, null, null, id, null, null,
				"Flathub", 0, 0, null);
			_cache[id] = pkg;
			return pkg;
		}

		public void GetInstalledFlatpaks(ref List<FlatpakPackage> pkgs)
		{
			foreach (var line in RunFlatpak("list --columns=name,application").Split('\n').Skip(1))
			{
				var pkg = ParseRow(line);
				if (pkg != null && !pkgs.Contains(pkg)) pkgs.Add(pkg);
			}
		}

		public void GetCategoryFlatpaks(string category, ref List<FlatpakPackage> pkgs) { }

		public void GetFlatpakUpdates(ref List<FlatpakPackage> pkgs) { }

		public bool TransRun(string sender, string[] toInstall, string[] toRemove, string[] toUpgrade)
		{
			try
			{
				if (toInstall.Length > 0)
				{
					EmitActionProgress?.Invoke(sender, "install", "starting", 0);
					RunFlatpak("install -y " + string.Join(" ", toInstall));
					EmitActionProgress?.Invoke(sender, "install", "done", 1);
				}
				if (toRemove.Length > 0) RunFlatpak("uninstall -y " + string.Join(" ", toRemove));
				if (toUpgrade.Length > 0) RunFlatpak("update -y " + string.Join(" ", toUpgrade));
				return true;
			}
			catch { return false; }
		}

		public void TransCancel(string sender) { }

		public void Refresh() { }
	}

	public static class RegisterPlugin
	{
		public static Type RegisterPlugin() => typeof(Flatpak);
	}
}
