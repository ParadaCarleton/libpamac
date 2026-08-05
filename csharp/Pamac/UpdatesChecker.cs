namespace Pamac;

public sealed class UpdatesAvailableEventArgs : EventArgs
{
    public UpdatesAvailableEventArgs(IReadOnlyList<string> packages)
    {
        Packages = packages;
    }
    public IReadOnlyList<string> Packages { get; }
    public ushort Count => (ushort)Packages.Count;
}

/// <summary>Periodic update checker used by tray applications.</summary>
public sealed class UpdatesChecker : IDisposable
{
    private readonly Config config;
    private readonly FileSystemWatcher? lockWatcher;
    private bool disposed;
    private DateTimeOffset lastCheck;

    public UpdatesChecker(string pamacConfigurationPath = "/etc/pamac.conf")
    {
        config = new Config(pamacConfigurationPath);
        var directory = Path.GetDirectoryName(Path.Combine(config.AlpmConfig.LocalDatabasePath, "db.lck"));
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            lockWatcher = new FileSystemWatcher(directory, "db.lck") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            lockWatcher.Created += (_, _) => { }; // a refresh is in progress
            lockWatcher.Deleted += (_, _) => _ = CheckUpdatesAsync();
            lockWatcher.EnableRaisingEvents = true;
        }
    }

    public ushort UpdatesNumber { get; private set; }
    public IReadOnlyList<string> UpdatesList { get; private set; } = Array.Empty<string>();
    public ulong RefreshPeriod => config.RefreshPeriod;
    public bool NoUpdateHideIcon => config.NoUpdateHideIcon;
    public event EventHandler<UpdatesAvailableEventArgs>? UpdatesAvailable;

    public IReadOnlyList<string> CheckUpdates()
    {
        config.Reload();
        if (config.RefreshPeriod == 0) return Array.Empty<string>();
        var result = ProcessRunner.Run("pamac-checkupdates", Array.Empty<string>());
        var updates = result.ExitCode == 100
            ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
        if (!result.Succeeded && result.ExitCode != 100)
        {
            using var database = new Database(config);
            var values = database.GetUpdates();
            updates = values.RepositoryUpdates.Select(package => package.Name)
                .Concat(values.AURUpdates.Select(package => package.Name)).ToArray();
        }
        UpdatesList = updates;
        UpdatesNumber = (ushort)Math.Min(ushort.MaxValue, updates.Count);
        lastCheck = DateTimeOffset.UtcNow;
        UpdatesAvailable?.Invoke(this, new UpdatesAvailableEventArgs(updates));
        return updates;
    }

    public Task<IReadOnlyList<string>> CheckUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.Run(CheckUpdates, cancellationToken);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lockWatcher?.Dispose();
    }
}
