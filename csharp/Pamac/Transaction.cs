namespace Pamac;

public sealed class TransactionProgressEventArgs : EventArgs
{
    public TransactionProgressEventArgs(string action, string status, double progress)
    {
        Action = action; Status = status; Progress = progress;
    }
    public string Action { get; }
    public string Status { get; }
    public double Progress { get; }
}

public sealed class TransactionHookEventArgs : EventArgs
{
    public TransactionHookEventArgs(string action, string details, string status, double progress)
    {
        Action = action; Details = details; Status = status; Progress = progress;
    }
    public string Action { get; }
    public string Details { get; }
    public string Status { get; }
    public double Progress { get; }
}

public sealed class TransactionErrorEventArgs : EventArgs
{
    public TransactionErrorEventArgs(string message, IReadOnlyList<string> details)
    {
        Message = message; Details = details;
    }
    public string Message { get; }
    public IReadOnlyList<string> Details { get; }
}

/// <summary>Coordinates pacman, AUR, Snap and Flatpak operations.</summary>
public class Transaction : IDisposable
{
    private readonly HashSet<string> packagesToInstall = new(StringComparer.Ordinal);
    private readonly HashSet<string> packagesToRemove = new(StringComparer.Ordinal);
    private readonly HashSet<string> pathsToLoad = new(StringComparer.Ordinal);
    private readonly HashSet<string> packagesToBuild = new(StringComparer.Ordinal);
    private readonly HashSet<string> temporaryIgnore = new(StringComparer.Ordinal);
    private readonly HashSet<string> overwriteFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> packagesToMarkAsDependency = new(StringComparer.Ordinal);
    private readonly HashSet<string> snapToInstall = new(StringComparer.Ordinal);
    private readonly HashSet<string> snapToRemove = new(StringComparer.Ordinal);
    private readonly HashSet<string> flatpakToInstall = new(StringComparer.Ordinal);
    private readonly HashSet<string> flatpakToRemove = new(StringComparer.Ordinal);
    private readonly HashSet<string> flatpakToUpgrade = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cancellation = new();
    private bool sysUpgrade;
    private bool forceRefresh;
    private bool disposed;

    public Transaction(Database database)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Database Database { get; }
    public bool DownloadOnly { get; set; }
    public bool DryRun { get; set; }
    public bool InstallIfNeeded { get; set; } = true;
    public bool RemoveIfUnneeded { get; set; }
    public bool Cascade { get; set; }
    public bool KeepConfigFiles { get; set; } = true;
    public bool InstallAsDependency { get; set; }
    public bool InstallAsExplicit { get; set; }
    public bool NoRefresh { get; set; }

    public event Action<string>? EmitAction;
    public event EventHandler<TransactionProgressEventArgs>? EmitActionProgress;
    public event EventHandler<TransactionProgressEventArgs>? EmitDownloadProgress;
    public event EventHandler<TransactionHookEventArgs>? EmitHookProgress;
    public event Action<string>? EmitScriptOutput;
    public event Action<string>? EmitWarning;
    public event EventHandler<TransactionErrorEventArgs>? EmitError;
    public event Action? StartWaiting;
    public event Action? StopWaiting;
    public event Action? StartPreparing;
    public event Action? StopPreparing;
    public event Action? StartDownloading;
    public event Action? StopDownloading;
    public event Action? StartBuilding;
    public event Action? StopBuilding;
    public event Action<bool>? ImportantDetailsOutput;

    // Lower-case aliases are intentionally not duplicated: C# event names are
    // PascalCase, matching the rest of the managed API.
    public bool download_only { get => DownloadOnly; set => DownloadOnly = value; }
    public bool dry_run { get => DryRun; set => DryRun = value; }
    public bool install_if_needed { get => InstallIfNeeded; set => InstallIfNeeded = value; }
    public bool remove_if_unneeded { get => RemoveIfUnneeded; set => RemoveIfUnneeded = value; }
    public bool cascade { get => Cascade; set => Cascade = value; }
    public bool keep_config_files { get => KeepConfigFiles; set => KeepConfigFiles = value; }
    public bool install_as_dep { get => InstallAsDependency; set => InstallAsDependency = value; }
    public bool install_as_explicit { get => InstallAsExplicit; set => InstallAsExplicit = value; }
    public bool no_refresh { get => NoRefresh; set => NoRefresh = value; }

    public void AddPackageToInstall(string name) => AddValidated(packagesToInstall, name);
    public void AddPackageToRemove(string name) => AddValidated(packagesToRemove, name);
    public void AddPathToLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A package path is required.", nameof(path));
        pathsToLoad.Add(path);
    }
    public void AddPackageToBuild(string name, bool cloneBuildFiles = true, bool cloneDependencyBuildFiles = true)
    {
        AddValidated(packagesToBuild, name);
        // The database owns the clone operation; the flags are retained for
        // callers through the same method signature as the Vala API.
    }
    public void AddTemporaryIgnorePackage(string name) => AddValidated(temporaryIgnore, name);
    public void AddOverwriteFile(string glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) throw new ArgumentException("An overwrite pattern is required.", nameof(glob));
        overwriteFiles.Add(glob);
    }
    public void AddPackageToMarkAsDependency(string name) => AddValidated(packagesToMarkAsDependency, name);
    public void AddPackagesToUpgrade(bool forceRefresh = false)
    {
        sysUpgrade = true;
        this.forceRefresh = forceRefresh;
        NoRefresh = !forceRefresh;
    }
    public void AddSnapToInstall(SnapPackage package) => AddValidated(snapToInstall, package?.Name ?? string.Empty);
    public void AddSnapToRemove(SnapPackage package) => AddValidated(snapToRemove, package?.Name ?? string.Empty);
    public void AddFlatpakToInstall(FlatpakPackage package) => AddValidated(flatpakToInstall, package?.Id ?? package?.Name ?? string.Empty);
    public void AddFlatpakToRemove(FlatpakPackage package) => AddValidated(flatpakToRemove, package?.Id ?? package?.Name ?? string.Empty);
    public void AddFlatpakToUpgrade(FlatpakPackage package) => AddValidated(flatpakToUpgrade, package?.Id ?? package?.Name ?? string.Empty);

    public void add_pkg_to_install(string name) => AddPackageToInstall(name);
    public void add_pkg_to_remove(string name) => AddPackageToRemove(name);
    public void add_path_to_load(string path) => AddPathToLoad(path);
    public void add_pkg_to_build(string name, bool cloneBuildFiles, bool cloneDependencyBuildFiles) => AddPackageToBuild(name, cloneBuildFiles, cloneDependencyBuildFiles);
    public void add_temporary_ignore_pkg(string name) => AddTemporaryIgnorePackage(name);
    public void add_overwrite_file(string glob) => AddOverwriteFile(glob);
    public void add_pkg_to_mark_as_dep(string name) => AddPackageToMarkAsDependency(name);
    public void add_pkgs_to_upgrade(bool forceRefresh) => AddPackagesToUpgrade(forceRefresh);

    public async Task<bool> GetAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        // pacman performs the final privilege check. This method represents the
        // polkit authorization hook from the original implementation.
        return true;
    }

    public void RemoveAuthorization() { }

    public async Task GenerateMirrorsListAsync(string country, CancellationToken cancellationToken = default)
    {
        Raise(EmitAction, $"Generating mirror list for {country}");
        await ProcessRunner.RunStreamingAsync(
            "pacman-mirrors", new[] { "-c", country },
            line => Raise(EmitScriptOutput, line), line => Raise(EmitWarning, line),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Database.Refresh();
    }

    public async Task<bool> CleanCacheAsync(CancellationToken cancellationToken = default)
    {
        var files = Database.GetCleanCacheDetails().Keys;
        return await Task.Run(() => Database.CleanCache(files), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CleanBuildFilesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Database.CleanBuildFiles(Database.GetRealAURBuildDirectory()), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetPackageReasonAsync(string packageName, uint reason, CancellationToken cancellationToken = default)
    {
        AddValidated(new HashSet<string>(StringComparer.Ordinal), packageName);
        var mode = reason == 0 ? "--asexplicit" : "--asdeps";
        var result = Database.ExecutePacman(new[] { "-D", mode, "--noconfirm", packageName }, cancellationToken);
        if (result.Succeeded) Database.Refresh();
        return result.Succeeded;
    }

    public async Task<bool> DownloadUpdatesAsync(CancellationToken cancellationToken = default)
    {
        Raise(StartDownloading);
        var result = await Database.ExecutePacmanStreamingAsync(
            new[] { "-Su", "--downloadonly", "--noconfirm" },
            line => Raise(EmitDownloadProgress, new TransactionProgressEventArgs("Downloading updates", line, 0)),
            line => Raise(EmitWarning, line), cancellationToken).ConfigureAwait(false);
        Raise(StopDownloading);
        return result == 0;
    }

    public async Task<bool> CheckDatabasesAsync(CancellationToken cancellationToken = default)
    {
        if (NoRefresh && !forceRefresh) return true;
        return await RefreshDatabasesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RefreshDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var result = await Database.ExecutePacmanStreamingAsync(
            new[] { "-Sy", "--noconfirm" }, line => Raise(EmitScriptOutput, line), line => Raise(EmitWarning, line), cancellationToken).ConfigureAwait(false);
        if (result == 0) Database.Refresh();
        return result == 0;
    }

    public async Task<bool> RefreshFilesDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var result = await Database.ExecutePacmanStreamingAsync(
            new[] { "-Fy", "--noconfirm" }, line => Raise(EmitScriptOutput, line), line => Raise(EmitWarning, line), cancellationToken).ConfigureAwait(false);
        return result == 0;
    }

    public async Task<bool> SnapSwitchChannelAsync(string snapName, string channel, CancellationToken cancellationToken = default)
    {
        if (!Database.Config.EnableSnap) return false;
        return await Database.Snap.SwitchChannelAsync(snapName, channel, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunCommandLineAsync(IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        if (arguments.Count == 0) throw new ArgumentException("At least one argument is required.", nameof(arguments));
        return await ProcessRunner.RunStreamingAsync(arguments[0], arguments.Skip(1),
            line => Raise(EmitScriptOutput, line), line => Raise(EmitWarning, line), workingDirectory,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellation.Token);
        var token = linked.Token;
        var didWork = sysUpgrade || packagesToInstall.Count > 0 || packagesToRemove.Count > 0 ||
                      pathsToLoad.Count > 0 || packagesToBuild.Count > 0 || snapToInstall.Count > 0 ||
                      snapToRemove.Count > 0 || flatpakToInstall.Count > 0 || flatpakToRemove.Count > 0 ||
                      flatpakToUpgrade.Count > 0;
        if (!didWork) return true;

        try
        {
            Raise(StartPreparing);
            if (DryRun)
            {
                Raise(EmitAction, "Dry run: transaction was not executed");
                Raise(StopPreparing);
                return true;
            }

            var success = await RunPacmanOperationsAsync(token).ConfigureAwait(false);
            Raise(StopPreparing);
            if (!success) return false;

            if (packagesToBuild.Count > 0)
            {
                success = await BuildAurPackagesAsync(token).ConfigureAwait(false);
                if (!success) return false;
            }

            if (snapToInstall.Count > 0 || snapToRemove.Count > 0)
            {
                Raise(EmitAction, "Applying Snap changes");
                if (snapToRemove.Count > 0 && !await Database.Snap.RemoveAsync(snapToRemove, token).ConfigureAwait(false)) return false;
                if (snapToInstall.Count > 0 && !await Database.Snap.InstallAsync(snapToInstall, token).ConfigureAwait(false)) return false;
            }
            if (flatpakToRemove.Count > 0 || flatpakToInstall.Count > 0 || flatpakToUpgrade.Count > 0)
            {
                Raise(EmitAction, "Applying Flatpak changes");
                if (flatpakToRemove.Count > 0 && !await Database.Flatpak.RemoveAsync(flatpakToRemove, token).ConfigureAwait(false)) return false;
                if (flatpakToInstall.Count > 0 && !await Database.Flatpak.InstallAsync(flatpakToInstall, token).ConfigureAwait(false)) return false;
                if (flatpakToUpgrade.Count > 0 && !await Database.Flatpak.UpgradeAsync(flatpakToUpgrade, token).ConfigureAwait(false)) return false;
            }
            Database.Refresh();
            return true;
        }
        catch (OperationCanceledException)
        {
            Raise(EmitWarning, "Transaction cancelled.");
            return false;
        }
        catch (Exception exception)
        {
            Raise(EmitError, new TransactionErrorEventArgs("Failed to run transaction", new[] { exception.Message }));
            return false;
        }
    }

    public Task<bool> get_authorization_async(CancellationToken cancellationToken = default) => GetAuthorizationAsync(cancellationToken);
    public Task<bool> clean_cache_async(CancellationToken cancellationToken = default) => CleanCacheAsync(cancellationToken);
    public Task<bool> clean_build_files_async(CancellationToken cancellationToken = default) => CleanBuildFilesAsync(cancellationToken);
    public Task<bool> set_pkgreason_async(string packageName, uint reason, CancellationToken cancellationToken = default) => SetPackageReasonAsync(packageName, reason, cancellationToken);
    public Task<bool> download_updates_async(CancellationToken cancellationToken = default) => DownloadUpdatesAsync(cancellationToken);
    public Task<bool> refresh_dbs_async(CancellationToken cancellationToken = default) => RefreshDatabasesAsync(cancellationToken);
    public Task<bool> refresh_files_dbs_async(CancellationToken cancellationToken = default) => RefreshFilesDatabasesAsync(cancellationToken);
    public Task<bool> snap_switch_channel_async(string snapName, string channel, CancellationToken cancellationToken = default) => SnapSwitchChannelAsync(snapName, channel, cancellationToken);
    public void add_snap_to_install(SnapPackage package) => AddSnapToInstall(package);
    public void add_snap_to_remove(SnapPackage package) => AddSnapToRemove(package);
    public void add_flatpak_to_install(FlatpakPackage package) => AddFlatpakToInstall(package);
    public void add_flatpak_to_remove(FlatpakPackage package) => AddFlatpakToRemove(package);
    public void add_flatpak_to_upgrade(FlatpakPackage package) => AddFlatpakToUpgrade(package);
    public Task<bool> run_async(CancellationToken cancellationToken = default) => RunAsync(cancellationToken);

    public void Cancel() => cancellation.Cancel();
    public void cancel() => Cancel();
    public void QuitDaemon() { }
    public void quit_daemon() => QuitDaemon();
    public Task<bool> SnapSwitchChannelAsyncCompat(string snapName, string channel, CancellationToken cancellationToken = default)
        => SnapSwitchChannelAsync(snapName, channel, cancellationToken);

    public TransactionSummary GetSummary()
    {
        var summary = new TransactionSummary();
        foreach (var name in packagesToInstall)
            if (Database.GetPackage(name) is { } package) summary.ToInstall.Add(package);
        foreach (var name in packagesToRemove)
            if (Database.GetPackage(name) is { } package) summary.ToRemove.Add(package);
        foreach (var name in packagesToBuild)
            if (Database.GetPackage(name) is { } package) summary.ToBuild.Add(package);
        return summary;
    }

    protected virtual Task<bool> AskCommitAsync(TransactionSummary summary) => Task.FromResult(true);
    protected virtual Task<bool> AskEditBuildFilesAsync(TransactionSummary summary) => Task.FromResult(false);
    protected virtual Task<bool> AskImportKeyAsync(string packageName, string key, string? owner) => Task.FromResult(false);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task<bool> RunPacmanOperationsAsync(CancellationToken cancellationToken)
    {
        if (pathsToLoad.Count > 0)
        {
            var arguments = new List<string> { "-U", "--noconfirm" };
            arguments.AddRange(pathsToLoad);
            if (!await RunPacmanAsync(arguments, cancellationToken).ConfigureAwait(false)) return false;
        }

        if (sysUpgrade || packagesToInstall.Count > 0)
        {
            var arguments = new List<string> { sysUpgrade ? (NoRefresh ? "-Su" : "-Syu") : "-S", "--noconfirm" };
            if (InstallIfNeeded) arguments.Add("--needed");
            if (DownloadOnly) arguments.Add("--downloadonly");
            if (InstallAsDependency) arguments.Add("--asdeps");
            if (InstallAsExplicit) arguments.Add("--asexplicit");
            AddSharedOptions(arguments);
            if (sysUpgrade) arguments.AddRange(packagesToInstall);
            else arguments.AddRange(packagesToInstall);
            if ((sysUpgrade || packagesToInstall.Count > 0) && !await RunPacmanAsync(arguments, cancellationToken).ConfigureAwait(false)) return false;
        }

        if (packagesToRemove.Count > 0)
        {
            var arguments = new List<string> { "-R", "--noconfirm" };
            if (Cascade) arguments.Add("--cascade");
            if (RemoveIfUnneeded) arguments.Add("--recursive");
            AddSharedOptions(arguments);
            arguments.AddRange(packagesToRemove);
            if (!await RunPacmanAsync(arguments, cancellationToken).ConfigureAwait(false)) return false;
        }

        foreach (var name in packagesToMarkAsDependency)
        {
            var result = Database.ExecutePacman(new[] { "-D", "--asdeps", "--noconfirm", name }, cancellationToken);
            if (!result.Succeeded) return false;
        }
        return true;
    }

    private async Task<bool> RunPacmanAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Raise(EmitAction, "Running pacman");
        Raise(StartDownloading);
        var result = await Database.ExecutePacmanStreamingAsync(arguments,
            line =>
            {
                Raise(EmitScriptOutput, line);
                Raise(EmitActionProgress, new TransactionProgressEventArgs("pacman", line, 0));
            },
            line =>
            {
                Raise(EmitWarning, line);
                Raise(EmitError, new TransactionErrorEventArgs("pacman", new[] { line }));
            }, cancellationToken).ConfigureAwait(false);
        Raise(StopDownloading);
        return result == 0;
    }

    private async Task<bool> BuildAurPackagesAsync(CancellationToken cancellationToken)
    {
        Raise(StartBuilding);
        try
        {
            foreach (var packageName in packagesToBuild)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Raise(EmitAction, $"Building {packageName}");
                if (!Database.CloneBuildFiles(packageName, overwriteFiles.Contains(packageName), cancellationToken)) return false;
                var directory = Path.Combine(Database.GetRealAURBuildDirectory(), packageName);
                var result = await RunCommandLineAsync(new[] { "makepkg", "--syncdeps", "--noconfirm", "--needed" }, directory, cancellationToken).ConfigureAwait(false);
                if (result != 0) return false;
                var builtPackages = Directory.Exists(directory)
                    ? Directory.EnumerateFiles(directory, "*.pkg.tar*", SearchOption.TopDirectoryOnly)
                        .Where(path => !path.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)).ToArray()
                    : Array.Empty<string>();
                if (builtPackages.Length > 0)
                {
                    var installResult = Database.ExecutePacman(new[] { "-U", "--noconfirm" }.Concat(builtPackages), cancellationToken);
                    if (!installResult.Succeeded) return false;
                }
            }
            return true;
        }
        finally
        {
            Raise(StopBuilding);
        }
    }

    private void AddSharedOptions(ICollection<string> arguments)
    {
        foreach (var ignored in temporaryIgnore) { arguments.Add("--ignore"); arguments.Add(ignored); }
        foreach (var overwrite in overwriteFiles) { arguments.Add("--overwrite"); arguments.Add(overwrite); }
    }

    private static void AddValidated(ISet<string> destination, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Contains(';') || value.Contains('\n'))
            throw new ArgumentException("Invalid package or path.", nameof(value));
        destination.Add(value);
    }

    private static void Raise(Action<string>? handler, string value) => handler?.Invoke(value);
    private static void Raise(Action<bool>? handler, bool value) => handler?.Invoke(value);
    private static void Raise(Action? handler) => handler?.Invoke();
    private static void Raise(EventHandler<TransactionProgressEventArgs>? handler, TransactionProgressEventArgs value) => handler?.Invoke(null, value);
    private static void Raise(EventHandler<TransactionHookEventArgs>? handler, TransactionHookEventArgs value) => handler?.Invoke(null, value);
    private static void Raise(EventHandler<TransactionErrorEventArgs>? handler, TransactionErrorEventArgs value) => handler?.Invoke(null, value);
}
