using System;
using System.Collections.Generic;
using System.Linq;

namespace SleddersLuaRuntime.Api
{
    internal static class RuntimeResourceRegistry
    {
        private sealed class Entry
        {
            public string Owner { get; set; } = string.Empty;
            public Action Release { get; set; } = null!;
        }

        private static readonly object Gate = new object();
        private static readonly List<Entry> Entries = new List<Entry>();

        public static void Register(string owner, Action release)
        {
            lock (Gate) Entries.Add(new Entry { Owner = owner, Release = release });
        }

        public static void ReleaseOwner(string owner)
        {
            Entry[] matches;
            lock (Gate)
            {
                matches = Entries.FindAll(x => string.Equals(x.Owner, owner, StringComparison.Ordinal)).ToArray();
                Entries.RemoveAll(x => string.Equals(x.Owner, owner, StringComparison.Ordinal));
            }
            foreach (Entry entry in matches)
            {
                try { entry.Release(); } catch { }
            }
        }
    }
}
