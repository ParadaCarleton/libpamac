// libpamac — C# port
// SPDX-License-Identifier: GPL-3.0-or-later
//
// The plugin backends and the daemon are separate assemblies but implement the
// library's internal plugin/daemon interfaces (mirroring how the Vala build
// links the plugin .so files against the shared library's internal symbols).

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Pamac.Aur")]
[assembly: InternalsVisibleTo("Pamac.Appstream")]
[assembly: InternalsVisibleTo("Pamac.Snap")]
[assembly: InternalsVisibleTo("Pamac.Flatpak")]
[assembly: InternalsVisibleTo("Pamac.Daemon")]
