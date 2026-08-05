namespace Pamac;

/// <summary>
/// In-process replacement for the GLib/D-Bus daemon. Applications that need a
/// system service can host this class behind their own .NET IPC transport.
/// </summary>
public sealed class Daemon : IDisposable
{
    private readonly Config config;
    private readonly Database database;
    private Transaction? transaction;
    private bool disposed;

    public Daemon(string pamacConfigurationPath = "/etc/pamac.conf", string? pacmanConfigurationPath = null)
    {
        config = new Config(pamacConfigurationPath, pacmanConfigurationPath);
        database = new Database(config);
    }

    public Config Config => config;
    public Database Database => database;
    public string Sender { get; } = $"dotnet-{Environment.ProcessId}-{Guid.NewGuid():N}";
    public string LockFile => Path.Combine(config.AlpmConfig.LocalDatabasePath, "db.lck");

    public event Action<string, string>? EmitAction;
    public event Action<string, string, string, double>? EmitActionProgress;
    public event Action<string, string, string, double>? EmitDownloadProgress;
    public event Action<string, string, string, string, double>? EmitHookProgress;
    public event Action<string, string>? EmitScriptOutput;
    public event Action<string, string>? EmitWarning;
    public event Action<string, string, IReadOnlyList<string>>? EmitError;
    public event Action<string, bool>? ImportantDetailsOutput;
    public event Action<string>? StartDownloading;
    public event Action<string>? StopDownloading;
    public event Action<string>? StartWaiting;
    public event Action<string>? StopWaiting;
    public event Action<string, bool>? TransRunFinished;
    public event Action<string, bool>? TransRefreshFinished;
    public event Action<string, bool>? TransRefreshFilesFinished;
    public event Action<string, bool>? DownloadUpdatesFinished;
    public event Action<string, bool>? SetPackageReasonFinished;
    public event Action<string, string>? GenerateMirrorsListData;
    public event Action<string>? GenerateMirrorsListFinished;
    public event Action<string, bool>? CleanCacheFinished;
    public event Action<string, bool>? CleanBuildFilesFinished;

    public string GetSender() => Sender;
    public string GetLockFile() => LockFile;
    public void SetEnvironmentVariables(IReadOnlyDictionary<string, string> variables)
    {
        foreach (var pair in variables) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    public Task<bool> GetAuthorizationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
    public void RemoveAuthorization() { }

    public void WriteAlpmConfig(IReadOnlyDictionary<string, object?> values) => config.AlpmConfig.Write(values);
    public void WritePamacConfig(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var pair in values)
        {
            switch (pair.Key)
            {
                case "RefreshPeriod" when ConvertToUInt64(pair.Value) is { } refresh: config.RefreshPeriod = refresh; break;
                case "EnableAUR" when pair.Value is bool aur: config.EnableAur = aur; break;
                case "EnableSnap" when pair.Value is bool snap: config.EnableSnap = snap; break;
                case "EnableFlatpak" when pair.Value is bool flatpak: config.EnableFlatpak = flatpak; break;
                case "BuildDirectory" when pair.Value is string directory: config.AURBuildDirectory = directory; break;
            }
        }
        config.Save();
    }

    public async Task GenerateMirrorsListAsync(string country, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunStreamingAsync("pacman-mirrors", new[] { "-c", country },
            line => GenerateMirrorsListData?.Invoke(Sender, line), line => EmitWarning?.Invoke(Sender, line),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result == 0) database.Refresh();
        GenerateMirrorsListFinished?.Invoke(Sender);
    }

    public bool CleanCache(IEnumerable<string> filenames)
    {
        var success = database.CleanCache(filenames);
        CleanCacheFinished?.Invoke(Sender, success);
        return success;
    }
    public bool CleanBuildFiles(string buildDirectory)
    {
        var success = database.CleanBuildFiles(buildDirectory);
        CleanBuildFilesFinished?.Invoke(Sender, success);
        return success;
    }
    public bool SetPackageReason(string packageName, uint reason)
    {
        transaction ??= CreateTransaction();
        var success = transaction.SetPackageReasonAsync(packageName, reason).GetAwaiter().GetResult();
        SetPackageReasonFinished?.Invoke(Sender, success);
        return success;
    }

    public bool DownloadUpdates()
    {
        transaction ??= CreateTransaction();
        var success = transaction.DownloadUpdatesAsync().GetAwaiter().GetResult();
        DownloadUpdatesFinished?.Invoke(Sender, success);
        return success;
    }

    public async Task<IReadOnlyList<string>> DownloadPackagesAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var paths = new List<string>();
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            var filename = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(filename)) filename = "download";
            var path = Path.Combine(Path.GetTempPath(), filename);
            await using var source = await database.GetUrlStreamAsync(url, cancellationToken).ConfigureAwait(false);
            await using var destination = File.Create(path);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            paths.Add(path);
        }
        return paths;
    }

    public async Task<bool> RefreshDatabasesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        transaction ??= CreateTransaction();
        transaction.NoRefresh = !force;
        var success = await transaction.RefreshDatabasesAsync(cancellationToken).ConfigureAwait(false);
        TransRefreshFinished?.Invoke(Sender, success);
        return success;
    }

    public async Task<bool> RefreshFilesDatabasesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        transaction ??= CreateTransaction();
        var success = await transaction.RefreshFilesDatabasesAsync(cancellationToken).ConfigureAwait(false);
        TransRefreshFilesFinished?.Invoke(Sender, success);
        return success;
    }

    public async Task<bool> RunTransactionAsync(bool sysUpgrade, IEnumerable<string> toInstall, IEnumerable<string> toRemove,
        IEnumerable<string> toLoadLocal, IEnumerable<string> toLoadRemote, IEnumerable<string> toBuild,
        CancellationToken cancellationToken = default)
    {
        transaction?.Dispose();
        transaction = CreateTransaction();
        if (sysUpgrade) transaction.AddPackagesToUpgrade();
        foreach (var name in toInstall) transaction.AddPackageToInstall(name);
        foreach (var name in toRemove) transaction.AddPackageToRemove(name);
        foreach (var path in toLoadLocal.Concat(toLoadRemote)) transaction.AddPathToLoad(path);
        foreach (var name in toBuild) transaction.AddPackageToBuild(name);
        var success = await transaction.RunAsync(cancellationToken).ConfigureAwait(false);
        TransRunFinished?.Invoke(Sender, success);
        return success;
    }

    public void CancelTransaction() => transaction?.Cancel();
    public void Quit() => Dispose();

    public Task<bool> start_trans_run(bool sysUpgrade, IEnumerable<string> toInstall, IEnumerable<string> toRemove,
        IEnumerable<string> toLoadLocal, IEnumerable<string> toLoadRemote, IEnumerable<string> toBuild,
        CancellationToken cancellationToken = default)
        => RunTransactionAsync(sysUpgrade, toInstall, toRemove, toLoadLocal, toLoadRemote, toBuild, cancellationToken);

    public void trans_cancel() => CancelTransaction();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        transaction?.Dispose();
        database.Dispose();
    }

    private Transaction CreateTransaction()
    {
        var value = new Transaction(database);
        value.EmitAction += action => EmitAction?.Invoke(Sender, action);
        value.EmitActionProgress += (_, args) => EmitActionProgress?.Invoke(Sender, args.Action, args.Status, args.Progress);
        value.EmitDownloadProgress += (_, args) => EmitDownloadProgress?.Invoke(Sender, args.Action, args.Status, args.Progress);
        value.EmitHookProgress += (_, args) => EmitHookProgress?.Invoke(Sender, args.Action, args.Details, args.Status, args.Progress);
        value.EmitScriptOutput += message => EmitScriptOutput?.Invoke(Sender, message);
        value.EmitWarning += message => EmitWarning?.Invoke(Sender, message);
        value.EmitError += (_, args) => EmitError?.Invoke(Sender, args.Message, args.Details);
        value.StartDownloading += () => StartDownloading?.Invoke(Sender);
        value.StopDownloading += () => StopDownloading?.Invoke(Sender);
        return value;
    }

    private static ulong? ConvertToUInt64(object? value)
    {
        try { return value is null ? null : Convert.ToUInt64(value); }
        catch (FormatException) { return null; }
        catch (InvalidCastException) { return null; }
    }
}

public sealed class DaemonConfig
{
    public DaemonConfig(string path = "/etc/pamac.conf") { Config = new Config(path); }
    public Config Config { get; }
    public string ConfPath => Config.ConfPath;
    public ulong RefreshPeriod { get => Config.RefreshPeriod; set => Config.RefreshPeriod = value; }
    public bool SupportAur => Config.SupportAur;
    public bool EnableAur { get => Config.EnableAur; set => Config.EnableAur = value; }
    public bool SupportAppstream => Config.SupportAppstream;
    public bool EnableAppstream { get => Config.EnableAppstream; set => Config.EnableAppstream = value; }
    public bool SupportSnap => Config.SupportSnap;
    public bool EnableSnap { get => Config.EnableSnap; set => Config.EnableSnap = value; }
    public bool SupportFlatpak => Config.SupportFlatpak;
    public bool EnableFlatpak { get => Config.EnableFlatpak; set => Config.EnableFlatpak = value; }
    public bool CheckFlatpakUpdates { get => Config.CheckFlatpakUpdates; set => Config.CheckFlatpakUpdates = value; }
    public string AURBuildDirectory { get => Config.AURBuildDirectory; set => Config.AURBuildDirectory = value; }
    public bool CheckAurUpdates { get => Config.CheckAurUpdates; set => Config.CheckAurUpdates = value; }
    public bool CheckAurVcsUpdates { get => Config.CheckAurVcsUpdates; set => Config.CheckAurVcsUpdates = value; }
    public bool DownloadUpdates { get => Config.DownloadUpdates; set => Config.DownloadUpdates = value; }
    public bool OfflineUpgrade { get => Config.OfflineUpgrade; set => Config.OfflineUpgrade = value; }
    public ulong MaxParallelDownloads { get => Config.MaxParallelDownloads; set => Config.MaxParallelDownloads = value; }
    public void Reload() => Config.Reload();
    public void Save() => Config.Save();
}
