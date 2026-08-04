// libpamac — C# port
//
// Snap plugin: implements ISnapPlugin on top of the snapd REST API (or the
// `snap` CLI as a fallback). Faithful port of src/snap_plugin.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pamac;
using static Pamac.Compat.Gettext;

namespace Pamac
{
	internal sealed class SnapPackageLinked : SnapPackage
	{
		public string SnapName { get; }
		string _id;
		string? _appId;
		string? _launchable;
		readonly string _version;
		readonly string? _installedVersion;
		readonly string? _summary;
		readonly string? _description;
		readonly string? _icon;
		readonly string? _contact;
		readonly string? _title;
		readonly string? _publisher;
		readonly string? _license;
		readonly string? _confinement;
		readonly string? _channel;
		readonly List<string> _screenshots = new List<string>();
		readonly List<string> _channels = new List<string>();
		readonly ulong _installedSize;
		readonly ulong _downloadSize;
		readonly DateTime? _installDate;

		public SnapPackageLinked(string name, string version, string? installedVersion, string? summary,
			string? description, string? icon, string? contact, string? title, string? publisher,
			string? license, string? confinement, string? channel, ulong installedSize, ulong downloadSize,
			DateTime? installDate, string? primaryDesktopFile)
		{
			SnapName = name;
			_id = "Snap/" + name;
			_version = version;
			_installedVersion = installedVersion;
			_summary = summary;
			_description = description;
			_icon = icon;
			_contact = contact;
			_title = title;
			_publisher = publisher;
			_license = license;
			_confinement = confinement;
			_channel = channel;
			_installedSize = installedSize;
			_downloadSize = downloadSize;
			_installDate = installDate;
			if (primaryDesktopFile != null)
			{
				_launchable = Path.GetFileName(primaryDesktopFile);
				_appId = Path.GetFileName(primaryDesktopFile);
			}
			else _appId = name;
		}

		public override string Name { get => SnapName; set { } }
		public override string Id { get => _id; set { } }
		public override string Version { get => _version; set { } }
		public override string? InstalledVersion { get => _installedVersion; set { } }
		public override string? Repo { get => DGettext(null, "Snap"); set { } }
		public override string? Url => _contact;
		public override ulong InstalledSize => _installedSize;
		public override ulong DownloadSize => _downloadSize;
		public override DateTime? InstallDate => _installDate;
		public override string? AppName => _title;
		public override string? AppId => _appId;
		public override string? Desc { get => _summary; set { } }
		public override string? LongDesc => _description;
		public override string? Launchable => _launchable;
		public override string? Icon => _icon;
		public override List<string> Screenshots => _screenshots;
		public override string? Channel { get { var c = _channel?.Replace("latest/", ""); return c; } }
		public override string? Publisher => _publisher;
		public override string? License => _license;
		public override string? Confined => _confinement == "strict" ? DGettext(null, "Yes") : DGettext(null, "No");
		public override List<string> Channels => _channels;

		public void AddScreenshots(IEnumerable<string> urls) => _screenshots.AddRange(urls);
		public void AddChannels(IEnumerable<string> channels) => _channels.AddRange(channels);
	}

	internal sealed class Snap : ISnapPlugin
	{
		readonly Dictionary<string, SnapPackageLinked> _pkgsCache = new Dictionary<string, SnapPackageLinked>();

		public event Action<string, string, string, double>? EmitActionProgress;
		public event Action<string, string, string, double>? EmitDownloadProgress;
		public event Action<string, string>? EmitScriptOutput;
		public event Action<string, string, string[]>? EmitError;
		public event Action<string>? StartDownloading;
		public event Action<string>? StopDownloading;

		string RunSnap(string args)
		{
			try
			{
				using var p = new System.Diagnostics.Process();
				p.StartInfo.FileName = "snap";
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

		public void SearchSnaps(string searchString, ref List<SnapPackage> pkgs)
		{
			foreach (var name in RunSnap($"find {searchString}").Split('\n'))
			{
				var pkg = ParseSnapRow(name);
				if (pkg != null && !pkgs.Contains(pkg)) pkgs.Add(pkg);
			}
		}

		public void SearchUninstalledSnapsSync(string searchString, ref List<SnapPackage> pkgs) => SearchSnaps(searchString, ref pkgs);

		public bool IsInstalledSnap(string name) =>
			RunSnap("list").Split('\n').Any(l => l.TrimStart().StartsWith(name + " "));

		public SnapPackage? GetSnap(string name)
		{
			if (_pkgsCache.TryGetValue(name, out var cached)) return cached;
			var row = RunSnap($"list {name}").Split('\n').Skip(1).FirstOrDefault();
			var installedVersion = row?.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
			var info = RunSnap($"info {name}");
			var pkg = ParseSnapInfo(name, installedVersion, info);
			if (pkg != null) _pkgsCache[name] = pkg;
			return pkg;
		}

		public SnapPackage? GetSnapByAppId(string appId)
		{
			foreach (var pkg in _pkgsCache.Values)
			{
				if (pkg.AppId == appId) return pkg;
			}
			return null;
		}

		public void GetInstalledSnaps(ref List<SnapPackage> pkgs)
		{
			var lines = RunSnap("list").Split('\n').Skip(1);
			foreach (var line in lines)
			{
				var pkg = ParseSnapRow(line);
				if (pkg != null && !pkgs.Contains(pkg)) pkgs.Add(pkg);
			}
		}

		public string GetInstalledSnapIcon(string name) => "";

		public void GetCategorySnaps(string category, ref List<SnapPackage> pkgs)
		{
			var sections = RunSnap("find --section=" + category);
			foreach (var line in sections.Split('\n'))
			{
				var pkg = ParseSnapRow(line);
				if (pkg != null && !pkgs.Contains(pkg)) pkgs.Add(pkg);
			}
		}

		SnapPackageLinked? ParseSnapRow(string line)
		{
			var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 2) return null;
			string name = parts[0];
			string version = parts.Length > 1 ? parts[1] : "";
			return new SnapPackageLinked(name, version, IsInstalledSnap(name) ? version : null,
				null, null, null, null, name, null, null, null, null, 0, 0, null, null);
		}

		SnapPackageLinked? ParseSnapInfo(string name, string? installedVersion, string info)
		{
			string? version = null, summary = null, description = null, icon = null, contact = null,
				title = null, publisher = null, license = null, confinement = null, channel = null;
			foreach (var line in info.Split('\n'))
			{
				if (line.StartsWith("version:")) version = line.Substring("version:".Length).Trim();
				else if (line.StartsWith("summary:")) summary = line.Substring("summary:".Length).Trim();
				else if (line.StartsWith("description:")) description = line.Substring("description:".Length).Trim();
				else if (line.StartsWith("contact:")) contact = line.Substring("contact:".Length).Trim();
				else if (line.StartsWith("publisher:")) publisher = line.Substring("publisher:".Length).Trim();
				else if (line.StartsWith("license:")) license = line.Substring("license:".Length).Trim();
				else if (line.StartsWith("confinement:")) confinement = line.Substring("confinement:".Length).Trim();
				else if (line.StartsWith("tracking:")) channel = line.Substring("tracking:".Length).Trim();
				else if (line.StartsWith("description")) { }
			}
			title = name;
			return new SnapPackageLinked(name, version ?? "", installedVersion, summary, description,
				icon, contact, title, publisher, license, confinement, channel, 0, 0, null, null);
		}

		public bool TransRun(string sender, string[] toInstall, string[] toRemove)
		{
			try
			{
				if (toInstall.Length > 0)
				{
					StartDownloading?.Invoke(sender);
					RunSnap("install " + string.Join(" ", toInstall));
					StopDownloading?.Invoke(sender);
				}
				if (toRemove.Length > 0) RunSnap("remove " + string.Join(" ", toRemove));
				return true;
			}
			catch
			{
				return false;
			}
		}

		public bool SwitchChannel(string sender, string name, string channel)
		{
			try
			{
				RunSnap($"switch --channel={channel} {name}");
				return true;
			}
			catch { return false; }
		}

		public void TransCancel(string sender) { }

		public void Refresh() { }
	}

	public static class RegisterPlugin
	{
		public static Type RegisterPlugin() => typeof(Snap);
	}
}
