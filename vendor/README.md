# Vendored LaunchBox plugin SDK

`Unbroken.LaunchBox.Plugins.dll` — assembly version **14.0.1.2** (LaunchBox 14.0.1), 122 168 bytes,
MD5 `bf7602fbbe2830582e0f82195be8019b`. Copied on 08/10 from a LaunchBox 14.0.1 install
(`G:\LB1326\Core\` on the machine this repo was started on).

**Why 14 and no longer 13.27.** Until 08/10 the plugins were built net9 against the
13.27 SDK, the oldest they needed, so one build loaded under LaunchBox 13.27 (.NET 9),
13.28 and 14 (.NET 10). Every install is LaunchBox 14 now and LiteBox goes net10-only
on this same SDK, so the plugins follow: net10.0-windows against 14.0.1.2, which itself
references System.Runtime 10 (a net9 project cannot compile against it). .NET 9 and
LaunchBox 13 hosts are no longer supported. One thing to know about this SDK: the save
rows (`GameSaveBase` / `GameSaveGame` / `GameSaveState`) have init-only properties, so a
row is built in one object initializer.

It is committed rather than referenced from an install so the repo builds on a
machine with no LaunchBox on it.

**Never ship this file.** Every project references it with `<Private>false</Private>`:
the host provides the SDK at runtime, and a second copy loaded from the plugin's
own folder would give the plugin *different* `EmulatorPlugin` / `IEmulator` types
than the host's, so the host's `obj is EmulatorPlugin` test would silently fail
and the plugin would never register.
