# On hold: the "You need to format the disk" prompt on AIM RAM disks

Seen by Mehdi on 14/10, at two melonDS DSiWare launches (01:13 and 01:22). A blank AIM RAM disk made Windows ask to
format a drive: I: the first time, Z: the second. Clicking Cancel is harmless: the disk is the empty RAM disk, and the
helper formats it a moment later.

**Not reproduced since.** From 21:04 the same evening, no prompt with any helper version, so there is no way to tell
whether a fix works. Everything tried was reverted (Mehdi, 14/10): the helper is 1.14 everywhere. Pick this up again
if the prompt comes back.

## What was measured (14/10, drive letters polled every 40 ms)

The helper 1.14 mounts with `aim_ll -a -t vm -s <MB>M -o rem -m Z: -p "/fs:ntfs /q /y"`. The raw volume was seen
twice before its format:

1. **Windows' automatic letter.** The mount manager gave the new raw volume the first free letter (I:). `KeepOnly`
   takes it away, but only after the prompt has already shown.
2. **aim_ll itself.** It puts Z: on the raw volume, then formats it: Z: was raw for 380 ms.

**Our own access is not the cause.** The plugin writes only after the helper's "OK mount", once the volume is
NTFS (Z: raw 31.191, NTFS 31.572, melonDS "mounted" 31.649). The prompt is Explorer's reaction to a volume arriving.

## What was tried (all reverted)

- **Automatic mounting off around the mount** (`mountvol /N`, then `/E`): I: was gone (`-o rem` included), but the
  prompt came back for Z:.
- **Helper 1.15:** automatic mounting off **and** the disk formatted in an empty folder of its own, its letter given
  only afterwards (`SetVolumeMountPoint`). No prompt at 21:04, but no prompt either at 21:08 and 21:10 with 1.14 back
  in place: the test proved nothing. Commits 64f4149 (plugins), 971ce6e (ExtendDB), 2d80d8e (LiteBox), all reverted.
- **Windows' letter memory:** `HKLM\SYSTEM\MountedDevices` holds `\DosDevices\Z:` for AIM disk unit 000000, and Y:
  for unit 000100. Deleting the Z: entry (backup in `C:\lbip-lab\MountedDevices-backup.reg`) did not bring the prompt
  back. aim_ll writes the entry again at the next mount.

## If it comes back

- First find out what makes it appear: repeated mounts with the letters and Explorer's dialogs watched. A small
  elevated test program, run by a scheduled task, was started and then dropped.
- Then measure the 1.15 approach against it. The reverted commit 64f4149 holds the code: `AutoMount` (with a marker
  file repaired at the next run) and `ToLetter`.

## Constraints

- **`-o rem` stays**, so that Everything does not index the RAM disk.
- **The helper is shared:** the same source is in `ExtendDB\RamDiskHelper`, and its exe is embedded in LiteBox
  (`thirdparty\RamDiskHelper.exe`). A new version means a version bump, with the three updated together.
- **Never run the helper by hand to see whether it starts:** an unknown action is a mount.
