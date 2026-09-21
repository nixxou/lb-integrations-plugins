# Vendored LaunchBox plugin SDK

`Unbroken.LaunchBox.Plugins.dll` — assembly version **13.27.0.0**, 102 400 bytes,
MD5 `1adef83b3adf2dcad374cb1ff0b1458a`. Copied from a LaunchBox 13.27 install
(`G:\LB\Core\` on the machine this repo was started on).

**Why 13.27 and not the newest.** A plugin compiled against the *oldest* SDK it
needs loads on every later LaunchBox; the reverse is not true. 13.27 is the last
version before the SDK gained `IPluginPaths` (constructor injection, LaunchBox
14), and nothing here needs it. The same assembly therefore loads under
LaunchBox 13.27 (.NET 9), 13.28 (.NET 10) and 14, and under LiteBox.

It is committed rather than referenced from an install so the repo builds on a
machine with no LaunchBox on it.

**Never ship this file.** Every project references it with `<Private>false</Private>`:
the host provides the SDK at runtime, and a second copy loaded from the plugin's
own folder would give the plugin *different* `EmulatorPlugin` / `IEmulator` types
than the host's, so the host's `obj is EmulatorPlugin` test would silently fail
and the plugin would never register.
