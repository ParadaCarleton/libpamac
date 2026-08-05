using Pamac;
using Pamac.LibAlpm;

// Equivalent of outdated_checker.vala.
uint years = 3;
if (args.Length > 1)
{
    Console.Error.WriteLine("Error: only one argument needed");
    return 1;
}
if (args.Length == 1)
{
    if (args[0] == "-h" || args[0] == "--help")
    {
        Console.WriteLine("Usage:  outdated-checker <number_of_years>\n");
        Console.WriteLine("Displays packages built a given number of years ago");
        Console.WriteLine("The number of years is optional, default to 3");
        return 0;
    }
    if (!uint.TryParse(args[0], out uint nb))
    {
        Console.Error.WriteLine("Error parsing number of years argument");
        return 1;
    }
    years = nb;
}

var alpmConfig = new AlpmConfig("/etc/pacman.conf");
var alpmHandle = alpmConfig.GetHandle();
if (alpmHandle == null)
{
    return 1;
}
alpmConfig.RegisterSyncdbs(alpmHandle);

var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
long yearsSeconds = (long)years * 365 * 86400;
var found = new List<AlpmPkg>();
foreach (IntPtr dbp in alpmHandle.Syncdbs)
{
    var db = new AlpmDB(dbp);
    foreach (IntPtr pp in db.Pkgcache)
    {
        var pkg = AlpmPkg.FromPointer(pp)!;
        if (pkg.Builddate != 0)
        {
            long elapsed = now - pkg.Builddate;
            if (elapsed > yearsSeconds)
                found.Add(pkg);
        }
    }
}

// sort found packages by date (descending)
found.Sort((p1, p2) => p2.Builddate.CompareTo(p1.Builddate));

Console.WriteLine($"Packages built over {years} years ago:");
foreach (var pkg in found)
{
    var buildTime = DateTimeOffset.FromUnixTimeSeconds(pkg.Builddate).ToLocalTime();
    Console.WriteLine($"{pkg.Db?.Name}/{pkg.Name}: built the {buildTime:yyyy-MM-dd} by {pkg.Packager}");
    var requiredby = pkg.ComputeRequiredby();
    if (requiredby.Count > 0)
    {
        Console.WriteLine("  required by:");
        foreach (string r in requiredby)
            Console.WriteLine($"    {r}");
    }
}
alpmHandle.Release();
return 0;
