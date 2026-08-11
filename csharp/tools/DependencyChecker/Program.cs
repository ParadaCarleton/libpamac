using Pamac;
using Pamac.LibAlpm;

// Equivalent of dependency_checker.vala.
if (args.Length != 1)
{
    Console.WriteLine("Error: one dependency argument needed");
    return 1;
}

var alpmConfig = new AlpmConfig("/etc/pacman.conf");
var alpmHandle = alpmConfig.GetHandle();
if (alpmHandle == null)
{
    return 1;
}
alpmConfig.RegisterSyncdbs(alpmHandle);

string depend = args[0];
foreach (IntPtr dbp in alpmHandle.Syncdbs)
{
    var db = new AlpmDB(dbp);
    foreach (IntPtr pp in db.Pkgcache)
    {
        var pkg = AlpmPkg.FromPointer(pp)!;
        // deps
        foreach (IntPtr depPtr in pkg.Depends)
        {
            var dep = AlpmDepend.FromPointer(depPtr);
            if (dep.Name == depend)
                Console.WriteLine($"{db.Name}/{pkg.Name} depends on {dep.ComputeString()} (built by {pkg.Packager})");
        }
        // optdeps
        foreach (IntPtr depPtr in pkg.Optdepends)
        {
            var dep = AlpmDepend.FromPointer(depPtr);
            if (dep.Name == depend)
                Console.WriteLine($"{db.Name}/{pkg.Name} optionally depends on {dep.ComputeString()} (built by {pkg.Packager})");
        }
        // makedeps
        foreach (IntPtr depPtr in pkg.Makedepends)
        {
            var dep = AlpmDepend.FromPointer(depPtr);
            if (dep.Name == depend)
                Console.WriteLine($"{db.Name}/{pkg.Name} make depends on {dep.ComputeString()} (built by {pkg.Packager})");
        }
        // checkdeps
        foreach (IntPtr depPtr in pkg.Checkdepends)
        {
            var dep = AlpmDepend.FromPointer(depPtr);
            if (dep.Name == depend)
                Console.WriteLine($"{db.Name}/{pkg.Name} check depends on {dep.ComputeString()} (built by {pkg.Packager})");
        }
    }
}
alpmHandle.Release();
return 0;
