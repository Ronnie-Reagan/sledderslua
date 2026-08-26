using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SleddersLuaRuntime.Api
{
    internal sealed class ObjectHandleRegistry
    {
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Dictionary<int, object> _byId = new Dictionary<int, object>();
        private readonly Dictionary<object, int> _byObject = new Dictionary<object, int>(ReferenceComparer.Instance);
        private int _next = 1;

        public int Add(object value)
        {
            if (_byObject.TryGetValue(value, out int existing))
                return existing;

            int id = _next++;
            _byId[id] = value;
            _byObject[value] = id;
            return id;
        }

        public object? Get(int id)
        {
            return _byId.TryGetValue(id, out object? value) ? value : null;
        }

        public void Clear()
        {
            _byId.Clear();
            _byObject.Clear();
            _next = 1;
        }
    }
}
