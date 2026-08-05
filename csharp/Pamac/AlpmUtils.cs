namespace Pamac;

/// <summary>
/// Common operation helper retained from the Vala implementation. Public
/// events make it usable without GLib signals; Transaction is the high-level
/// facade normally used by applications.
/// </summary>
public sealed class AlpmUtils
{
    private readonly Config config;
    private readonly Database database;
    private readonly CancellationTokenSource cancellation = new();

    public AlpmUtils(Config config, Database? database = null)
    {
        this.config = config;
        this.database = database ?? new Database(config);
    }

    public Config Config => config;
    public Database Database => database;
    public CancellationToken CancellationToken => cancellation.Token;
    public string CurrentStatus { get; private set; } = string.Empty;
    public string CurrentFilename { get; private set; } = string.Empty;
    public string CurrentAction { get; private set; } = string.Empty;
    public double CurrentProgress { get; private set; }
    public ulong TotalDownload { get; private set; }
    public ulong AlreadyDownloaded { get; private set; }
    public IReadOnlyList<string> Unresolvables { get; private set; } = Array.Empty<string>();

    public event Func<string, IReadOnlyList<string>, int>? ChooseProvider;
    public event Action<string>? StartDownloading;
    public event Action<string>? StopDownloading;
    public event Action<string, string>? EmitAction;
    public event Action<string, string, string, double>? EmitActionProgress;
    public event Action<string, string, string, double>? EmitDownloadProgress;
    public event Action<string, string, string, string, double>? EmitHookProgress;
    public event Action<string, string>? EmitScriptOutput;
    public event Action<string, string>? EmitWarning;
    public event Action<string, string, IReadOnlyList<string>>? EmitError;
    public event Action<string, bool>? ImportantDetailsOutput;

    public int DoChooseProvider(string dependency, IReadOnlyList<string> providers)
    {
        var handlers = ChooseProvider?.GetInvocationList();
        return handlers is null
            ? 0
            : handlers.Select(handler => ((Func<string, IReadOnlyList<string>, int>)handler)(dependency, providers)).FirstOrDefault();
    }

    public bool SetPackageReason(string sender, string packageName, uint reason)
    {
        var transaction = new Transaction(database);
        try { return transaction.SetPackageReasonAsync(packageName, reason).GetAwaiter().GetResult(); }
        finally { transaction.Dispose(); }
    }
    public bool CleanCache(string[] filenames) => database.CleanCache(filenames);
    public bool CleanBuildFiles(string directory) => database.CleanBuildFiles(directory);
    public bool TransRefresh(string sender, bool forceRefresh)
        => new Transaction(database).RefreshDatabasesAsync().GetAwaiter().GetResult();
    public bool TransRefreshFiles(string sender, bool forceRefresh)
        => new Transaction(database).RefreshFilesDatabasesAsync().GetAwaiter().GetResult();
    public bool TransRefreshAur(string sender, bool forceRefresh)
    {
        database.Aur?.Search(string.Empty);
        return true;
    }
    public bool DownloadUpdates(string sender)
        => new Transaction(database).DownloadUpdatesAsync().GetAwaiter().GetResult();
    public bool DownloadPackages(string sender, IEnumerable<string> urls, out IReadOnlyList<string> paths)
    {
        paths = new TransactionInterfaceRoot(database).DownloadPackagesAsync(urls).GetAwaiter().GetResult();
        return paths.Count > 0 || !urls.Any();
    }
    public bool TransRun(string sender, bool sysUpgrade, IEnumerable<string> toInstall, IEnumerable<string> toRemove,
        IEnumerable<string> toLoad, IEnumerable<string> toBuild)
        => new TransactionInterfaceRoot(database).RunAsync(sysUpgrade, toInstall, toRemove, toLoad, toBuild).GetAwaiter().GetResult();
    public void TransCancel(string sender) => cancellation.Cancel();
    public void EmitEvent(uint primaryEvent, uint secondaryEvent, IReadOnlyList<string> details) { }
    public void EmitProgress(uint progress, string packageName, uint percent, uint targetCount, uint currentTarget)
    {
        CurrentFilename = packageName;
        CurrentProgress = percent / 100d;
    }
    public void EmitDownload(ulong transferred, ulong total)
    {
        AlreadyDownloaded = transferred;
        TotalDownload = total;
    }
    public void EmitTotalDownload(ulong total) => TotalDownload = total;
    public void EmitLog(uint level, string message) => EmitScriptOutput?.Invoke("alpm", message);
    public void Cancel() => cancellation.Cancel();
}
