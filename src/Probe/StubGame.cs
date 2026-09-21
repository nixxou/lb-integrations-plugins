// An IGame with four answers and nothing else.
//
// IGame is a very wide interface and the plugin reads four members of it. Writing the rest by hand
// would be a hundred lines of noise that says nothing, and every SDK version that adds a member
// would break the harness rather than the thing under test. DispatchProxy builds the implementation
// at run time instead: named values come back, everything else answers a type default.
//
// Deliberately NOT a stand-in for the real thing: it is here so GetSaves can be called with a game
// that resolves to a real ROM on disk, which is what makes the listing assertion meaningful.

using System;
using System.Collections.Generic;
using System.Reflection;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    public class StubGame : DispatchProxy
    {
        private Dictionary<string, object> _values;

        public static IGame Create(string id, string title, string applicationPath, string emulatorId = null)
        {
            var game = DispatchProxy.Create<IGame, StubGame>();
            ((StubGame)(object)game)._values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Id"] = id,
                ["Title"] = title,
                ["ApplicationPath"] = applicationPath,
                ["EmulatorId"] = emulatorId,
            };
            return game;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod == null) return null;

            if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                var name = targetMethod.Name.Substring(4);
                if (_values != null && _values.TryGetValue(name, out var value)) return value;
            }
            return DefaultOf(targetMethod.ReturnType);
        }

        /// <summary>An IPlatform that knows its name, built the same way and for the same reason.
        /// The plugin only ever asks a platform what it is called.</summary>
        public static IPlatform Platform(string name)
        {
            var platform = DispatchProxy.Create<IPlatform, StubGame>();
            ((StubGame)(object)platform)._values =
                new Dictionary<string, object>(StringComparer.Ordinal) { ["Name"] = name };
            return platform;
        }

        private static object DefaultOf(Type t)
        {
            if (t == null || t == typeof(void)) return null;
            if (t.IsArray) return Array.CreateInstance(t.GetElementType(), 0);
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }
}
