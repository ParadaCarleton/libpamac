using Pamac;

if (args.Length != 1)
{
    Console.Error.WriteLine("Error: one dependency argument is needed");
    return 1;
}

var dependency = AlpmVersionComparer.GetName(args[0]);
using var database = new Database(new Config());
foreach (var package in database.GetSyncPackagesByGlob("*"))
{
    var relation = package.Depends.Any(value => AlpmVersionComparer.GetName(value) == dependency) ? "depends on" :
                   package.OptionalDependencies.Any(value => AlpmVersionComparer.GetName(value) == dependency) ? "optionally depends on" :
                   package.MakeDependencies.Any(value => AlpmVersionComparer.GetName(value) == dependency) ? "make depends on" :
                   package.CheckDependencies.Any(value => AlpmVersionComparer.GetName(value) == dependency) ? "check depends on" : null;
    if (relation is not null)
        Console.WriteLine($"{package.Repository}/{package.Name} {relation} {args[0]} (built by {package.Packager})");
}
return 0;
