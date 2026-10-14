# Fixed in helper 1.15: the "You need to format the disk" prompt on AIM RAM disks

Seen by Mehdi on 14/10, at a melonDS DSiWare launch. Each blank AIM RAM disk made Windows ask to format a drive: I:
first, then Z: once the first cause was fixed. Clicking Cancel was harmless: the disk is the empty RAM disk, and the
helper formats it a moment later.

## Measured (14/10, drive letters polled every 40 ms)

The helper 1.14 mounts with `aim_ll -a -t vm -s <MB>M -o rem -m Z: -p "/fs:ntfs /q /y"`. There were two causes:

1. **Windows' automatic letter.** The mount manager gives the new raw volume the first free letter (I:). `KeepOnly`
   takes it away, but only after the prompt has already shown.
2. **aim_ll itself.** It puts Z: on the raw volume, then formats it: Z: was raw for 380 ms.

## First try (automatic mounting off only)

Windows' automatic mounting turned off around a blank AIM mount (`mountvol /N`, then `/E`):

- I: was gone, `-o rem` included;
- `NoAutoMount` went back to 0;
- the prompt came back for Z:, because of cause 2.

## The fix (helper 1.15)

Both causes together, for every AIM mount that formats onto a letter:

1. **Automatic mounting off** (`mountvol /N`) when it was on, back on (`mountvol /E`) once the disk has its letter.
   A marker beside the exe (`automount-off.marker`) says it is the helper's to put back: a helper that died in between
   is repaired by its next run, whatever the action (`AutoMount.Repair`, first thing in `Main`). A machine whose
   automatic mounting was already off is left as it is.
2. **Formatted out of sight:** the disk is mounted and formatted in an empty folder of its own
   (`<helper>\staging\<guid>`, where Explorer asks nothing), then given its letter (`SetVolumeMountPoint`), and the
   folder's mount point is taken off (`ToLetter`). If the letter cannot be set, the disk is dismounted again: nothing
   half-made is left. The result line says `staged=1`.

ImDisk's disks are not Plug and Play, so they are untouched. A mount into a folder (`mount=`) was never prompted and
is untouched too.

**Measured 14/10, 21:04:** melonDS DSiWare launch, `OK mount Z exit=0 backend=aim staged=1`, no prompt, then a clean
dismount (`locked=1 dismounted=1`).

## Constraints

- **`-o rem` stays**, so that Everything does not index the RAM disk.
- **The helper is shared:** the same source is in `ExtendDB\RamDiskHelper`, and its exe is embedded in LiteBox
  (`thirdparty\RamDiskHelper.exe`). The three were updated together for 1.15.
- **No new cfg key:** callers need no version check for 1.15.
- **Never run the helper by hand to see whether it starts:** an unknown action is a mount.
