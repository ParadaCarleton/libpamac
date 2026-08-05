# LibPamac (C# port)

A C# (.NET) port of the [libpamac](https://gitlab.manjaro.org/packages/community/pamac/libpamac)
Vala library — the library behind the Manjaro **Pamac** package manager.

The port re-implements the public API surface and core logic of libpamac in C# and
talks to the Arch/Manjaro package manager through **P/Invoke bindings to `libalpm`**
(the same C library the original binds to).

## Layout

```
LibPamac.sln
src/LibPamac/
    LibPamac.csproj            # core library
    LibAlpm/                   # P/Invoke bindings for libalpm (alpm.h)
    Utils.cs
    Version.cs
    Package.cs                 # abstract Package, TransactionSummary, Updates
    AlpmConfig.cs              # /etc/pacman.conf parsing + Alpm.Handle setup
    AlpmPackage.cs             # AlpmPackage hierarchy (Linked / Local / Static, AUR)
    PamacConfig.cs             # /etc/pamac.conf parsing, plugin loading
    PluginLoader.cs            # native plugin (.so) loading
    PluginInterfaces.cs        # AURPlugin / AppstreamPlugin / SnapPlugin / FlatpakPlugin contracts
    Database.cs
    AlpmUtils.cs               # transaction engine on top of libalpm
    Transaction.cs
    TransactionInterface.cs
    TransactionInterfaceRoot.cs
    TransactionInterfaceDaemon.cs
    UpdatesChecker.cs
tools/
    PamacCheckupdates/Program.cs     # pamac-checkupdates equivalent
    DependencyChecker/Program.cs     # dependency-checker equivalent
    OutdatedChecker/Program.cs       # outdated-checker equivalent
```

## Dependencies

The core library needs:

* .NET SDK (target framework `net10.0`; builds cleanly with SDK 10.0.301,
  0 warnings-as-errors / 0 errors)
* `libalpm` ≥ 16.0 at **runtime** (it is a native shared library; the P/Invoke
  bindings load it via `DllImport("libalpm")`)

`libalpm` is the C library behind Arch/Manjaro's `pacman`. When it is not
installed on the host, the library degrades gracefully exactly like the
original Vala code: `AlpmConfig.GetHandle()` returns `null`, prints
"Failed to initialize alpm library", and the tools exit cleanly (verified on a
plain Debian box with no `libalpm`).

The optional plugin backends (`pamac-aur`, `pamac-appstream`, `pamac-snap`,
`pamac-flatpak`) are loaded as native shared objects at runtime by `PluginLoader`,
mirroring the original design. When a plugin is missing, the corresponding
`support_*` / `enable_*` switches simply turn off — the core still works.

## Building

```sh
cd csharp
dotnet build LibPamac.sln        # Debug
dotnet build LibPamac.sln -c Release
```

The three tools are produced as `pamac-checkupdates`, `dependency-checker` and
`outdated-checker` (see `tools/`).

## Notes on the translation

This is a faithful, line-by-line-in-spirit port of the Vala sources. The main
mechanical differences are:

* `GenericArray<string>` → `List<string>`
* `GenericSet<string?>` → `HashSet<string>`
* `HashTable<K,V>` → `Dictionary<K,V>`
* GLib `async` methods (with `yield`) → .NET `async`/`Task`
* GLib signals → C# `event`
* `unowned`/ref-counting of native objects → `SafeHandle`/`IntPtr` wrappers over alpm
* `DateTime.from_unix_*` → `DateTimeOffset.FromUnixTimeSeconds`
* GLib `MainLoop`/`context.invoke` → a small `SyncContext` scheduler helper

Everything else — the config parsers, the alpm option plumbing, the update/dependency
logic and the transaction flows — is ported directly from the original Vala code.
