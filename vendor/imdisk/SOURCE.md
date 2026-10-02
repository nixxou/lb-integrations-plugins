# ImDisk Virtual Disk Driver 2.1.2 - the files the installer can put on a machine

Taken unchanged from LTR Data's own package, `imdiskinst.exe` 2.1.2 (driver built 9 May 2026,
packaged 11 May 2026), downloaded from https://static.ltr-data.se/files/imdiskinst.exe
(SHA-256 11D3423F3971B4D7A2BBDA538E47998BE901199546B36455315F22D3FC8B8A04, signed by LTR Data).
It is a 7-Zip self-extractor that runs `install.cmd`, which installs `imdisk.inf`.

ONLY THE DRIVER, AND ONLY WHAT `imdisk.inf` COPIES ON x64: `imdisk.sys`, `imdisk.exe` and
`imdisk.cpl` (and their 32-bit copies for SysWOW64), the `imdsksvc.exe` service and
`uninstall_imdisk.cmd`. Not the ImDisk Toolkit - no RamDiskUI, no DiscUtils (.NET Framework 4.8),
no ImDiskTk-svc, no awealloc. The result is the same as running `imdiskinst.exe`, measured: every file
here is byte-identical to what that package left in System32, SysWOW64 and inf on the machine this
was taken from. The binaries carry Microsoft's Windows Hardware Compatibility signature.

`imdisk.inf` keeps its source layout (`sys\amd64\`, `cli\i386\` ...), because it names the files
by those paths. Only the x64 and i386 folders are kept: the installer is an x64 program.

Licence: the driver is LTR Data's, under the terms in `README.md` (permissive, with GPL-2.0 parts -
`LICENSE.md`). Source: https://github.com/LTRData/ImDisk
