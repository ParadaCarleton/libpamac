# libpamac — C# / GtkSharp port

This directory is a C# (.NET) rewrite of the entire libpamac repository
(`src/*.vala`), a GObject/Vala package-manager backend for Pamac built on
libalpm. The port mirrors the original meson targets 1:1:

| Meson target (Vala)        | C# project                  | Entry point / notes                       |
|----------------------------|-----------------------------|--------------------------------------------|
| `libpamac` (core library)  | `src/Pamac/Pamac.csproj`    | `Package`, `AlpmPackage`, `Database`, `Transaction`, `AlpmUtils`, configs, interfaces |
| `libpamac-aur` (plugin)    | `src/Pamac.Aur`             | AUR RPC client                             |
| `libpamac-appstream` (plug.)| `src/Pamac.Appstream`      | AppStream XML catalog parser               |
| `libpamac-snap` (plugin)   | `src/Pamac.Snap`            | snapd client                               |
| `libpamac-flatpak` (plugin)| `src/Pamac.Flatpak`         | flatpak CLI wrapper                        |
| `pamac-daemon`             | `src/Pamac.Daemon`          | `org.manjaro.pamac.daemon` service         |
| `pamac-checkupdates`       | `src/Pamac.CheckUpdates`    | prints updates, exits 100 when available   |
| `dependency-checker`       | `src/Pamac.DependencyChecker`| prints packages depending on an arg      |
| `outdated-checker`         | `src/Pamac.OutdatedChecker` | prints packages built N years ago          |
| `libalpm.vapi`             | `bindings/LibAlpm`          | managed model of the libalpm API           |
| GLib / GIO / POSIX shims   | `src/Pamac.Shared`          | `Pamac.Compat` (GFile, MainLoop, Spawn, …) |

## Mapping conventions

The Vala/GObject idioms map onto C# as follows:

- `signal` → `event`
- `async`/`yield` → `Task`-based `async`
- `GenericArray<T>` → `List<T>`
- `HashTable<K,V>` → `Dictionary<K,V>`
- `GenericSet<T>` → `HashSet<T>`
- `GLib.File`, `DataInputStream`, `MainLoop`, `Subprocess`, `Posix`, `Cancellable`
  → `Pamac.Compat` helpers (see `src/Pamac.Shared`)
- `dgettext(null, …)` → `Pamac.Compat.Gettext.DGettext`
- `libalpm` (`Alpm.Handle`, `Alpm.DB`, `Alpm.Package`, `Alpm.Depend`, …) →
  managed model in `bindings/LibAlpm`
- DBus (`org.manjaro.pamac.daemon`) → `IDaemon` interface + in-process
  `DaemonBridge` proxy (a real deployment plugs in a DBus binding)
- GObject dynamic plugins (`register_plugin`) → `PluginLoader<T>` which
  resolves a `RegisterPlugin()` static method in a plugin assembly

## Building

Requires the .NET SDK 8.0:

```sh
dotnet build libpamac.sln
```

Run the tools:

```sh
dotnet run --project src/Pamac.CheckUpdates
dotnet run --project src/Pamac.Daemon --                 # the pamac daemon
dotnet run --project src/Pamac.OutdatedChecker -- 3
```

## Notes on fidelity

Every public API and the full control flow (transaction prepare/commit, AUR
dependency resolution and the fake `pamac_aur` database, update checking,
the daemon's authorization and lock handling) are ported faithfully from the
Vala sources. The native backends (libalpm, AppStream, snapd, Flatpak, DBus)
are represented by thin managed wrappers so the port reads identically to the
original; a production build would back `bindings/LibAlpm` and the plugin
projects with the real native libraries (as the `vapi/` files do today).

SPDX-License-Identifier: GPL-3.0-or-later
Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>
