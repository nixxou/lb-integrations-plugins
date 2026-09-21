// A minimal IEmulator the probe can hand to a plugin. Auto-properties throughout; only Title and
// ApplicationPath are ever set, because those are the only two a plugin reads when deciding whether
// an emulator is its own.

using System;
using System.Collections.Generic;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal sealed class StubEmulator : IEmulator
    {
        private readonly List<IEmulatorPlatform> _platforms = new List<IEmulatorPlatform>();

        public string Id { get; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string ApplicationPath { get; set; } = "";
        public string CommandLine { get; set; } = "";
        public string DefaultPlatform { get; set; } = "";

        public bool AggressiveWindowHiding { get; set; }
        public bool AutoExtract { get; set; }
        public bool DisableShutdownScreen { get; set; }
        public bool EnableHardcoreAchievements { get; set; }
        public bool FileNameWithoutExtensionAndPath { get; set; }
        public bool HideAllNonExclusiveFullscreenWindows { get; set; }
        public bool HideConsole { get; set; }
        public bool HideMouseCursorInGame { get; set; }
        public bool NoQuotes { get; set; }
        public bool NoSpace { get; set; }
        public bool UseStartupScreen { get; set; }
        public int StartupLoadDelay { get; set; }

        public string AutoHotkeyScript { get; set; }
        public string ExitAutoHotkeyScript { get; set; }
        public string LoadStateAutoHotkeyScript { get; set; }
        public string PauseAutoHotkeyScript { get; set; }
        public string ResetAutoHotkeyScript { get; set; }
        public string ResumeAutoHotkeyScript { get; set; }
        public string SaveStateAutoHotkeyScript { get; set; }
        public string SwapDiscsAutoHotkeyScript { get; set; }

        public IEmulatorPlatform AddNewEmulatorPlatform()
        {
            var p = new StubEmulatorPlatform(Id);
            _platforms.Add(p);
            return p;
        }

        public IEmulatorPlatform[] GetAllEmulatorPlatforms() => _platforms.ToArray();

        public bool TryRemoveEmulatorPlatform(IEmulatorPlatform emulatorPlatform) => _platforms.Remove(emulatorPlatform);
    }

    internal sealed class StubEmulatorPlatform : IEmulatorPlatform
    {
        public StubEmulatorPlatform(string emulatorId) { EmulatorId = emulatorId; }

        public string EmulatorId { get; }
        public string Platform { get; set; } = "";
        public string CommandLine { get; set; } = "";
        public bool IsDefault { get; set; }
        public bool M3uDiscLoadEnabled { get; set; }
        public bool? AutoExtract { get; set; }
    }
}
