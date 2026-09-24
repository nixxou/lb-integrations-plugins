# `src/Installer` is GPL-3.0-or-later

Every line of this installer is this repository's own work and would otherwise be MIT, like the rest
of `LICENSE`. It is GPL-3.0-or-later because of what it carries rather than what it is.

`NixxIntegrations.exe` embeds all five merged plugins, and two of them - `MelonDs.dll` and
`NoGba.dll` - are GPL-3.0-or-later: they load `melonds-nand` into their own process, and that
library is built from [melonDS](https://github.com/melonDS-emu/melonDS)'s NAND code. Shipping them
inside a single file means distributing them, so the file goes out under their terms. The licence
text is at `tools/melonds-nand/LICENSE`.

## This is the cautious reading, not the only one

The installer does not link against anything it carries. The plugins are opaque bytes to it: they
are written to disk and never loaded into this process, and the two never share an address space.
That is close to what the GPL calls mere aggregation - the same relationship a zip file has with its
contents - and a reasonable person could conclude the installer keeps its own licence.

The cautious reading was taken anyway, for two reasons. It costs nothing: nobody is worse off for
this installer being GPL, since its source is right here either way. And "it was probably
aggregation" is a poor thing to find out you were wrong about after publishing.

## What this does not change

The plugins themselves are unaffected: `Flycast.dll`, `Ppsspp.dll` and `Xenia.dll` remain MIT, and
installing them from here does not alter that. A user who takes those three out of the release and
leaves the other two has three MIT plugins. The licence attaches to the combined file, not to each
thing inside it.
