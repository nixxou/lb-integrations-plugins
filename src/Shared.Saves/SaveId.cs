// The SaveGroupId of every save row the pack writes: one JSON object, one serializer.
//
// WHY JSON. LaunchBox keeps a row's SaveGroupId for ever and compares it as a STRING (measured
// 07/10, docs\saves-lb14-routing.md §1). A JSON object holds every field we need without inventing a
// grammar of ':' and '#'.
//
// AN IDENTITY AND NOTHING ELSE (Mehdi, 08/10). The id says WHICH save this is - its owner, its kind,
// what names it - and every field of it is stable: the listing must give the very same string every
// time, or LaunchBox takes it for another save. Where the save came from (its game, its version) is
// NOT here: the row already carries it (GameId, AdditionalApplicationId), and LaunchBox and LiteBox
// keep it up to date there (LiteBox's Make Default and combine re-point rows). A copy frozen in the id
// would only drift from it - and change the id when it did. No fingerprint of the rom either: reading
// roms at every listing cost gigabytes at each LaunchBox start, for two rare cases.
//
// WHY System.Text.Json AND NOTHING OF OURS (Mehdi, 08/10). Writing and reading JSON is the library's
// job. What is ours is only WHICH fields exist and IN WHAT ORDER - [JsonPropertyOrder] below - and
// the encoder's settings. Two identical states therefore always give the same string. The probe's
// --saves-selftest pins exact strings, so a .NET update that changed one byte would be seen there.
//
// THE ESCAPES. Every Unicode range is allowed through, so a name stays readable in the XML. '#' and
// '|' are forbidden, so the library writes them # and |: LiteBox cuts an id at '#' (its
// #cN / #web / #b:<name> decorations) and at '|'. Whatever the settings, .NET also escapes the quote
// and the HTML-sensitive characters, and writes characters outside the BMP (emoji) as a surrogate
// pair: stable, only less readable, and pinned by the self-test like the rest.
//
// A '#' can therefore never occur inside our JSON, so anything from the first '#' on is a decoration
// somebody else appended: TryRead reads what comes before it.

#nullable enable

using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace LbIntegrations.Saves
{
    /// <summary>One save row's identity. A field that does not apply stays null and is left out.
    /// The order of the properties IS the order in the string - never reorder them.</summary>
    internal sealed class SaveIdData
    {
        /// <summary>Who owns the row: a Nixx plugin ("ares", "xemu", "cxbx"...), one value per plugin, or
        /// "lb.&lt;name&gt;" for a LaunchBox integration ("lb.retroarch", "lb.pcsx2"...).</summary>
        [JsonPropertyOrder(0)] public string? Emu { get; init; }
        /// <summary>save, state, console, nand, vmu, nvram.</summary>
        [JsonPropertyOrder(1)] public string? Kind { get; init; }
        /// <summary>The system, only for a plugin that runs several.</summary>
        [JsonPropertyOrder(2)] public string? Sys { get; init; }
        /// <summary>What <see cref="Id"/> is: name, title, disc, product, set.</summary>
        [JsonPropertyOrder(3)] public string? By { get; init; }
        [JsonPropertyOrder(4)] public string? Id { get; init; }
        /// <summary>The emulator's own slot number.</summary>
        [JsonPropertyOrder(5)] public int? Slot { get; init; }
        /// <summary>A LaunchBox integration's own id, kept exactly so the way back is exact.</summary>
        [JsonPropertyOrder(6)] public string? Native { get; init; }
        /// <summary>The folder of a LaunchBox integration's file named by its name alone (by "name", no native id): relative
        /// to LaunchBox's root, lower case. Two of RetroArch's cores keep a state of one name in two folders
        /// (sort_savestates_enable): by name only, they were one card (review 10/10).</summary>
        [JsonPropertyOrder(7)] public string? Dir { get; init; }
        /// <summary>A hint of the install a pack plugin's card was made on (docs\saves-two-installs.md §4): a CRC32 of the folder
        /// of the emulator's exe (InstallHint), 8 hex digits. Two installs of one plugin list a save of one name: without it, one
        /// id, one card, the second install's live file taken for a backup (measured 12/10). Set when a card is made, never
        /// corrected, never a destination - only a tiebreak between installs.</summary>
        [JsonPropertyOrder(8)] public string? Inst { get; init; }
    }

    internal static class SaveId
    {
        private static readonly JsonSerializerOptions Options = MakeOptions();

        private static JsonSerializerOptions MakeOptions()
        {
            var allowed = new TextEncoderSettings();
            allowed.AllowRange(UnicodeRanges.All);
            allowed.ForbidCharacters('#', '|');
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                Encoder = JavaScriptEncoder.Create(allowed),
            };
        }

        /// <summary>The id string of <paramref name="data"/>.</summary>
        public static string Write(SaveIdData data) => JsonSerializer.Serialize(data, Options);

        /// <summary>True, with the data, when <paramref name="saveGroupId"/> is one of our JSON ids. Never throws:
        /// anything else (pcsx2:..., saturn-..., a GUID, LiteBox's entry:/unit:) answers false at once.</summary>
        public static bool TryRead(string? saveGroupId, out SaveIdData? data)
        {
            data = null;
            if (string.IsNullOrEmpty(saveGroupId) || saveGroupId[0] != '{') return false;
            var json = saveGroupId;
            int cut = json.IndexOf('#');
            if (cut >= 0) json = json.Substring(0, cut);   // somebody else's decoration, see the header
            try { data = JsonSerializer.Deserialize<SaveIdData>(json, Options); }
            catch (JsonException) { data = null; }
            catch (NotSupportedException) { data = null; }
            return data != null;
        }

        /// <summary>The same id written again - what a row's id becomes once read and re-serialized.</summary>
        public static string? Canonical(string? saveGroupId)
            => TryRead(saveGroupId, out var d) ? Write(d!) : null;
    }
}
