# `src/NoGba` is GPL-3.0-or-later

Flycast, Xenia and PPSSPP are MIT. This plugin is not, and neither is melonDS's, and the reason is
worth stating rather than leaving to be discovered.

`NoGba.dll` loads `melonds-nand.dll` into its own process through P/Invoke, and that library is
built from [melonDS](https://github.com/melonDS-emu/melonDS)'s NAND code, which is GPL-3.0-or-later.
Loading it makes the two one work, so this plugin is distributed under the same terms. The licence
text is at `tools/melonds-nand/LICENSE`.

This was a deliberate choice, made with the alternative on the table: talking to a separate
executable at arm's length would have kept the plugin MIT, at the cost of a process per question.
Direct calls won because reading a DSiWare save out of its NAND is something the plugin does while
drawing a page, not once per launch.

**Nothing else in the repository is affected.** The Flycast, PPSSPP and Xenia plugins share no code
with this one. They do share `src/Shared.Lbip`, the code that publishes an emulator row to
LaunchBox's catalogue, but that folder is this repository's own work and MIT like the rest of it -
nothing in it goes near the NAND library. They remain MIT.

## Why a no$gba plugin carries melonDS's licence

Because the code that drives a DSi NAND is the same code either way. It chooses a dump by region,
builds a configured console from it, installs the title, walks the image, takes the difference a
session made and packs it - none of which is about the emulator that will run the result. That
engine lives once, in `src/Shared.Dsi`, and both plugins compile it in.

Measured on 2026-09-24: a console built by the melonDS side booted under no$gba, our own tool
installed a title into it, the DSi menu ran it, and the walk afterwards found the same set of files
a melonDS session produces. Two emulators, one engine, one licence.
