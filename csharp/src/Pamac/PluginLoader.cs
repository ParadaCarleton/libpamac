// libpamac — C# port
//
// PluginLoader<T>: loads an optional backend plugin by name. The Vala version
// dlopen()s a shared library (pamac-aur, pamac-appstream, ...) and calls its
// register_plugin() entry point. The C# port resolves the plugin assembly by
// name and invokes a static RegisterPlugin() method, so the optional AUR /
// appstream / snap / flatpak backends can be shipped independently.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Linq;
using System.Reflection;

namespace Pamac
{
	internal class PluginLoader<T> where T : class
	{
		readonly string _name;
		Type? _type;

		public string Path { get; private set; }

		public PluginLoader(string name)
		{
			_name = name;
			Path = name + ".dll";
		}

		/// <summary>Resolve and load the plugin assembly by name.</summary>
		public bool Load()
		{
			try
			{
				var assembly = Assembly.Load(_name);
				// The plugin registers its concrete type via a static RegisterPlugin() method.
				var registration = assembly.GetTypes()
					.FirstOrDefault(t => t.GetMethod("RegisterPlugin", BindingFlags.Public | BindingFlags.Static) != null);
				if (registration == null) return false;
				_type = (Type?)registration.GetMethod("RegisterPlugin")!
					.Invoke(null, null);
				return _type != null && typeof(T).IsAssignableFrom(_type);
			}
			catch
			{
				return false;
			}
		}

		public T GetPlugin()
		{
			if (_type == null) throw new InvalidOperationException("Plugin not loaded");
			return (T)Activator.CreateInstance(_type)!;
		}
	}
}
