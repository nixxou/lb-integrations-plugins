# Demul 0.7 build 251220 - the build Nixx-Demul installs

`demul_251220.7z` exactly as Batocera ships it in
`package/batocera/emulators/demul/demul_251220.7z` (https://github.com/batocera-linux/batocera.linux,
added there 2026-01-16 by "add hikaru, gaelco & cave sega systems back with demul & mame"):
3 015 006 bytes, SHA-256 D27023B24C0764BAB88DAB6C11902893D9061C2CB79D513B120509151144F659,
MD5 84B22D2D8B252D58E570A56AEC0C5471. `demul.exe` inside is dated 2025-12-20, MD5
AFF2F5EAFCB1C32C28DE1A98B6AE5526.

KEPT HERE because Batocera is the only place it is published: http://demul.emulation64.com/downloads/
stops at demul07_280418.7z (2018-04-28), and Batocera already removed every DEmul file once
(2024-10-09) before bringing them back. The plugin downloads this file from THIS repository and
checks the SHA-256 above before extracting it.

WHY THIS BUILD and not the site's, measured on 2026-10-05 by crossing the set names written in each
`demul.exe` with the MAME drivers in LaunchBox's MAME.xml: everything the 2018 build knows, plus
Virtual-On Force and the Hikaru check ROM (Hikaru 9/9), System SP 29/30 (2018: 10), CV1000 27/29
(25), Gaelco 6/6 (4), Naomi/Naomi 2/Atomiswave 300/302 (242). What it lacks is the 2018 build's
`arcade_compat.txt`, the only per-game list of what boots and what plays.

Licence: DEmul is closed-source freeware by the DEmul team (http://demul.emulation64.com/team/).
