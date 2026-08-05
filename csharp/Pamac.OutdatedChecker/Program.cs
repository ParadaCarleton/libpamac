using Pamac;

var years = 3u;
if (args.Length > 1)
{
    Console.Error.WriteLine("Error: only one argument is allowed");
    return 1;
}
if (args.Length == 1)
{
    if (args[0] is "-h" or "--help")
    {
        Console.WriteLine("Usage: outdated-checker <number_of_years>");
        Console.WriteLine("Displays packages built a given number of years ago (default: 3).");
        return 0;
    }
    if (!uint.TryParse(args[0], out years))
    {
        Console.Error.WriteLine("Error parsing number of years argument");
        return 1;
    }
}

using var database = new Database(new Config());
var cutoff = DateTimeOffset.UtcNow.AddDays(-365.25 * years);
var packages = database.GetSyncPackagesByGlob("*")
    .Where(package => package.BuildDate is not null && package.BuildDate.Value < cutoff)
    .OrderByDescending(package => package.BuildDate)
    .ToArray();
Console.WriteLine($"Packages built over {years} years ago:");
foreach (var package in packages)
{
    Console.WriteLine($"{package.Repository}/{package.Name}: built the {package.BuildDate!.Value:yyyy-MM-dd} by {package.Packager}");
    if (package.RequiredBy.Count > 0)
    {
        Console.WriteLine("  required by:");
        foreach (var dependent in package.RequiredBy) Console.WriteLine($"    {dependent}");
    }
}
return 0;
