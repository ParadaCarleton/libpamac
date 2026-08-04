// libpamac — C# port
//
// outdated-checker: displays packages built more than a given number of years
// ago (default 3). Faithful port of src/outdated_checker.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2020-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Linq;
using LibAlpm;

namespace Pamac
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			uint years = 3;
			if (args.Length > 1)
			{
				Console.Error.WriteLine("Error: only one argument needed");
				return 1;
			}
			if (args.Length == 1)
			{
				if (args[0] == "-h" || args[0] == "--help")
				{
					Console.Out.WriteLine("Usage:  outdated-checker <number_of_years>\n\n");
					Console.Out.WriteLine("Displays packages built a given number of years ago");
					Console.Out.WriteLine("The number of years is optional, default to 3");
					return 0;
				}
				if (uint.TryParse(args[0], out uint nb)) years = nb;
				else
				{
					Console.Error.WriteLine("Error parsing number of years argument");
					return 1;
				}
			}
			var alpmConfig = new AlpmConfig("/etc/pacman.conf");
			var alpmHandle = alpmConfig.GetHandle();
			if (alpmHandle == null) return 1;
			alpmConfig.RegisterSyncdbs(alpmHandle);
			var now = DateTime.UtcNow;
			TimeSpan yearsTime = TimeSpan.FromDays(365 * years);
			var found = new List<Package>();
			foreach (var db in alpmHandle.SyncDbs)
			{
				foreach (var pkg in db.PkgCache)
				{
					if (pkg.BuildDate != 0)
					{
						var buildTime = DateTimeOffset.FromUnixTimeSeconds(pkg.BuildDate).UtcDateTime;
						TimeSpan elapsed = now - buildTime;
						if (elapsed > yearsTime) found.Add(pkg);
					}
				}
			}
			found.Sort((a, b) => b.BuildDate.CompareTo(a.BuildDate));
			Console.Out.WriteLine($"Packages built over {years} years ago:");
			foreach (var pkg in found)
			{
				var buildTime = DateTimeOffset.FromUnixTimeSeconds(pkg.BuildDate).UtcDateTime;
				Console.Out.WriteLine($"{pkg.DB!.Name}/{pkg.Name}: built the {buildTime.ToShortDateString()} by {pkg.Packager}");
				var requiredby = pkg.ComputeRequiredBy();
				if (requiredby.Count != 0)
				{
					Console.Out.WriteLine("  required by:");
					foreach (var r in requiredby) Console.Out.WriteLine($"    {r}");
				}
			}
			return 0;
		}
	}
}
