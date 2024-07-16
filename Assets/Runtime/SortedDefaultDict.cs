using System;
using System.Collections.Generic;

namespace NSM
{
    internal class SortedDefaultDict<TKey, TValue> : SortedDictionary<TKey, TValue>
    {
        private readonly Func<TValue> _defaultValueFactory;

        public SortedDefaultDict(Func<TValue> defaultValueFactory)
        {
            _defaultValueFactory = defaultValueFactory;
        }

        public new TValue this[TKey key]
        {
            get {
                if (!TryGetValue(key, out TValue value))
                {
                    value = _defaultValueFactory();
                    this[key] = value;
                }
                return value;
            }
            set => base[key] = value;
        }
    }
}
