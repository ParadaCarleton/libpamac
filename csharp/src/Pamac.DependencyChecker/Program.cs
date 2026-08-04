// libpamac — C# port
//
// dependency-checker: prints the sync packages that depend (or optionally /
// make / check depend) on the given dependency. Faithful port of src/dependency_checker.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2025 Guillaume Benoit <guillaume@manjaro.org>

using System;
using LibAlpm;

namespace Pamac
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length != 1)
			{
				Console.Out.WriteLine("Error: one dependecy argument needed");
				return 1;
			}
			var alpmConfig = new AlpmConfig("/etc/pacman.conf");
			var alpmHandle = alpmConfig.GetHandle();
			if (alpmHandle == null) return 1;
			alpmConfig.RegisterSyncdbs(alpmHandle);
			string depend = args[0];
			foreach (var db in alpmHandle.SyncDbs)
			{
				foreach (var pkg in db.PkgCache)
				{
					foreach (var dep in pkg.Depends)
					{
						if (dep.Name == depend)
							Console.Out.WriteLine($"{db.Name}/{pkg.Name} depends on {dep.ComputeString()} (built by {pkg.Packager})");
					}
					foreach (var dep in pkg.OptDepends)
					{
						if (dep.Name == depend)
							Console.Out.WriteLine($"{db.Name}/{pkg.Name} optionally depends on {dep.ComputeString()} (built by {pkg.Packager})");
					}
					foreach (var dep in pkg.MakeDepends)
					{
						if (dep.Name == depend)
							Console.Out.WriteLine($"{db.Name}/{pkg.Name} make depends on {dep.ComputeString()} (built by {pkg.Packager})");
					}
					foreach (var dep in pkg.CheckDepends)
					{
						if (dep.Name == depend)
							Console.Out.WriteLine($"{db.Name}/{pkg.Name} check depends on {dep.ComputeString()} (built by {pkg.Packager})");
					}
				}
			}
			return 0;
		}
	}
}
