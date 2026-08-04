// libpamac — C# port
//
// Public abstract base class for every kind of package the library can
// represent (alpm, AUR, snap, flatpak).
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;

namespace Pamac
{
	public abstract class Package
	{
		public abstract string Name { get; set; }
		public abstract string Id { get; set; }
		public abstract string? AppName { get; }
		public abstract string? AppId { get; }
		public abstract string Version { get; set; }
		public abstract string? InstalledVersion { get; set; }
		public abstract string? Desc { get; set; }
		public abstract string? LongDesc { get; }
		public abstract string? Repo { get; set; }
		public abstract string? Launchable { get; }
		public abstract string? License { get; }
		public abstract string? Url { get; }
		public abstract string? Icon { get; }
		public abstract ulong InstalledSize { get; }
		public abstract ulong DownloadSize { get; }
		public abstract DateTime? InstallDate { get; }
		public abstract List<string> Screenshots { get; }
	}
}
