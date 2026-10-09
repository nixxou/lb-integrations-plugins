// Every patch of the pack, with what the Compatibility page says about it (docs\compat-page-plan.md §3, §3b). The texts
// are the page's: the problem, an example, what the patch does, what happens without it. A new patch is added HERE first,
// then guarded by Compat.On(id) where it acts.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Compat
{
    internal static class CompatRegistry
    {
        public static CompatPatch? Find(string id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        public static string GroupTitle(CompatGroup g) => g switch
        {
            CompatGroup.Missing => "Missing in LaunchBox",
            CompatGroup.Bug => "LaunchBox bugs worked around",
            CompatGroup.Safeguard => "Safeguards",
            CompatGroup.Data => "Changes to your LaunchBox data",
            _ => "Hooks into LaunchBox",
        };

        public static readonly IReadOnlyList<CompatPatch> All = new List<CompatPatch>
        {
            // ── missing in LaunchBox ──
            new CompatPatch
            {
                Id = "desk", Group = CompatGroup.Missing, Title = "Saves from every emulator of a game",
                Problem = "LaunchBox only asks the emulator a game is set to about its saves. A save made by another emulator - with "
                        + "\"Launch With\", or after moving a game from one emulator to another - is never shown, never backed up when "
                        + "the game closes, and a restore writes it into the folders of the game's emulator.",
                Example = "Zelda is set to SUPER ZSNES. You play it once with ares (Launch With). Game Saves only shows SUPER ZSNES's "
                        + "save; ares's save is never backed up.",
                Does = "When the game's emulator is one of the pack, its plugin also lists the saves of every other emulator that covers "
                     + "the platform (the pack's, and LaunchBox's RetroArch, Dolphin and PCSX2), each on its own card with its "
                     + "emulator's name, and sends every action on it (backup, restore, delete) to the emulator that made it.",
                Without = "LaunchBox's own behaviour: only the saves of the game's emulator are shown and backed up. The patches that "
                        + "need this one are off too.",
            },
            new CompatPatch
            {
                Id = "retroarchView", Group = CompatGroup.Missing, Needs = "desk", Title = "RetroArch's states on a game of the pack",
                Problem = "LaunchBox's RetroArch integration finds its states folder from the RetroArch core named on the GAME's "
                        + "command line (\"-L cores\\...\"). On a game set to one of the pack's emulators there is no such core: it "
                        + "finds no folder, lists no state, and a restore of one writes nowhere.",
                Example = "Zelda is set to SUPER ZSNES and you saved a state in RetroArch with Snes9x: its save shows in Game Saves, "
                        + "its states do not.",
                Does = "When the pack asks RetroArch about such a game, it shows RetroArch the game as RetroArch would run it - its own "
                     + "command line for that platform - and everything else as it is.",
                Without = "RetroArch's states of those games are not listed, and cannot be restored from there.",
                Checks = new[] { "igame.cmdline", "ra.statics" }, BugTest = "ra.folder",
            },
            new CompatPatch
            {
                Id = "foreignSlots", Group = CompatGroup.Missing, Needs = "desk", Title = "A state of another emulator on a game whose emulator has no slots",
                Problem = "To restore a save state LaunchBox asks the GAME's emulator which slots there are, and refuses every state "
                        + "restore when it has none - even for a state another emulator made.",
                Example = "An Xbox game set to Cxbx (which has no states) shows xemu's states; restoring one says \"The emulator for "
                        + "this game/app doesn't appear to support save state restoration!\".",
                Does = "Such an emulator (bsnes, no$gba, Cxbx) offers slots 0 to 9; the state's own emulator then takes the slot "
                     + "picked or refuses it with its own numbers (\"xemu keeps each savestate in its own slot...\").",
                Without = "Those states cannot be restored from LaunchBox.",
            },

            // ── LaunchBox bugs worked around ──
            new CompatPatch
            {
                Id = "planA", Group = CompatGroup.Bug, Title = "A save taken into another card",
                Problem = "When Game Saves shows a save that LaunchBox has not recorded at the same place, LaunchBox looks for a backup "
                        + "of the same game with the same FILE NAME and puts the save into that backup's card - whatever emulator "
                        + "made either. It also records a game's saves on the version that runs the game's own file, and then counts "
                        + "that record as an extra backup.",
                Example = "Secret of Mana: RASnes9x's \"Secret of Mana (USA).srm\" has the name of a SUPER ZSNES backup. It vanished "
                        + "from Game Saves and showed inside SUPER ZSNES's card; ares's card said \"1 Backups\" with none.",
                Does = "Each save shown is recorded at its own place, on the same version LaunchBox would use, so LaunchBox finds it "
                     + "by its place and never matches it by name; leftover duplicates of the pack's own records are removed; a "
                     + "save that carries its card says so when LaunchBox asks.",
                Without = "Saves of different emulators with the same file name can disappear into one another's card, and backup "
                        + "counts can be wrong. Nothing is deleted, but what is shown can be wrong.",
                OffWarning = "Without it, saves of different emulators with the same name can be shown in the wrong card.",
                Checks = new[] { "lb.saves", "lb.applyPersisted" }, BugTest = "lb.mergeByName",
            },
            new CompatPatch
            {
                Id = "stockRestore", Group = CompatGroup.Bug, Needs = "desk", Title = "RetroArch and Dolphin restores on a game of the pack",
                Problem = "LaunchBox's RetroArch and Dolphin integrations restore a save into the folders of the GAME's emulator, not of "
                        + "the emulator that made the save.",
                Example = "Restoring RetroArch's Zelda save while Zelda is set to SUPER ZSNES: RetroArch finds no core and no folder; "
                        + "with two RetroArch installs, a save of the second was written over the first's.",
                Does = "The pack runs their own restore steps with the emulator the save came from - and, with two installs of the "
                     + "same emulator, the one the save's card lives in, or refuses when nothing says which.",
                Without = "Those restores are handed to RetroArch and Dolphin as they are: they fail, or write in the wrong place.",
                OffWarning = "Without it, a RetroArch or Dolphin restore on such a game can write over another install's save.",
                Checks = new[] { "ra.statics", "dolphin.statics" },
            },
            new CompatPatch
            {
                Id = "companionSignature", Group = CompatGroup.Bug, Title = "A change in a save's companion file not seen",
                Problem = "To decide whether a save changed - and must be backed up - LaunchBox only compares its main file, not the "
                        + "files that go with it: a cartridge's clock (.rtc), Satellaview data (.psr), the 64DD's clock...",
                Example = "Only the clock of a RASnes9x save changed: LaunchBox says \"backed up\" and refuses Backup; the new clock "
                        + "is never kept.",
                Does = "The pack's saves that have companions get a fingerprint over all their files.",
                Without = "A change in a companion alone is never backed up.",
            },
            new CompatPatch
            {
                Id = "companionRestore", Group = CompatGroup.Bug, Title = "Companion files not put back at a restore",
                Problem = "LaunchBox copies a save's companions into the vault at a backup, but puts none back at a restore.",
                Example = "Restoring a RASnes9x save: the .srm comes back, the .rtc stays the one it replaced - the game's clock from "
                        + "another moment.",
                Does = "The pack's restore puts the backup's companions back beside the save (it always checks first that it can "
                     + "write every file, so a restore is never half done).",
                Without = "A restored save can come back with another moment's clock or data.",
            },

            // ── safeguards ──
            new CompatPatch
            {
                Id = "overwriteWarning", Group = CompatGroup.Safeguard, Title = "Warn before an overwrite LaunchBox will not back up",
                Problem = "Before a restore LaunchBox backs up the restored card's own file - not the file that will be overwritten. "
                        + "The two differ when a state goes into another slot, or when a card without a live file (a manual import) is "
                        + "restored over another card's save: that file is replaced and kept nowhere.",
                Example = "You restore the backup of slot 2 into slot 1, which holds another state: the old slot 1 is gone - not in "
                        + "the states folder, not in the vault.",
                Does = "In LaunchBox the pack asks first: \"Slot 1 already holds another save... Replace it anyway?\" - only when the "
                     + "target exists, belongs to another card and differs. In BigBox, where no dialog fits, it refuses with a "
                     + "message. Lifted by itself from the LaunchBox version that fixes the bug.",
                Without = "LaunchBox's behaviour: the other save is replaced without a word, and not backed up.",
                OffWarning = "Without it, restoring into another occupied slot loses what was there, with no warning.",
                Checks = new[] { "lb.version" },
            },

            // ── changes to your LaunchBox data ──
            new CompatPatch
            {
                Id = "emulatorEntry", Group = CompatGroup.Data, Switchable = false, Title = "The emulator entry an install creates",
                Problem = "LaunchBox's own integrations build their emulator entry inside LaunchBox; a plugin cannot.",
                Example = "Installing ares from the pack.",
                Does = "The install adds the emulator to LaunchBox (LaunchBox's public plugin interface) with one platform row per "
                     + "system it runs - set as the platform's default only when no other emulator is - and a reinstall only "
                     + "updates the exe's path. The pack never writes LaunchBox's database or its XML files itself.",
                Without = "No install without it: shown for information.",
            },
            new CompatPatch
            {
                Id = "emulatorFields", Group = CompatGroup.Data, Title = "Fields filled on an emulator the pack drives",
                Problem = "Some fields of an emulator entry must be set for the pack's emulator to work from LaunchBox, and an entry "
                        + "made by hand, or by an older version, does not have them.",
                Example = "An emulator added by hand for ares, with no command line: each game would start on the wrong system. The "
                        + "exit script of an older version sent Alt+F4 to whatever window had the focus.",
                Does = "When it starts, each plugin fills only what is empty or what it wrote itself before: the AutoHotkey exit and "
                     + "savestate scripts, ares's \"--system\" platform lines, Cxbx's entry pointed at its loader, xemu's exe path "
                     + "after its rename (else LaunchBox's own xemu plugin takes the entry back), a hand-made Flycast entry's "
                     + "missing platform rows.",
                Without = "Those fields stay as they are; the plugin works with what it finds - some games may not start right.",
            },
            new CompatPatch
            {
                Id = "autoExtract", Group = CompatGroup.Data, Title = "\"Extract ROM archives\" forced on some emulators",
                Problem = "LaunchBox's \"Extract ROM archives\" option is wrong for some emulators of the pack.",
                Example = "Cxbx, Xenia and xemu unpack an archive themselves, to a RAM disk when they can: LaunchBox extracting it "
                        + "first writes the whole disc image to disk for nothing. no$gba cannot open an archive at all (\"Cartridge "
                        + "not found\").",
                Does = "Turned off for Cxbx, Xenia and xemu, on for no$gba - over the entry's own setting, on purpose.",
                Without = "The entry's setting is kept, with the costs above.",
            },
            new CompatPatch
            {
                Id = "importFixes", Group = CompatGroup.Data, Switchable = false, Title = "Games and versions corrected after an import",
                Problem = "LaunchBox's import drops some games silently, and guesses titles, regions and versions from file names.",
                Example = "Arcade sets like vf4b or clubkprz never added; a Vita game imported with \"(NoNpDrm) [v1.03]\" as its version.",
                Does = "Flycast adds the dropped arcade sets as versions of the right game; Xenia, Vita3K, Cxbx and PPSSPP set the "
                     + "title, region and version read from the game itself.",
                Without = "LaunchBox's import as it is.",
                OwnSwitches = "Each plugin's page: Flycast \"repair the import\"; Xenia, Vita3K, Cxbx, PPSSPP title / region options.",
            },
            new CompatPatch
            {
                Id = "gameCommandLine", Group = CompatGroup.Data, Switchable = false, Title = "A game's command line moved to its plugin",
                Problem = "Options of melonDS and Vita3K used to be written into a game's own command line.",
                Example = "A game's \"-f -l\" left from an older version.",
                Does = "When you press OK in that plugin's game window, its flags still in the game's command line are moved to the "
                     + "plugin's own store.",
                Without = "Only on your OK: shown for information.",
            },

            // ── hooks into LaunchBox ──
            new CompatPatch
            {
                Id = "rowInjection", Group = CompatGroup.Hook, Title = "The pack's emulators in LaunchBox's emulator lists",
                Problem = "LaunchBox's \"Add emulator\" list and its Associated Platforms grid come from its database, which knows "
                        + "neither Flycast, nor the standalone DS emulators, nor any \"Nixx-\" name.",
                Example = "Adding Flycast by hand: it is not in the list, and its platforms grid is empty.",
                Does = "A hook (Harmony) on LaunchBox's database reader adds the pack's emulator rows to what it reads - in memory "
                     + "only: the database file is never written.",
                Without = "The pack's emulators are missing from those lists (they still work once set up). Replaces the old "
                        + "\"no-metadata\" marker file.",
                Checks = new[] { "sqlite.reader" },
            },
            new CompatPatch
            {
                Id = "emulatorOpened", Group = CompatGroup.Hook, Title = "Knowing when an emulator is opened without a game",
                Problem = "LaunchBox's plugin interface has no event for \"Open emulator\" (Tools menu, emulator list).",
                Example = "Opening Cxbx from LaunchBox started its loader alone, which shows nothing.",
                Does = "A hook (Harmony) on the start of a program - only an exe started by LaunchBox's \"Open emulator\" - lets the "
                     + "plugins know it was opened and when it closed, and opens Cxbx's own window.",
                Without = "Opening Cxbx from LaunchBox starts the loader alone; the plugins do not know the emulator ran.",
                Checks = new[] { "lb.openEmulator" },
            },
            new CompatPatch
            {
                Id = "raConfigCache", Group = CompatGroup.Hook, Title = "RetroArch's settings read once, not once per game",
                Problem = "LaunchBox's RetroArch integration reads and parses its whole retroarch.cfg again for every game it is asked "
                        + "about (where are its saves, where are its states) - nothing in it depends on the game.",
                Example = "2,000 games listed with the saves desk on: about 6 seconds per RetroArch install spent re-reading the same "
                        + "3,400-line file.",
                Does = "A hook (Harmony) on that integration's settings reader keeps the file once read, and reads it again only when it "
                     + "changes (its date or size). Nothing is written.",
                Without = "The same answers, slower: the file is read again for each game.",
                Checks = new[] { "ra.loadConfig" },
            },
            new CompatPatch
            {
                Id = "importWizard", Group = CompatGroup.Hook, Switchable = false, Title = "LaunchBox's import wizard steered",
                Problem = "LaunchBox's ROM import lists files it should not (updates, DLC, a whole MAME folder) and mishandles some "
                        + "platforms.",
                Example = "Choosing the Vita platform made the wizard list the games installed in Vita3K and ignore the files you "
                        + "picked; importing Naomi games offered a whole MAME folder.",
                Does = "Inside the wizard: Vita3K and Xenia swap the platform for a temporary one while it scans; Vita3K, Xenia, Cxbx "
                     + "and PPSSPP remove updates, DLC, firmware, save data and foreign files from the list; Flycast and Demul keep "
                     + "only their arcade sets. A watcher notices the files LaunchBox drops at the end (read only).",
                Without = "LaunchBox's wizard as it is.",
                OwnSwitches = "Each plugin's page: Vita3K \"bypass\" and \"clean the list\", Xenia / Cxbx / PPSSPP import options, "
                            + "Flycast and Demul \"sort the import\".",
                Checks = new[] { "lb.wizard" },
            },
        };
    }
}
