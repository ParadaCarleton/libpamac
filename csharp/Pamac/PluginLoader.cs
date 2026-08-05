namespace Pamac;

/// <summary>Managed replacement for the GModule plugin loader.</summary>
public sealed class PluginLoader<T> where T : class
{
    private readonly string name;
    private T? plugin;

    public PluginLoader(string name) { this.name = name; }
    public bool Load()
    {
        if (plugin is not null) return true;
        var type = typeof(T);
        if (!type.IsAbstract && !type.IsInterface)
            plugin = Activator.CreateInstance(type) as T;
        return plugin is not null;
    }
    public T? GetPlugin()
    {
        Load();
        return plugin;
    }
    public string Name => name;
}
