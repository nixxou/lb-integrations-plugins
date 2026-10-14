# To do: the "You need to format the disk" prompt on AIM RAM disks

Seen by Mehdi on 14/10, at a melonDS DSiWare launch. Each blank AIM RAM disk makes Windows ask to format a drive: I:
first, then Z: once the first cause was fixed. Clicking Cancel is harmless: the disk is the empty RAM disk, and the
helper formats it a moment later.

## Measured (14/10, drive letters polled every 40 ms)

The helper 1.14 mounts with `aim_ll -a -t vm -s <MB>M -o rem -m Z: -p "/fs:ntfs /q /y"`. There are two causes:

1. **Windows' automatic letter.** The mount manager gives the new raw volume the first free letter (I:). `KeepOnly`
   takes it away, but only after the prompt has already shown.
2. **aim_ll itself.** It puts Z: on the raw volume, then formats it: Z: was raw for 380 ms.

## Tried and reverted (helper 1.15, not shipped)

Windows' automatic mounting was turned off around a blank AIM mount (`mountvol /N`, then `/E`). A marker file beside
the exe let the next run turn it back on if the helper died in between.

- I: was gone, `-o rem` included.
- `NoAutoMount` went back to 0.
- The prompt came back for Z:, because of cause 2.

Mehdi asked to revert. The source and the deployed exe are back at 1.14.

## Next idea, not tried

Turn automatic mounting off **and** format out of sight:

1. Mount the blank disk in an empty folder: `aim_ll -m <folder> -p …`. Explorer asks nothing of a volume mounted in
   a folder.
2. Once it is formatted, give it its letter (`SetVolumeMountPoint`).
3. Take the folder's mount point off.

This has to be measured first: no prompt at all, the letter right, the dismount unchanged.

## Constraints

- **`-o rem` stays**, so that Everything does not index the RAM disk.
- **The helper is shared:** the same source is in `ExtendDB\RamDiskHelper`, and its exe is embedded in LiteBox
  (`thirdparty\RamDiskHelper.exe`). A new version means a version bump, with the three updated together.
- **Never run the helper by hand to see whether it starts:** an unknown action is a mount.
