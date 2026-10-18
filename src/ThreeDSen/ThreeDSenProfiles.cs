// 3dSen's games: its PROFILES. 3dSen does not take a ROM and draw it in 3D - it has a 3D profile per game (101 built in,
// more as community .3dn files), and the user binds one ROM file to each (rom.json, ThreeDSenRomJson). A launch is
// "3dSen.exe -id=<profile id>" (docs\3dsen.md section 3). So the one question of a launch is: which profile is this game?
//
//   1. The game's own choice, made in its window (Nixx-3DSen : Options..., key "Profile" in games\<game id>.ini).
//   2. Else its title - then its file name - matched against the profiles' titles, normalised (LbipTitleNormalizer, as
//      LaunchBox's CompareName) and through the aliases below: 3dSen spells some titles its own way ("Digdug", "Pacman",
//      "Adv of Lolo", "Kungfu / Spartan X"). The WHOLE title only: the text before a ":" would make "Batman: Return of
//      the Joker" the profile "Batman" (another game). One profile must answer; two answering is no answer.
//   3. Else no profile: "2D Mode" (id 47, 3dSen's plain 2D emulator, any ROM) - Mehdi 19/10 (question Q5 of
//      docs\3dsen.md): a game 3dSen has no 3D data for still plays, flat. The match never picks 2D Mode itself.
//
// NOTHING CHECKS THAT THE ROM IS THE PROFILE'S GAME (docs\3dsen.md section 2: no CRC nor hash anywhere in 3dSen's code):
// a wrong match plays the ROM with another game's 3D data. Hence the exact matching and the refusal rather than a guess.
//
// THE LIST: rom.json's items once 3dSen has run (it holds every profile, the custom ones too, each with its title), and the
// 101 built-in profiles of 1.1.2 below for what rom.json does not hold (3dSen never run yet). Read in 3dSen 1.1.2's own
// rom.json (written 18/10/2026, 101 items) - the same table as docs\3dsen.md section 2.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;

namespace LbIntegrations.ThreeDSen
{
    internal sealed class ThreeDSenProfile
    {
        public string Id, Title;
        public bool BuiltIn;
        public override string ToString() => Title + " (" + Id + ")";
    }

    internal static class ThreeDSenProfiles
    {
        public const string TwoDModeId = "47";

        /// <summary>3dSen PC 1.1.2's built-in profiles, as its rom.json lists them.</summary>
        internal static readonly (string Id, string Title)[] BuiltIn =
        {
            ("1", "Arkanoid"), ("2", "Balloon Fight"), ("3", "Batman"), ("4", "Battle Kid"), ("5", "Battle City"), ("6", "Bomberman"),
            ("7", "Castlevania"), ("8", "Circus Charlie"), ("9", "Contra"), ("10", "Digdug"), ("11", "Donkey Kong"), ("12", "Donkey Kong 3"),
            ("13", "Donkey Kong Jr"), ("14", "Dr Mario"), ("15", "Duck Hunt"), ("16", "Elevator Action"), ("17", "Excitebike"), ("18", "Galaga"),
            ("19", "Gyromite"), ("20", "Hogan's Alley"), ("21", "Ice Climber"), ("22", "Journey To Silius"), ("23", "Shadow Of Ninja / Kage"),
            ("24", "Kungfu / Spartan X"), ("25", "Legacy Of The Wizard"), ("26", "Legend Of Kage"), ("27", "Lode Runner"), ("28", "Mappy"),
            ("29", "Mario Bros."), ("30", "Mega Man 1"), ("31", "Mega Man 2"), ("32", "Metroid"), ("33", "Pacman"), ("34", "Popeye"),
            ("35", "Road Fighter"), ("36", "Super Bat Puncher"), ("37", "Super Mario Bros."), ("38", "Tetris - Nintendo"), ("39", "Tetris - Tengen"),
            ("40", "Urban Champion"), ("41", "Wrecking Crew"), ("42", "Yie Ar Kungfu"), ("43", "Bubble Bobble"), ("44", "Wild Gun Man"),
            ("45", "Legend Of Zelda"), ("46", "Zooming Secretary"), ("47", "2D Mode"), ("48", "City Connection"), ("49", "Micro Mages"),
            ("50", "TwinBee"), ("51", "Pinball"), ("52", "Flappy Bird"), ("53", "Flappy Bird FPV"), ("54", "Shatterhand"), ("55", "Super Mario Bros. 3"),
            ("56", "Goonies"), ("57", "Pooyan"), ("58", "Chip & Dale Rescue Ranger"), ("59", "Duck Tales"), ("60", "Adv of Lolo"),
            ("61", "Antarctic Adventure"), ("62", "Clu Clu Land"), ("63", "Spy Hunter"), ("64", "Mega Man 3"), ("65", "Ghosts N Goblins"),
            ("66", "Adv Of Lolo 2"), ("67", "Adv Of Lolo 3"), ("68", "Duck Tales 2"), ("69", "Adventure Island"), ("70", "Snow Brothers"),
            ("71", "Jackie Chan's Action KungFu"), ("72", "Depths"), ("73", "Zelda II: The Adventure of Link"), ("74", "Castlevania II: Simon's Quest"),
            ("75", "Solomon's Key"), ("76", "Fire 'n Ice"), ("77", "Final Fantasy"), ("78", "Dragon Warrior I"), ("79", "Dragon Warrior II"),
            ("80", "Felix the Cat"), ("81", "Gradius"), ("82", "Burger Time"), ("83", "Life Force"), ("84", "Ninja Gaiden III"), ("85", "Blaster Master"),
            ("86", "Super Spike V'ball"), ("87", "Punch Out!!"), ("88", "Lunar Pool"), ("89", "Moai-kun"), ("90", "Binary Land"), ("91", "Flippull"),
            ("92", "Nuts & Milk"), ("93", "Quarth"), ("94", "Puzznic"), ("95", "Palamedes"), ("96", "Yoshi"), ("97", "Flappy"),
            ("98", "Tetris - Famicom"), ("99", "Dragon Warrior III"), ("100", "Tiny Toon Adventures"), ("101", "Jackal"),
        };

        /// <summary>The titles LaunchBox (and No-Intro file names) give those games where 3dSen spells them otherwise. Only
        /// names of the same game: a title in two profiles' lists matches neither.</summary>
        internal static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>
        {
            ["3"] = new[] { "Batman: The Video Game" },
            ["4"] = new[] { "Battle Kid: Fortress of Peril" },
            ["10"] = new[] { "Dig Dug" },
            ["13"] = new[] { "Donkey Kong Junior" },
            ["18"] = new[] { "Galaga: Demons of Death" },
            ["23"] = new[] { "Shadow of the Ninja", "Blue Shadow", "Kage" },
            ["24"] = new[] { "Kung Fu", "Spartan X" },
            ["25"] = new[] { "Dragon Slayer IV: Drasle Family" },
            ["30"] = new[] { "Mega Man", "Rockman" },
            ["31"] = new[] { "Rockman 2: Dr. Wily no Nazo" },
            ["33"] = new[] { "Pac-Man" },
            ["38"] = new[] { "Tetris" },
            ["39"] = new[] { "Tetris: The Soviet Mind Game" },
            ["42"] = new[] { "Yie Ar Kung-Fu" },
            ["44"] = new[] { "Wild Gunman" },
            ["58"] = new[] { "Chip 'n Dale: Rescue Rangers", "Chip 'n Dale Rescue Rangers" },
            ["59"] = new[] { "DuckTales" },
            ["60"] = new[] { "Adventures of Lolo" },
            ["64"] = new[] { "Rockman 3: Dr. Wily no Saigo!?" },
            ["66"] = new[] { "Adventures of Lolo 2" },
            ["67"] = new[] { "Adventures of Lolo 3" },
            ["68"] = new[] { "DuckTales 2" },
            ["69"] = new[] { "Hudson's Adventure Island" },
            ["70"] = new[] { "Snow Bros." },
            ["71"] = new[] { "Jackie Chan's Action Kung Fu" },
            ["76"] = new[] { "Solomon's Key 2" },
            ["78"] = new[] { "Dragon Warrior", "Dragon Quest" },
            ["79"] = new[] { "Dragon Quest II" },
            ["82"] = new[] { "BurgerTime" },
            ["83"] = new[] { "Salamander" },
            ["84"] = new[] { "Ninja Gaiden III: The Ancient Ship of Doom" },
            ["87"] = new[] { "Mike Tyson's Punch-Out!!", "Punch-Out!! Featuring Mr. Dream" },
            ["91"] = new[] { "Flipull" },
            ["99"] = new[] { "Dragon Quest III" },
        };

        /// <summary>Every profile: rom.json's (with their current titles), then the built-in ones it does not hold.</summary>
        public static List<ThreeDSenProfile> All(string dataDir)
        {
            var list = new List<ThreeDSenProfile>();
            var builtIn = new HashSet<string>(BuiltIn.Select(b => b.Id));
            try
            {
                foreach (var item in ThreeDSenRomJson.Items(Path.Combine(dataDir ?? "", ThreeDSenRomJson.FileName)))
                    if (!string.IsNullOrEmpty(item.Id) && !list.Any(p => p.Id == item.Id))
                        list.Add(new ThreeDSenProfile { Id = item.Id, Title = string.IsNullOrWhiteSpace(item.Title) ? Title(item.Id) ?? item.Id : item.Title, BuiltIn = builtIn.Contains(item.Id) });
            }
            catch (Exception ex) { Log.Warn("the profiles of rom.json could not be read", ex); }
            foreach (var (id, title) in BuiltIn)
                if (!list.Any(p => p.Id == id)) list.Add(new ThreeDSenProfile { Id = id, Title = title, BuiltIn = true });
            return list.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Title(string id) => BuiltIn.FirstOrDefault(b => b.Id == id).Title;

        /// <summary>The comparison key of a title: LaunchBox's CompareName (brackets, punctuation, articles out, numerals
        /// as digits), then letters and digits only.</summary>
        internal static string Key(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var s = LbipTitleNormalizer.PerformSanitize(title);
            var key = LbipTitleNormalizer.NormalizeCompareName(s, out _);
            return string.IsNullOrEmpty(key) ? null : key;
        }

        /// <summary>The profile a game's title or file name names, or null: exactly one profile must answer. 2D Mode never.</summary>
        public static ThreeDSenProfile Match(IReadOnlyList<ThreeDSenProfile> profiles, string title, string romPath)
        {
            var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);   // key -> profile ids
            void Name(string text, string id)
            {
                var k = Key(text);
                if (k == null) return;
                if (!names.TryGetValue(k, out var set)) names[k] = set = new HashSet<string>();
                set.Add(id);
            }
            foreach (var p in profiles)
            {
                if (p.Id == TwoDModeId) continue;
                Name(p.Title, p.Id);
                var builtInTitle = Title(p.Id);
                if (p.BuiltIn && builtInTitle != null) Name(builtInTitle, p.Id);
                if (p.BuiltIn && Aliases.TryGetValue(p.Id, out var more)) foreach (var a in more) Name(a, p.Id);
            }

            var candidates = new List<string>();
            void Try(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                candidates.Add(text);
            }
            Try(title);
            try { Try(Path.GetFileNameWithoutExtension(romPath ?? "")); } catch { }

            foreach (var c in candidates)
            {
                var k = Key(c);
                if (k == null || !names.TryGetValue(k, out var ids)) continue;
                if (ids.Count == 1) return profiles.First(p => p.Id == ids.First());
                Log.Info("\"" + c + "\" names " + ids.Count + " 3dSen profiles (" + string.Join(", ", ids) + ") - none chosen");
                return null;
            }
            return null;
        }
    }
}
