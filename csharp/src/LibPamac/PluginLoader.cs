using System.Reflection;

namespace Pamac
{
    /// <summary>
    /// Loads a plugin by name (equivalent of plugin_loader.vala).
    ///
    /// The original dynamically loads a native shared object exposing a
    /// `register_plugin` symbol. In this managed port we mirror that contract:
    /// a plugin is resolved from a managed assembly whose type is named after
    /// the plugin (e.g. `Pamac.AurPlugin`). This keeps the same pluggable design
    /// while staying 100% C#. A native `.so` with a `register_plugin` export can
    /// still be used by implementing <see cref="LoadFromNative"/>.
    /// </summary>
    internal class PluginLoader<T> where T : class
    {
        private string _name;

        public PluginLoader(string name)
        {
            _name = name;
        }

        /// <summary>Returns true when the plugin type could be located.</summary>
        public bool Load()
        {
            return ResolveType() != null;
        }

        /// <summary>Creates a fresh plugin instance.</summary>
        public T GetPlugin()
        {
            Type? t = ResolveType();
            if (t == null)
                throw new InvalidOperationException($"Plugin '{_name}' is not available");
            return (T)Activator.CreateInstance(t)!;
        }

        private Type? ResolveType()
        {
            // Plugins may be shipped as managed assemblies next to this library.
            string typeName = _name switch
            {
                "pamac-aur" => "Pamac.AurPlugin",
                "pamac-appstream" => "Pamac.AppstreamPlugin",
                "pamac-snap" => "Pamac.SnapPlugin",
                "pamac-flatpak" => "Pamac.FlatpakPlugin",
                _ => _name,
            };
            try
            {
                var asm = Assembly.Load(typeName.Substring(0, typeName.IndexOf('.')));
                return asm.GetType(typeName, throwOnError: false);
            }
            catch
            {
                // Plugin assembly not present -> treated as "not supported".
                return null;
            }
        }
    }
}
