# `src/Vita3k` is GPL-2.0-or-later

The rest of this repository is MIT unless it says otherwise. This plugin is not, and the reason is
worth stating rather than leaving to be discovered.

`Vita3k.dll` loads `vita3k-install.native` into its own process, and that library is built from
[Vita3K](https://github.com/Vita3K/Vita3K)'s own install code - its firmware installer and the PFS
decryptor it carries - which is GPL-2.0-or-later. Loading it makes the two one work, so this plugin is
distributed under the same terms. The licence text is at `tools/vita3k-install/LICENSE`.

This was a deliberate choice, made with the alternative on the table and built first: an executable
run at arm's length kept the plugin MIT, and worked. The library won because installing a game and a
firmware is something the user watches, and a call in the same process can report how far it has got
where a child process could only print lines to be parsed - see `Vita3kProgressWindow.cs` for the
game, and the host's own install window for the firmware.

Running Vita3K's code inside the host has a cost, and it is paid here rather than ignored: every
export runs under structured exception handling, so an access violation in that code becomes a failed
install and not a crashed front end, and the library is loaded and freed around every call, because
the firmware code keeps a static counter that only a fresh load resets.

**Nothing else in the repository is affected.** The other plugins share no code with this one that
touches the library. `src/Shared.Lbip`, `src/Shared.Snapshot` and `src/Shared.Psf` are compiled into
this plugin too, but they are this repository's own work and MIT like the rest of it - the licence
follows the combined plugin, not the shared folders it is built from.
