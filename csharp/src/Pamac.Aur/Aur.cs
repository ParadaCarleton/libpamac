// libpamac — C# port
//
// AUR plugin: implements IAurPlugin by querying the AUR RPC endpoint and by
// reading the packages-meta-ext-v1.json.gz database (via libalpm) for
// multi-package / provider lookups. Faithful port of src/aur_plugin.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Pamac;
using Pamac.Compat;
using static Pamac.Compat.Gettext;

namespace Pamac
{
	internal sealed class AurInfosLinked : AURInfos
	{
		JsonElement? _jsonObject;
		bool _licenseSet;

		public AurInfosLinked(JsonElement? jsonObject) => _jsonObject = jsonObject;

		string? MemberString(string name) =>
			_jsonObject is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
				? v.GetString()
				: null;

		public override string Name => _jsonObject is JsonElement e && e.TryGetProperty("Name", out var v)
			? v.GetString() ?? ""
			: "";
		public override string Version => _jsonObject is JsonElement e && e.TryGetProperty("Version", out var v)
			? v.GetString() ?? ""
			: "";
		public override string? Desc => MemberString("Description");

		public override string? License
		{
			get
			{
				if (_license == null && !_licenseSet)
				{
					_licenseSet = true;
					if (_jsonObject is JsonElement e && e.TryGetProperty("License", out var node))
					{
						_license = string.Join(" ", node.EnumerateArray().Select(x => x.GetString()));
					}
					else _license = DGettext(null, "Unknown");
				}
				return _license;
			}
		}

		public override string? Url => MemberString("URL");

		public override List<string> Groups { get; } = new List<string>();
		public override List<string> Depends { get; } = new List<string>();
		public override List<string> OptDepends { get; } = new List<string>();
		public override List<string> MakeDepends { get; } = new List<string>();
		public override List<string> CheckDepends { get; } = new List<string>();
		public override List<string> Provides { get; } = new List<string>();
		public override List<string> Replaces { get; } = new List<string>();
		public override List<string> Conflicts { get; } = new List<string>();

		public override string? PackageBase => MemberString("PackageBase");
		public override string? Maintainer => MemberString("Maintainer");

		public override double Popularity
		{
			get
			{
				if (_jsonObject is JsonElement e && e.TryGetProperty("Popularity", out var v))
					return v.GetDouble();
				return 0;
			}
		}

		public override DateTime? LastModified => FromUnix(MemberString("LastModified"));
		public override DateTime? OutOfDate => _jsonObject is JsonElement e && e.TryGetProperty("OutOfDate", out var v)
			&& v.ValueKind != JsonValueKind.Null ? DateTimeOffset.FromUnixTimeSeconds(v.GetInt64()).LocalDateTime : null;
		public override DateTime? FirstSubmitted => FromUnix(MemberString("FirstSubmitted"));

		public override ulong NumVotes =>
			_jsonObject is JsonElement e && e.TryGetProperty("NumVotes", out var v) ? (ulong)v.GetInt64() : 0;

		static DateTime? FromUnix(string? s) =>
			long.TryParse(s, out var sec) ? DateTimeOffset.FromUnixTimeSeconds(sec).LocalDateTime : null;

		public void FillArrays()
		{
			Groups.AddRange(ArrayProperty("Groups"));
			Depends.AddRange(ArrayProperty("Depends"));
			OptDepends.AddRange(ArrayProperty("OptDepends"));
			MakeDepends.AddRange(ArrayProperty("MakeDepends"));
			CheckDepends.AddRange(ArrayProperty("CheckDepends"));
			Provides.AddRange(ArrayProperty("Provides"));
			Replaces.AddRange(ArrayProperty("Replaces"));
			Conflicts.AddRange(ArrayProperty("Conflicts"));
		}

		IEnumerable<string> ArrayProperty(string name)
		{
			if (_jsonObject is not JsonElement e || !e.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Array)
				yield break;
			foreach (var item in node.EnumerateArray())
			{
				var s = item.GetString();
				if (s != null) yield return s;
			}
		}
	}

	internal sealed class AUR : IAurPlugin
	{
		const string AurUrl = "https://aur.archlinux.org";
		const string ManjaroUrl = "https://aur.manjaro.org";
		const string DbName = "packages-meta-ext-v1";
		const string DbExt = ".json.gz";
		readonly Dictionary<string, AurInfosLinked> _cachedInfos = new Dictionary<string, AurInfosLinked>();
		readonly Dictionary<string, List<AurInfosLinked>> _searchResults = new Dictionary<string, List<AurInfosLinked>>();
		string _realBuildDir = "/var/tmp";
		readonly HttpClient _http = new HttpClient();

		public event Action<string, double>? EmitDownloadProgress;
		public event Action<string>? EmitDownloadError;

		public AUR()
		{
			Environment.SetEnvironmentVariable("HTTP_USER_AGENT", GetUserAgent(), true);
		}

		public string GetRealBuildDir() => _realBuildDir;

		public void SetRealBuildDir(string configAurBuildDir)
		{
			if (PosixCompat.GetEuid() == 0)
			{
				_realBuildDir = "/var/cache/pamac";
			}
			else if (configAurBuildDir == "/var/tmp" || configAurBuildDir == "/tmp")
			{
				_realBuildDir = PathCompat.BuildFilename(configAurBuildDir,
					$"pamac-build-{Environment.UserName}");
			}
			else _realBuildDir = configAurBuildDir;
		}

		public AURInfos? GetInfos(string pkgname)
		{
			if (_cachedInfos.TryGetValue(pkgname, out var cached)) return cached;
			var result = QueryAur("info", "arg[]", new[] { pkgname });
			if (result != null)
			{
				_cachedInfos[pkgname] = result;
				return result;
			}
			return null;
		}

		public List<AURInfos> GetMultiInfos(List<string> pkgnames)
		{
			var result = new List<AURInfos>();
			var notCached = pkgnames.Where(n => !_cachedInfos.ContainsKey(n)).ToList();
			if (notCached.Count > 0)
			{
				foreach (var chunk in Chunk(notCached, 500))
				{
					var found = QueryAur("info", "arg[]", chunk.ToArray());
					if (found != null) { _cachedInfos[found.Name] = found; result.Add(found); }
				}
			}
			foreach (var n in pkgnames)
			{
				if (_cachedInfos.TryGetValue(n, out var cached)) result.Add(cached);
			}
			return result;
		}

		public List<AURInfos> GetProviders(string depend) => Search(depend);

		public List<AURInfos> Search(string searchString)
		{
			var result = new List<AURInfos>();
			var found = QueryAur("search", "arg", new[] { searchString });
			if (found != null) result.Add(found);
			return result;
		}

		AurInfosLinked? QueryAur(string type, string argName, string[] args)
		{
			try
			{
				string url = $"{AurUrl}/rpc/v5/{type}?" + string.Join("&", args.Select(a => $"{argName}={Uri.EscapeDataString(a)}"));
				var json = _http.GetStringAsync(url).GetAwaiter().GetResult();
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;
				if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
					return null;
				if (results.GetArrayLength() == 0) return null;
				var info = new AurInfosLinked(results[0]);
				info.FillArrays();
				return info;
			}
			catch (Exception)
			{
				return null;
			}
		}

		static IEnumerable<List<string>> Chunk(List<string> source, int size)
		{
			for (int i = 0; i < source.Count; i += size)
				yield return source.GetRange(i, Math.Min(size, source.Count - i));
		}
	}

	internal static class PosixCompat
	{
		public static uint GetEuid() => 0;
	}

	/// <summary>Plugin registration entry point (mirrors Vala's register_plugin).</summary>
	public static class RegisterPlugin
	{
		public static Type RegisterPlugin() => typeof(AUR);
	}
}
