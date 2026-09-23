# `src/MelonDs` is GPL-3.0-or-later

The rest of this repository is MIT. This plugin is not, and the reason is worth stating rather than
leaving to be discovered.

`MelonDs.dll` loads `melonds-nand.dll` into its own process through P/Invoke, and that library is
built from [melonDS](https://github.com/melonDS-emu/melonDS)'s NAND code, which is GPL-3.0-or-later.
Loading it makes the two one work, so this plugin is distributed under the same terms. The licence
text is at `tools/melonds-nand/LICENSE`.

This was a deliberate choice, made with the alternative on the table: talking to a separate
executable at arm's length would have kept the plugin MIT, at the cost of a process per question.
Direct calls won because reading a DSiWare save out of its NAND is something the plugin does while
drawing a page, not once per launch.

**Nothing else in the repository is affected.** The Flycast, PPSSPP and Xenia plugins share no code
with this one - the repository copies rather than links, as `docs/conception.md` explains - and they
remain MIT.
