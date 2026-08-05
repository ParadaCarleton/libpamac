# Pamac for .NET

This directory is the managed C# port of the library in `src/`. It targets
`.NET 8` and has no NuGet dependencies.

## Build

```sh
dotnet build csharp/Pamac.sln -c Release
```

The library is `Pamac/Pamac.csproj`; the solution also builds
`pamac-checkupdates`, `dependency-checker`, `outdated-checker`, and an
in-process `pamac-daemon` host executable.

## Backend differences

The Vala implementation talks to libalpm, GLib/GObject, D-Bus, AppStream,
Snapd and Flatpak through native bindings. The C# port keeps the package,
configuration, database, transaction, AUR and update-checker APIs, but uses
managed equivalents:

* pacman's on-disk database and command line interface replace the libalpm ABI;
* AUR RPC v5 is queried with `HttpClient`;
* AppStream XML catalogs are read with `System.Xml.Linq`;
* Snap and Flatpak operations use their command line clients when installed;
* `Daemon` is an in-process service facade so applications can expose it over
  the IPC mechanism they use (D-Bus adapters should be hosted by the caller).

All process arguments are passed through `ProcessStartInfo.ArgumentList`, not a
shell. Package names and AUR build paths are validated before they are used.
Optional system backends fail closed when their command is not installed.
