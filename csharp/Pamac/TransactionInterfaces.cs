namespace Pamac;

/// <summary>Backend abstraction corresponding to the Vala transaction interface.</summary>
public interface ITransactionInterface
{
    Task<bool> GetAuthorizationAsync(CancellationToken cancellationToken = default);
    void RemoveAuthorization();
    Task GenerateMirrorsListAsync(string country, CancellationToken cancellationToken = default);
    Task<bool> CleanCacheAsync(IEnumerable<string> files, CancellationToken cancellationToken = default);
    Task<bool> CleanBuildFilesAsync(string buildDirectory, CancellationToken cancellationToken = default);
    Task<bool> SetPackageReasonAsync(string packageName, uint reason, CancellationToken cancellationToken = default);
    Task<bool> DownloadUpdatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> DownloadPackagesAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default);
    Task<bool> RefreshDatabasesAsync(bool force, CancellationToken cancellationToken = default);
    Task<bool> RefreshFilesDatabasesAsync(bool force, CancellationToken cancellationToken = default);
    Task<bool> RunAsync(bool sysUpgrade, IEnumerable<string> toInstall, IEnumerable<string> toRemove,
        IEnumerable<string> toLoad, IEnumerable<string> toBuild, CancellationToken cancellationToken = default);
    void Cancel();
    void QuitDaemon();
}

/// <summary>Direct/root transaction backend.</summary>
public sealed class TransactionInterfaceRoot : ITransactionInterface, IDisposable
{
    private readonly Database database;
    private readonly Transaction transaction;

    public TransactionInterfaceRoot(Database database)
    {
        this.database = database;
        transaction = new Transaction(database);
    }

    public Task<bool> GetAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public void RemoveAuthorization() { }
    public async Task GenerateMirrorsListAsync(string country, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync("pacman-mirrors", new[] { "-c", country }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) database.Refresh();
    }
    public Task<bool> CleanCacheAsync(IEnumerable<string> files, CancellationToken cancellationToken = default)
        => Task.Run(() => database.CleanCache(files), cancellationToken);
    public Task<bool> CleanBuildFilesAsync(string buildDirectory, CancellationToken cancellationToken = default)
        => Task.Run(() => database.CleanBuildFiles(buildDirectory), cancellationToken);
    public Task<bool> SetPackageReasonAsync(string packageName, uint reason, CancellationToken cancellationToken = default)
        => transaction.SetPackageReasonAsync(packageName, reason, cancellationToken);
    public Task<bool> DownloadUpdatesAsync(CancellationToken cancellationToken = default)
        => transaction.DownloadUpdatesAsync(cancellationToken);
    public async Task<IReadOnlyList<string>> DownloadPackagesAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        using var client = new HttpClient();
        foreach (var url in urls)
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) continue;
            var file = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));
            await File.WriteAllBytesAsync(file, await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            result.Add(file);
        }
        return result;
    }
    public async Task<bool> RefreshDatabasesAsync(bool force, CancellationToken cancellationToken = default)
    {
        transaction.NoRefresh = !force;
        return await transaction.RefreshDatabasesAsync(cancellationToken).ConfigureAwait(false);
    }
    public Task<bool> RefreshFilesDatabasesAsync(bool force, CancellationToken cancellationToken = default)
        => transaction.RefreshFilesDatabasesAsync(cancellationToken);
    public async Task<bool> RunAsync(bool sysUpgrade, IEnumerable<string> toInstall, IEnumerable<string> toRemove,
        IEnumerable<string> toLoad, IEnumerable<string> toBuild, CancellationToken cancellationToken = default)
    {
        if (sysUpgrade) transaction.AddPackagesToUpgrade();
        foreach (var value in toInstall) transaction.AddPackageToInstall(value);
        foreach (var value in toRemove) transaction.AddPackageToRemove(value);
        foreach (var value in toLoad) transaction.AddPathToLoad(value);
        foreach (var value in toBuild) transaction.AddPackageToBuild(value);
        return await transaction.RunAsync(cancellationToken).ConfigureAwait(false);
    }
    public void Cancel() => transaction.Cancel();
    public void QuitDaemon() { }
    public void Dispose() => transaction.Dispose();
}

/// <summary>Daemon-backed transaction facade. IPC is intentionally supplied by the host.</summary>
public sealed class TransactionInterfaceDaemon : ITransactionInterface, IDisposable
{
    private readonly Daemon daemon;
    public TransactionInterfaceDaemon(Config config) { daemon = new Daemon(config.ConfPath, config.AlpmConfig.ConfigurationPath); }
    public Task<bool> GetAuthorizationAsync(CancellationToken cancellationToken = default) => daemon.GetAuthorizationAsync(cancellationToken);
    public void RemoveAuthorization() => daemon.RemoveAuthorization();
    public Task GenerateMirrorsListAsync(string country, CancellationToken cancellationToken = default) => daemon.GenerateMirrorsListAsync(country, cancellationToken);
    public Task<bool> CleanCacheAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) => Task.FromResult(daemon.CleanCache(files));
    public Task<bool> CleanBuildFilesAsync(string buildDirectory, CancellationToken cancellationToken = default) => Task.FromResult(daemon.CleanBuildFiles(buildDirectory));
    public Task<bool> SetPackageReasonAsync(string packageName, uint reason, CancellationToken cancellationToken = default) => Task.FromResult(daemon.SetPackageReason(packageName, reason));
    public Task<bool> DownloadUpdatesAsync(CancellationToken cancellationToken = default) => Task.FromResult(daemon.DownloadUpdates());
    public Task<IReadOnlyList<string>> DownloadPackagesAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default) => daemon.DownloadPackagesAsync(urls, cancellationToken);
    public Task<bool> RefreshDatabasesAsync(bool force, CancellationToken cancellationToken = default) => daemon.RefreshDatabasesAsync(force, cancellationToken);
    public Task<bool> RefreshFilesDatabasesAsync(bool force, CancellationToken cancellationToken = default) => daemon.RefreshFilesDatabasesAsync(force, cancellationToken);
    public Task<bool> RunAsync(bool sysUpgrade, IEnumerable<string> toInstall, IEnumerable<string> toRemove,
        IEnumerable<string> toLoad, IEnumerable<string> toBuild, CancellationToken cancellationToken = default)
        => daemon.RunTransactionAsync(sysUpgrade, toInstall, toRemove, toLoad, Array.Empty<string>(), toBuild, cancellationToken);
    public void Cancel() => daemon.CancelTransaction();
    public void QuitDaemon() { }
    public void Dispose() => daemon.Dispose();
}
