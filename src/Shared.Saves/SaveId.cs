// The SaveGroupId of every save row the pack writes: one JSON object, one serializer.
//
// WHY JSON. LaunchBox keeps a row's SaveGroupId for ever and compares it as a STRING (measured
// 07/10, docs\saves-lb14-routing.md §1): it is the only place a save can carry where it came from.
// A JSON object holds every field we need without inventing a grammar of ':' and '#'.
//
// WHY System.Text.Json AND NOTHING OF OURS (Mehdi, 08/10). Writing and reading JSON is the library's
// job. What is ours is only WHICH fields exist and IN WHAT ORDER - [JsonPropertyOrder] below - and
// the encoder's settings. Two identical states therefore always give the same string, which is what
// matters: LaunchBox and LiteBox compare the string, never the JSON. The probe's --saves-selftest
// pins exact strings, so a .NET update that changed one byte of the output would be seen there.
//
// THE ESCAPES. Every Unicode range is allowed through, so a name stays readable in the XML. '#' and
// '|' are forbidden, so the library writes them # and |: LiteBox cuts an id at '#' (its
// #cN / #web / #b:<name> decorations) and at '|'. Whatever the settings, .NET also escapes a few
// characters on its own (the HTML-sensitive ones, characters outside the BMP such as emoji): stable,
// only less readable, and pinned by the self-test like the rest.
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
    /// <summary>The fingerprint of the file LaunchBox launches, frozen when the row is created (see RomPrint).
    /// Up to 10 MB: <see cref="Crc"/> of the whole file, a proof. Above: <see cref="Head"/>, <see cref="Mid"/> and
    /// <see cref="Tail"/>, the CRC32 of its first, middle and last MB - a sample. CRCs are 8 upper-case hex digits.</summary>
    internal sealed class RomPrintData
    {
        [JsonPropertyOrder(0)] public long Size { get; init; }
        [JsonPropertyOrder(1)] public string? Crc { get; init; }
        [JsonPropertyOrder(2)] public string? Head { get; init; }
        [JsonPropertyOrder(3)] public string? Mid { get; init; }
        [JsonPropertyOrder(4)] public string? Tail { get; init; }

        public bool SameAs(RomPrintData? other)
            => other != null && Size == other.Size
               && string.Equals(Crc, other.Crc, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Head, other.Head, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Mid, other.Mid, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Tail, other.Tail, StringComparison.OrdinalIgnoreCase);
    }

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
        /// <summary>The LaunchBox game's GUID, frozen at creation.</summary>
        [JsonPropertyOrder(6)] public string? Game { get; init; }
        /// <summary>The additional version's GUID when the save belongs to one on ANOTHER file.</summary>
        [JsonPropertyOrder(7)] public string? Ver { get; init; }
        [JsonPropertyOrder(8)] public RomPrintData? Rom { get; init; }
        /// <summary>A LaunchBox integration's own id, kept exactly so the way back is exact.</summary>
        [JsonPropertyOrder(9)] public string? Native { get; init; }
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
