// libpamac — C# port
//
// Top-level helpers from src/utils.vala: get_os_id / get_user_agent.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using Pamac.Compat;

namespace Pamac
{
	/// <summary>Returns the ID= value of /etc/os-release, or null.</summary>
	public static string? GetOsId()
	{
		var file = GFile.NewForPath("/etc/os-release");
		if (file.QueryExists())
		{
			try
			{
				using var dis = new DataInputStream(file.Read());
				string? line;
				while ((line = dis.ReadLine()) != null)
				{
					if (line.StartsWith("ID="))
					{
						return line.Split("ID=", 2)[1];
					}
				}
			}
			catch (Exception)
			{
				// silent error
			}
		}
		return null;
	}

	/// <summary>Builds the HTTP user agent string used for downloads.</summary>
	public static string GetUserAgent()
	{
		string? id = GetOsId();
		if (id == null)
		{
			return string.Format("Pamac/{0}", VersionInfo.Version);
		}
		return string.Format("Pamac/{0}_{1}", VersionInfo.Version, id);
	}
}
