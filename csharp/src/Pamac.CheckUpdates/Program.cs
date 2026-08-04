// libpamac — C# port
//
// pamac-checkupdates: prints available updates and returns exit status 100
// when updates exist (0 otherwise). Faithful port of src/checkupdates.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Threading.Tasks;
using Pamac;

namespace Pamac
{
	public static class Program
	{
		public static async Task<int> CheckUpdates()
		{
			var config = new Config("/etc/pamac.conf");
			config.EnableAppstream = false;
			config.EnableSnap = false;
			var database = new Database(config);
			var transaction = new Transaction(database);
			if (database.NeedRefresh())
			{
				await transaction.RefreshDbsAsync();
			}
			var updates = await database.GetUpdatesAsync();
			uint updatesNb = (uint)(updates.ReposUpdates.Count + updates.AurUpdates.Count + updates.FlatpakUpdates.Count);
			if (updatesNb == 0)
			{
				return 0;
			}
			else
			{
				await transaction.RefreshFilesDbsAsync();
				if (config.DownloadUpdates) await transaction.DownloadUpdatesAsync();
				foreach (var pkg in updates.ReposUpdates)
				{
					if (pkg.InstalledVersion != null)
						Console.Out.WriteLine($"{pkg.Name}  {pkg.InstalledVersion} -> {pkg.Version}");
					else
						Console.Out.WriteLine($"{pkg.Name}  {pkg.Version}");
				}
				foreach (var pkg in updates.AurUpdates)
					Console.Out.WriteLine($"{pkg.Name}  {pkg.InstalledVersion} -> {pkg.Version}");
				foreach (var pkg in updates.FlatpakUpdates)
				{
					if (pkg.AppName == null) Console.Out.WriteLine($"{pkg.Name}  {pkg.Version}");
					else Console.Out.WriteLine($"{pkg.AppName}  {pkg.Version}");
				}
				// special status when updates are available
				return 100;
			}
		}

		public static int Main()
		{
			return CheckUpdates().GetAwaiter().GetResult();
		}
	}
}
