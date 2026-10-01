// --lb-types <assembly> <type name filter>: the properties and fields of the types of an assembly whose name holds the
// filter - read from its metadata only, nothing loaded or run. For LaunchBox's obfuscated core, whose method bodies are
// decoys but whose view models keep readable property names.

using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace LbIntegrations.Probe
{
    internal static class LbTypesCheck
    {
        public static bool Run(string path, string filter)
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var md = pe.GetMetadataReader();
            foreach (var th in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(th);
                var name = md.GetString(t.Name);
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine("== " + md.GetString(t.Namespace) + "." + name);
                foreach (var ph in t.GetProperties()) Console.WriteLine("   P " + md.GetString(md.GetPropertyDefinition(ph).Name));
                foreach (var fh in t.GetFields()) Console.WriteLine("   F " + md.GetString(md.GetFieldDefinition(fh).Name));
            }
            return true;
        }
    }
}
