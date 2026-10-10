using System.Collections;
using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua;

public sealed class LuaTable : IEnumerable<KeyValuePair<LuaValue, LuaValue>>
{
    public LuaTable()
        : this(8, 8) { }

    public LuaTable(int arrayCapacity, int dictionaryCapacity)
    {
        array = arrayCapacity > 1 ? new LuaValue[arrayCapacity] : [];
        dictionary = new(dictionaryCapacity);
    }

    LuaValue[] array;

    // A lower bound on the non-nil prefix, not a cached table length. Reusing it
    // avoids rescanning earlier elements on append; nil writes and moves may shorten it.
    int knownArrayPrefixLength;

    // Callers can retain a Span, Memory or ref and mutate the array without updating the prefix.
    // Once exposed, length queries use a linear scan until the backing array is replaced.
    bool hasWritableArrayReferences;
    readonly LuaValueDictionary dictionary;
    LuaTable? metatable;

    internal LuaValueDictionary Dictionary => dictionary;

    const int MaxArraySize = 1 << 24;
    const int MaxDistance = 1 << 12;

    public LuaValue this[LuaValue key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (key.Type is LuaValueType.Nil)
            {
                ThrowIndexIsNil();
            }

            if (TryGetInteger(key, out var index))
            {
                if (index > 0 && index <= array.Length)
                {
                    // Arrays in Lua are 1-origin...
                    return array[index - 1];
                }
            }

            if (dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            return LuaValue.Nil;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (key.TryReadNumber(out var d))
            {
                if (double.IsNaN(d))
                {
                    ThrowIndexIsNaN();
                }

                if (MathEx.IsInteger(d))
                {
                    var index = (int)d;

                    var distance = index - array.Length;
                    if (distance > MaxDistance)
                    {
                        dictionary[key] = value;
                        return;
                    }

                    if (0 < index && index < MaxArraySize && index <= Math.Max(array.Length * 2, 8))
                    {
                        // Include the last slot: consecutive keys beyond the array may already
                        // be in the hash part and must be brought into the array. Interior writes
                        // do not need this hash lookup.
                        if (index >= array.Length)
                        {
                            EnsureArrayCapacity(index);
                        }
                        array[index - 1] = value;
                        if (value.Type is LuaValueType.Nil && index <= knownArrayPrefixLength)
                        {
                            knownArrayPrefixLength = index - 1;
                        }
                        return;
                    }
                }
            }

            dictionary[key] = value;
        }
    }

    public int HashMapCount => dictionary.Count - dictionary.NilCount;

    public int ArrayLength
    {
        get
        {
            // Lua 5.2 leaves the length of tables with holes undefined. Finding the first nil
            // is this implementation's choice, not an additional Lua-facing guarantee.
            var start = hasWritableArrayReferences ? 0 : knownArrayPrefixLength;
            for (var i = start; i < array.Length; i++)
            {
                if (array[i].Type is LuaValueType.Nil)
                {
                    if (!hasWritableArrayReferences)
                    {
                        knownArrayPrefixLength = i;
                    }
                    return i;
                }
            }

            if (!hasWritableArrayReferences)
            {
                knownArrayPrefixLength = array.Length;
            }
            return array.Length;
        }
    }

    public LuaTable? Metatable
    {
        get => metatable;
        set => metatable = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(LuaValue key, out LuaValue value)
    {
        if (key.Type is LuaValueType.Nil)
        {
            value = default;
            return false;
        }

        if (TryGetInteger(key, out var index))
        {
            if (index > 0 && index <= array.Length)
            {
                value = array[index - 1];
                return value.Type is not LuaValueType.Nil;
            }
        }

        return dictionary.TryGetValue(key, out value) && value.Type is not LuaValueType.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref LuaValue FindValue(LuaValue key)
    {
        if (key.Type is LuaValueType.Nil)
        {
            ThrowIndexIsNil();
        }

        if (TryGetInteger(key, out var index))
        {
            if (index > 0 && index <= array.Length)
            {
                hasWritableArrayReferences = true;
                return ref array[index - 1];
            }
        }

        return ref dictionary.FindValue(key, out _);
    }

    public bool ContainsKey(LuaValue key)
    {
        if (key.Type is LuaValueType.Nil)
        {
            return false;
        }

        if (TryGetInteger(key, out var index) && index > 0 && index <= array.Length)
        {
            return array[index - 1].Type != LuaValueType.Nil;
        }

        return dictionary.TryGetValue(key, out var value) && value.Type is not LuaValueType.Nil;
    }

    public LuaValue RemoveAt(int index)
    {
        var arrayIndex = index - 1;
        var value = array[arrayIndex];

        if (arrayIndex < array.Length - 1)
        {
            array.AsSpan(arrayIndex + 1).CopyTo(array.AsSpan(arrayIndex));
        }

        array[^1] = default;
        knownArrayPrefixLength = Math.Min(knownArrayPrefixLength, arrayIndex);

        return value;
    }

    public void Insert(int index, LuaValue value)
    {
        var luaArrayLength = ArrayLength;
        if (index <= 0 || index > luaArrayLength + 1)
        {
            throw new IndexOutOfRangeException();
        }

        if (
            index >= array.Length
            || array.Length < 2
            || array[^1].Type != LuaValueType.Nil
            || array[^2].Type != LuaValueType.Nil
        )
        {
            EnsureArrayCapacity(array.Length + 1);
        }

        var arrayIndex = index - 1;
        if (arrayIndex < luaArrayLength)
        {
            array
                .AsSpan(arrayIndex, luaArrayLength - arrayIndex)
                .CopyTo(array.AsSpan(arrayIndex + 1));
        }

        array[arrayIndex] = value;
        if (!hasWritableArrayReferences)
        {
            knownArrayPrefixLength =
                value.Type is LuaValueType.Nil ? arrayIndex : luaArrayLength + 1;
        }
    }

    public bool TryGetNext(LuaValue key, out KeyValuePair<LuaValue, LuaValue> pair)
    {
        var index = -1;
        if (key.Type is LuaValueType.Nil)
        {
            index = 0;
        }
        else if (TryGetInteger(key, out var integer) && integer > 0 && integer <= array.Length)
        {
            index = integer;
        }

        if (index != -1)
        {
            var span = array.AsSpan(index);
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i].Type is not LuaValueType.Nil)
                {
                    pair = new(index + i + 1, span[i]);
                    return true;
                }
            }

            foreach (var kv in dictionary)
            {
                if (kv.Value.Type is not LuaValueType.Nil)
                {
                    pair = kv;
                    return true;
                }
            }
        }
        else
        {
            if (dictionary.TryGetNext(key, out pair))
            {
                return true;
            }
        }

        pair = default;
        return false;
    }

    public void Clear()
    {
        array.AsSpan().Clear();
        dictionary.Clear();
        knownArrayPrefixLength = 0;
        // Keep hasWritableArrayReferences: Clear does not detach retained writable views.
    }

    public Memory<LuaValue> GetArrayMemory()
    {
        hasWritableArrayReferences = true;
        return array.AsMemory();
    }

    public Span<LuaValue> GetArraySpan()
    {
        hasWritableArrayReferences = true;
        return array.AsSpan();
    }

    internal void EnsureArrayCapacity(int newCapacity)
    {
        newCapacity = Math.Min(Math.Max(array.Length, newCapacity), MaxArraySize);
        while (
            newCapacity < MaxArraySize
            && dictionary.TryGetValue(newCapacity + 1, out var dictionaryValue)
            && dictionaryValue.Type is not LuaValueType.Nil
        )
        {
            newCapacity++;
        }
        if (array.Length >= newCapacity)
        {
            return;
        }

        var prevLength = array.Length;
        var newLength = newCapacity <= 8 ? 8 : MathEx.NextPowerOfTwo(newCapacity);

        Array.Resize(ref array, newLength);
        if (hasWritableArrayReferences)
        {
            // Retained views now refer only to the old array. Recompute the prefix because
            // values copied from that array may have been changed through those views.
            knownArrayPrefixLength = 0;
            hasWritableArrayReferences = false;
        }

        using PooledList<(int, LuaValue)> indexList = new(dictionary.Count);

        // Move some of the elements of the hash part to a newly allocated array
        foreach (var kv in dictionary)
        {
            if (TryGetInteger(kv.Key, out var index))
            {
                if (index > prevLength && index <= newLength)
                {
                    indexList.Add((index, kv.Value));
                }
            }
        }

        foreach (var (index, value) in indexList.AsSpan())
        {
            dictionary.Remove(index);
            array[index - 1] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryGetInteger(LuaValue value, out int integer)
    {
        if (value.TryReadNumber(out var num) && MathEx.IsInteger(num))
        {
            integer = (int)num;
            return true;
        }

        integer = default;
        return false;
    }

    static void ThrowIndexIsNil()
    {
        throw new ArgumentException("the table index is nil");
    }

    static void ThrowIndexIsNaN()
    {
        throw new ArgumentException("the table index is NaN");
    }

    public LuaTableEnumerator GetEnumerator()
    {
        return new(this);
    }

    IEnumerator<KeyValuePair<LuaValue, LuaValue>> IEnumerable<
        KeyValuePair<LuaValue, LuaValue>
    >.GetEnumerator()
    {
        return new LuaTableEnumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new LuaTableEnumerator(this);
    }

    public struct LuaTableEnumerator(LuaTable table) : IEnumerator<KeyValuePair<LuaValue, LuaValue>>
    {
        public KeyValuePair<LuaValue, LuaValue> Current => current;

        int index = -1;
        readonly int version = table.dictionary.Version;
        KeyValuePair<LuaValue, LuaValue> current = default;

        public bool MoveNext()
        {
            if (index < 0)
            {
                var arrayIndex = -index - 1;
                var span = table.array.AsSpan(arrayIndex);
                for (var i = 0; i < span.Length; i++)
                {
                    if (span[i].Type is not LuaValueType.Nil)
                    {
                        current = new(arrayIndex + i + 1, span[i]);
                        index = -arrayIndex - i - 2;
                        return true;
                    }
                }

                index = 0;
            }

            while (
                LuaValueDictionary.MoveNext(table.Dictionary, version, ref index, out current)
                && current.Value.Type is LuaValueType.Nil
            ) { }

            return current.Value.Type is not LuaValueType.Nil;
        }

        public void Reset() { }

        object IEnumerator.Current => Current;

        public void Dispose() { }
    }
}
