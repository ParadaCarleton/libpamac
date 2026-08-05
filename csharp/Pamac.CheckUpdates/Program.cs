using Pamac;

var config = new Config();
using var database = new Database(config);
using var transaction = new Transaction(database);
if (database.NeedRefresh()) await transaction.RefreshDatabasesAsync();
var updates = database.GetUpdates();
var names = updates.RepositoryUpdates.Select(package => package.Name)
    .Concat(updates.AURUpdates.Select(package => package.Name))
    .Concat(updates.FlatpakUpdates.Select(package => package.Name))
    .Distinct(StringComparer.Ordinal)
    .ToArray();
if (names.Length > 0)
{
    await transaction.RefreshFilesDatabasesAsync();
    if (config.DownloadUpdates) await transaction.DownloadUpdatesAsync();
}
foreach (var name in names) Console.WriteLine(name);
return names.Length == 0 ? 0 : 100;
