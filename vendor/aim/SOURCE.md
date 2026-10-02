# AIM Toolkit build 20260420 - offered by the installer beside ImDisk

`files.cab` and `install.bat` exactly as shipped in `AIMtk.zip` (SHA-256
83B88E533DD10917EE38E6B2DE3181C7DABB830C9AFC76CEA366DDC662BBC400, https://sourceforge.net/projects/aim-toolkit/),
by the author of the ImDisk Toolkit. AIM is the successor ImDisk's own author points to.

INSTALLED WHOLE, the way `install.bat` does it: `extrac32 /e /l <dir> files.cab`, then
`config.exe /fullsilent /mountimg:1 /ramdiskui:1 /menu_entries:1 /shortcuts_desktop:0` - the Arsenal
Image Mounter driver (signed by Microsoft through its catalog), MountImg for images, VHD, VHDX, VMDK...
(the virtual disk formats through DiscUtils, which needs .NET Framework 4.8 - built into Windows 10
1903 and later), RamDiskUI, and the Explorer menu entries. Only the desktop shortcuts are left out.
`install.bat` is not run; it is kept as the record of how the package installs itself.

THE RAM DISK USES IT FIRST since the helper 1.6 (tools\ramdisk-helper): aim_ll for the RAM disks, aim_cli
for disk images (with the DevIO driver, a manual service the helper starts). ImDisk is the fallback.

Licence: Arsenal Image Mounter is Copyright (c) Arsenal Consulting, Inc. (d/b/a Arsenal Recon),
AGPL-3.0 for open-source use - https://github.com/ArsenalRecon/Arsenal-Image-Mounter. The Toolkit's
own tools are by its author, https://sourceforge.net/projects/aim-toolkit/