using Pamac;

// Equivalent of checkupdates.vala (pamac-checkupdates).
int Checkupdates()
{
    var config = new Config("/etc/pamac.conf");
    config.EnableAppstream = false;
    config.EnableSnap = false;
    var database = new Database(config);
    var transaction = new Transaction(database);
    if (database.NeedRefresh())
    {
        transaction.RefreshDbsAsync().GetAwaiter().GetResult();
    }
    var updates = database.GetUpdates();
    uint updatesNb = (uint)updates.ReposUpdates.Count + (uint)updates.AurUpdates.Count + (uint)updates.FlatpakUpdates.Count;
    if (updatesNb == 0)
    {
        return 0;
    }
    else
    {
        transaction.RefreshFilesDbsAsync().GetAwaiter().GetResult();
        if (config.DownloadUpdates)
        {
            transaction.DownloadUpdatesAsync().GetAwaiter().GetResult();
        }
        foreach (var pkg in updates.ReposUpdates)
        {
            if (pkg.InstalledVersion != null)
                Console.WriteLine($"{pkg.Name}  {pkg.InstalledVersion} -> {pkg.Version}");
            else
                Console.WriteLine($"{pkg.Name}  {pkg.Version}");
        }
        foreach (var pkg in updates.AurUpdates)
        {
            Console.WriteLine($"{pkg.Name}  {pkg.InstalledVersion} -> {pkg.Version}");
        }
        foreach (var pkg in updates.FlatpakUpdates)
        {
            string? appName = pkg.AppName;
            if (appName == null)
                Console.WriteLine($"{pkg.Name}  {pkg.Version}");
            else
                Console.WriteLine($"{appName}  {pkg.Version}");
        }
        // special status when updates are available
        return 100;
    }
}

return Checkupdates();
