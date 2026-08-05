
Library for Pamac package manager based on libalpm

A managed C#/.NET port is available under [`csharp/`](csharp/). It targets
.NET 8, preserves the package/configuration/database/transaction APIs, and
uses pacman/AppStream/AUR/Snap/Flatpak managed adapters instead of native
GObject bindings. See [`csharp/README.md`](csharp/README.md) for build and
backend details.

#### Features

 - Optional AUR support
 - Optional Appstream support
 - Optional Flatpak support
 - Optional Snap support
 - Library usable in Vala, C, C++, Python, Javascript and all languages supporting [GObject Introspection](https://gi.readthedocs.io/en/latest/users.html)

#### Installing from source

libpamac uses [Meson](http://mesonbuild.com/index.html) build system.
In the source directory run:

`mkdir builddir && cd builddir`

`meson setup --prefix=/usr --sysconfdir=/etc -Denable-aur=true -Denable-appstream=true -Denable-snap=true -Denable-flatpak=true --buildtype=release`

`meson compile`

`sudo meson install`

#### Translation

If you want to contribute in Pamac translations, use [Transifex](https://www.transifex.com/manjarolinux/manjaro-pamac).
