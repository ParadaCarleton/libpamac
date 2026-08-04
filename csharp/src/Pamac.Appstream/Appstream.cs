// libpamac — C# port
//
// Appstream plugin: loads the per-repo AppStream XML catalogs
// (/usr/share/swcatalog/xml/<repo>.xml.gz), exposes desktop apps for search,
// package-name lookup and category browsing. Faithful port of src/appstream_plugin.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Pamac;

namespace Pamac
{
	internal sealed class AppLinked : App
	{
		readonly XElement? _asApp;
		readonly string? _repo;
		string? _name;
		string? _id;
		string? _pkgname;
		string? _desc;
		string? _longDesc;
		string? _launchable;
		string? _icon;
		List<string> _screenshots = null!;

		public AppLinked(XElement? asApp, string? repo)
		{
			_asApp = asApp;
			_repo = repo;
		}

		string? Child(string element) =>
			_asApp?.Element(element)?.Value?.Trim();

		public override string? Name => _name ??= Child("name") ?? _id;
		public override string? Id => _id ??= _asApp?.Attribute("id")?.Value;
		public override string? Pkgname => _pkgname ??= Child("pkgname");
		public override string? Desc => _desc ??= Child("summary");
		public override string? LongDesc => _longDesc ??= Child("description")?.Trim();
		public override string? Repo => _repo;
		public override string? Launchable => _launchable ??= _asApp?.Element("launchable")?.Attribute("type")?.Value;

		public override string? Icon
		{
			get
			{
				if (_icon == null && _asApp != null)
				{
					var icon = _asApp.Element("icon");
					if (icon?.Attribute("type")?.Value == "stock" && icon.Value != "")
						_icon = $"/usr/share/swcatalog/icons/archlinux-arch-{_repo}/64x64/{icon.Value}";
					else _icon = icon?.Value;
				}
				return _icon;
			}
		}

		public override List<string> Screenshots
		{
			get
			{
				if (_screenshots == null)
				{
					_screenshots = new List<string>();
					if (_asApp != null)
					{
						foreach (var shot in _asApp.Elements("screenshot"))
						{
							foreach (var img in shot.Elements("image"))
							{
								var url = img.Value?.Trim();
								if (!string.IsNullOrEmpty(url)) _screenshots.Add(url);
							}
						}
					}
				}
				return _screenshots;
			}
		}

		public uint SearchMatchesAll(string[] searchTokens)
		{
			uint score = 0;
			if (searchTokens.Length == 0) return 0;
			foreach (var token in searchTokens)
			{
				if (token.Length == 0) continue;
				bool matched = (Name ?? "").ToLowerInvariant().Contains(token.ToLowerInvariant())
					|| (Id ?? "").ToLowerInvariant().Contains(token.ToLowerInvariant())
					|| (Desc ?? "").ToLowerInvariant().Contains(token.ToLowerInvariant())
					|| (LongDesc ?? "").ToLowerInvariant().Contains(token.ToLowerInvariant());
				if (!matched) return 0;
				score++;
			}
			return score;
		}
	}

	internal sealed class Appstream : IAppstreamPlugin
	{
		readonly List<Dictionary<string, App>> _storesTable = new List<Dictionary<string, App>>();
		readonly Dictionary<string, List<App>> _pkgnameAppsCache = new Dictionary<string, List<App>>();
		readonly Dictionary<string, Dictionary<string, App>> _categoriesCache = new Dictionary<string, Dictionary<string, App>>();
		List<string> _reposNames = new List<string>();

		public void Load(List<string> reposNames)
		{
			_reposNames = reposNames;
			_categoriesCache.Clear();
			_pkgnameAppsCache.Clear();
			_storesTable.Clear();
			foreach (var category in new[]
			{
				"Photo & Video", "Music & Audio", "Productivity", "Communication & News",
				"Education & Science", "Games", "Utilities", "Development"
			})
			{
				_categoriesCache[category] = new Dictionary<string, App>();
			}
			foreach (var repo in reposNames)
			{
				try
				{
					var appstreamFile = new Compat.GFile($"/usr/share/swcatalog/xml/{repo}.xml.gz");
					if (!appstreamFile.QueryExists()) continue;
					var desktopApps = new Dictionary<string, App>();
					XDocument doc;
					using (var fs = System.IO.File.OpenRead(appstreamFile.Path))
					using (var gz = new GZipStream(fs, CompressionMode.Decompress))
					{
						doc = XDocument.Load(gz);
					}
					foreach (var comp in doc.Root?.Elements("component") ?? Enumerable.Empty<XElement>())
					{
						if (comp.Attribute("type")?.Value != "desktop-application") continue;
						var newApp = new AppLinked(comp, repo);
						if (desktopApps.ContainsKey(newApp.Id!)) continue;
						string? pkgname = newApp.Pkgname;
						if (pkgname == null) continue;
						if (!_pkgnameAppsCache.TryGetValue(pkgname, out var pkgnameApps))
						{
							pkgnameApps = new List<App>();
							_pkgnameAppsCache[pkgname] = pkgnameApps;
						}
						pkgnameApps.Add(newApp);
						desktopApps[newApp.Id!] = newApp;
						AddCategories(comp, newApp);
					}
					if (desktopApps.Count > 0) _storesTable.Add(desktopApps);
				}
				catch (Exception)
				{
					// skip invalid/absent catalog
				}
			}
		}

		void AddCategories(XElement comp, App app)
		{
			foreach (var cat in (comp.Element("categories")?.Elements("category") ?? Enumerable.Empty<XElement>()).Select(e => e.Value))
			{
				string? bucket = CategoryMap.ContainsKey(cat) ? CategoryMap[cat] : null;
				if (bucket != null && _categoriesCache.TryGetValue(bucket, out var dict))
					dict[app.Id!] = app;
			}
		}

		static readonly Dictionary<string, string> CategoryMap = new Dictionary<string, string>
		{
			["Photography"] = "Photo & Video", ["ImageProcessing"] = "Photo & Video",
			["Graphics"] = "Photo & Video", ["Video"] = "Photo & Video",
			["VectorGraphics"] = "Photo & Video", ["2DGraphics"] = "Photo & Video",
			["3DGraphics"] = "Photo & Video",
			["Audio"] = "Music & Audio", ["Music"] = "Music & Audio", ["Midi"] = "Music & Audio",
			["Mixer"] = "Music & Audio", ["Multimedia"] = "Music & Audio",
			["WebBrowser"] = "Productivity", ["Email"] = "Productivity", ["Office"] = "Productivity",
			["Calculator"] = "Productivity", ["Calendar"] = "Productivity", ["Clock"] = "Productivity",
			["ContactManagement"] = "Productivity", ["Dictionary"] = "Productivity",
			["Documentation"] = "Productivity", ["Finance"] = "Productivity", ["FlowChart"] = "Productivity",
			["Presentation"] = "Productivity", ["Spreadsheet"] = "Productivity",
			["WordProcessor"] = "Productivity", ["Publishing"] = "Productivity",
			["Network"] = "Communication & News", ["Chat"] = "Communication & News",
			["News"] = "Communication & News", ["Feed"] = "Communication & News",
			["Communication"] = "Communication & News", ["InstantMessaging"] = "Communication & News",
			["IRCClient"] = "Communication & News", ["Telephony"] = "Communication & News",
			["VideoConference"] = "Communication & News",
			["Education"] = "Education & Science", ["Science"] = "Education & Science",
			["Astronomy"] = "Education & Science", ["Chemistry"] = "Education & Science",
			["ComputerScience"] = "Education & Science", ["Electricity"] = "Education & Science",
			["Electronics"] = "Education & Science", ["Physics"] = "Education & Science",
			["Engineering"] = "Education & Science", ["Geography"] = "Education & Science",
			["Geoscience"] = "Education & Science", ["Languages"] = "Education & Science",
			["Literature"] = "Education & Science", ["Maps"] = "Education & Science",
			["Math"] = "Education & Science", ["NumericalAnalysis"] = "Education & Science",
			["Games"] = "Games", ["Game"] = "Games", ["ActionGame"] = "Games",
			["ArcadeGame"] = "Games", ["BlocksGame"] = "Games", ["BoardGame"] = "Games",
			["LogicGame"] = "Games", ["StrategyGame"] = "Games", ["RolePlaying"] = "Games",
			["AdventureGame"] = "Games", ["CardGame"] = "Games", ["KidsGame"] = "Games",
			["Emulator"] = "Games",
			["Utility"] = "Utilities", ["Monitor"] = "Utilities", ["Accessibility"] = "Utilities",
			["Archiving"] = "Utilities", ["Compression"] = "Utilities", ["ConsoleOnly"] = "Utilities",
			["Core"] = "Utilities", ["DesktopSettings"] = "Utilities", ["DiscBurning"] = "Utilities",
			["Settings"] = "Utilities", ["FileManager"] = "Utilities", ["Filesystem"] = "Utilities",
			["FileTools"] = "Utilities", ["PackageManager"] = "Utilities", ["FileTransfer"] = "Utilities",
			["HardwareSettings"] = "Utilities",
			["Development"] = "Development", ["IDE"] = "Development", ["Building"] = "Development",
			["Construction"] = "Development", ["Database"] = "Development",
			["DataVisualization"] = "Development", ["Debugger"] = "Development",
			["GUIDesigner"] = "Development", ["Java"] = "Development", ["WebDevelopment"] = "Development"
		};

		public List<Dictionary<string, App>> GetApps() => _storesTable;

		public List<App> Search(string[] searchTokens)
		{
			var result = new List<App>();
			foreach (var apps in _storesTable)
			{
				foreach (var app in apps.Values)
				{
					var appLinked = (AppLinked)app;
					uint matchScore = appLinked.SearchMatchesAll(searchTokens);
					if (matchScore > 0 || (searchTokens.Length > 0 && (app.Id ?? "").ToLowerInvariant().Contains(searchTokens[0].ToLowerInvariant())))
						result.Add(app);
				}
			}
			return result;
		}

		public List<App> GetPkgnameApps(string pkgname) =>
			_pkgnameAppsCache.TryGetValue(pkgname, out var apps) ? apps : new List<App>();

		public Dictionary<string, App> GetCategoryApps(string category)
		{
			if (_categoriesCache.TryGetValue(category, out var appsTable)) return appsTable;
			appsTable = new Dictionary<string, App>();
			if (category == "Featured")
			{
				foreach (var name in new[]
				{
					"firefox", "vlc", "gimp", "shotwell", "inkscape", "blender", "libreoffice-still",
					"telegram-desktop", "cura", "arduino", "retroarch", "virtualbox"
				})
				{
					if (!_pkgnameAppsCache.TryGetValue(name, out var apps)) continue;
					foreach (var app in apps) appsTable[app.Id!] = app;
				}
				_categoriesCache[category] = appsTable;
			}
			return appsTable;
		}
	}

	public static class RegisterPlugin
	{
		public static Type RegisterPlugin() => typeof(Appstream);
	}
}
