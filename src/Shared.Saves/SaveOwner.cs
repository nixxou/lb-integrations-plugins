// Who owns a save row - read from the row's id, never from the exe (Mehdi, 08/10).
//
// The exe cannot say it: a row's EmulatorFileName holds the exe's FILE NAME only ("retroarch.exe",
// measured 08/10), and two plugins may one day drive the same exe (a Nixx-DuckStation beside
// LaunchBox's own DuckStation integration). The id can: ours carries its owner in "emu", and
// LaunchBox's integrations write forms of their own.
//
//   1. our JSON                         → its emu: a Nixx plugin, or "lb.<name>" for a translated id
//   2. a known native form              → that LaunchBox integration
//   3. LiteBox's entry:/unit: forms     → LiteBox (it routes those itself)
//   4. anything else (a bare GUID...)   → unknown: the caller asks LaunchBox which plugin it picked
//                                         for the game's emulator (GetBestEmulatorPlugin)

#nullable enable

using System;

namespace LbIntegrations.Saves
{
    internal enum SaveOwnerKind { Unknown, Nixx, LaunchBox, LiteBox }

    internal readonly struct SaveOwner
    {
        public SaveOwner(SaveOwnerKind kind, string? name) { Kind = kind; Name = name; }

        public SaveOwnerKind Kind { get; }
        /// <summary>The owner's emu value ("ares", "lb.pcsx2"...); null when unknown.</summary>
        public string? Name { get; }

        /// <summary>The prefix that marks a LaunchBox integration in our ids.</summary>
        public const string LaunchBoxPrefix = "lb.";

        /// <summary>The owner of a row whose SaveGroupId is <paramref name="saveGroupId"/>.</summary>
        public static SaveOwner Of(string? saveGroupId)
        {
            if (string.IsNullOrEmpty(saveGroupId)) return default;
            if (SaveId.TryRead(saveGroupId, out var d))
            {
                var emu = d!.Emu;
                if (string.IsNullOrEmpty(emu)) return default;
                return emu.StartsWith(LaunchBoxPrefix, StringComparison.Ordinal)
                    ? new SaveOwner(SaveOwnerKind.LaunchBox, emu)
                    : new SaveOwner(SaveOwnerKind.Nixx, emu);
            }
            foreach (var (prefix, owner) in NativeForms)
                if (saveGroupId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new SaveOwner(SaveOwnerKind.LaunchBox, owner);
            if (saveGroupId.StartsWith("entry:", StringComparison.Ordinal) || saveGroupId.StartsWith("unit:", StringComparison.Ordinal))
                return new SaveOwner(SaveOwnerKind.LiteBox, "litebox");
            return default;
        }

        /// <summary>The native id forms of LaunchBox's integrations that name their owner on their own (measured
        /// 07/10). A bare GUID is NOT here: RetroArch writes them, and nothing proves no other integration does.</summary>
        private static readonly (string Prefix, string Owner)[] NativeForms =
        {
            ("pcsx2:", "lb.pcsx2"),
            ("saturn-", "lb.retroarch"),
        };
    }
}
