# `src/Xemu` is GPL-2.0-or-later

The rest of this repository is MIT unless it says otherwise. This plugin is not, and the reason is
worth stating rather than leaving to be discovered.

`Xemu.dll` sets the region of the console a game runs on, and an original Xbox keeps it encrypted:
in the security section of its EEPROM, RC4 under a key derived by an HMAC-SHA1 with the Xbox's own
constants. The code that does it - `Eeprom/HmacSha1.cs`, `Eeprom/RC4.cs`, `Eeprom/EepromVersion.cs` and
the time zone table in `Eeprom/XemuEeprom.cs` - comes from
[XboxEepromEditor](https://github.com/Ernegien/XboxEepromEditor) (fork:
[nixxou/XboxEepromEditor](https://github.com/nixxou/XboxEepromEditor)), which is GPL-2.0-or-later.
Compiled in, it makes the plugin one work with it, so the plugin is distributed under the same terms.
The licence text is at `Eeprom/LICENSE.txt`.

This was a choice, made with the alternative on the table (Mehdi, 04/10): the algorithm is short
enough to have been written again from its description, which would have kept the plugin MIT. Taking
the code that has been decrypting real consoles' EEPROMs for years was preferred to a rewrite that
would have to earn that trust - and the probe holds it to the real thing: an EEPROM xemu made, opened
and sealed again unchanged, gives back its very bytes (`--xemu`).

**Nothing else in the repository is affected.** The shared folders compiled into this plugin
(`src/Shared.*`, and the Cxbx plugin's `Xbe.cs`, `Xdvdfs.cs`, `GitHubReleases.cs`, `Archives.cs`) are
this repository's own work and keep their licences - the licence follows the combined plugin, not the
folders it is built from. `src/Xemu/ExfatOneFileView.cs`, compiled into the RAM disk helper too, is
this repository's own as well and carries no GPL code.
