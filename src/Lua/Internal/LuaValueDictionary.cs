// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Lua.Internal;

/// <summary>
/// A minimal dictionary that uses LuaValue as key and value.
/// Nil value counting is included.
/// </summary>
sealed class LuaValueDictionary
{
    int[]? _buckets;
    Entry[]? _entries;
    int _count;
    int _freeList;
    int _freeCount;
    int _version;
    const int StartOfFreeList = -3;

    int _nilCount;

    public LuaValueDictionary(int capacity)
    {
        if (capacity < 0)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(capacity));
        }

        if (capacity > 0)
        {
            Initialize(capacity);
        }
    }

    public int Count => _count - _freeCount;

    public int NilCount => _nilCount;


    public LuaValue this[LuaValue key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ref var value = ref FindValue(key, out _);
            if (!Unsafe.IsNullRef(ref value))
            {
                return value;
            }

            return default;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Insert(key, value);
    }


    public void Clear()
    {
        var count = _count;
        if (count > 0)
        {
            Debug.Assert(_buckets != null, "_buckets should be non-null");
            Debug.Assert(_entries != null, "_entries should be non-null");

            Array.Clear(_buckets, 0, _buckets.Length);

            _count = 0;
            _freeList = -1;
            _freeCount = 0;
            Array.Clear(_entries, 0, count);
            _nilCount = 0;
        }
    }

    public bool ContainsKey(LuaValue key)
    {
        return !Unsafe.IsNullRef(ref FindValue(key, out _));
    }

    public bool ContainsValue(LuaValue value)
    {
        var entries = _entries;

        for (var i = 0; i < _count; i++)
        {
            if (entries![i].next >= -1 && entries[i].value.Equals(value))
            {
                return true;
            }
        }

        return false;
    }


    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal ref LuaValue FindValue(LuaValue key, out int index)
    {
        index = -1;
        ref var entry = ref Unsafe.NullRef<Entry>();
        if (_buckets != null)
        {
            Debug.Assert(_entries != null, "expected entries to be != null");
            {
                var hashCode = (uint)key.GetHashCode();
                var i = GetBucket(hashCode);
                var entriesLength = _entries.Length;
                ref var entriesRef = ref _entries[0];
                uint collisionCount = 0;

                // ValueType: Devirtualize with EqualityComparer<LuaValue>.Default intrinsic
                i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                do
                {
                    // Should be a while loop https://github.com/dotnet/runtime/issues/9422
                    // Test in if to drop range check for following array access
                    if ((uint)i >= (uint)entriesLength)
                    {
                        goto ReturnNotFound;
                    }

                    entry = ref Unsafe.Add(ref entriesRef, i);
                    if (entry.hashCode == hashCode && entry.keyType == key.Type &&
                        key.Type switch
                        {
                            LuaValueType.String => Unsafe.As<string>(entry.keyObject) == key.UnsafeReadString(),
                            LuaValueType.Number or LuaValueType.Boolean => entry.keyNumber == key.UnsafeReadDouble(),
                            _ => entry.keyObject == key.UnsafeReadObject()
                        })
                    {
                        index = i;
                        goto ReturnFound;
                    }

                    i = entry.next;

                    collisionCount++;
                } while (collisionCount <= (uint)entriesLength);

                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                goto ConcurrentOperation;
            }
        }

        goto ReturnNotFound;

    ConcurrentOperation:
        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
    ReturnFound:
        ref var value = ref entry.value;
    Return:
        return ref value;
    ReturnNotFound:
        value = ref Unsafe.NullRef<LuaValue>();
        goto Return;
    }

    void Initialize(int capacity)
    {
        var newSize = 8;
        while (newSize < capacity)
        {
            newSize *= 2;
        }

        var size = newSize;
        var buckets = new int[size];
        var entries = new Entry[size];

        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
        _freeList = -1;
        _buckets = buckets;
        _entries = entries;
    }

    void Insert(LuaValue key, LuaValue value)
    {
        if (value.Type is LuaValueType.Nil)
        {
            _nilCount++;
        }

        if (_buckets == null)
        {
            Initialize(0);
        }

        Debug.Assert(_buckets != null);

        var entries = _entries;
        Debug.Assert(entries != null, "expected entries to be non-null");


        var hashCode = (uint)key.GetHashCode();

        uint collisionCount = 0;
        ref var bucket = ref GetBucket(hashCode);
        var i = bucket - 1; // Value in _buckets is 1-based

        {
            ref var entry = ref Unsafe.NullRef<Entry>();
            while ((uint)i < (uint)entries.Length)
            {
                entry = ref entries[i];
                if (entry.hashCode == hashCode && entry.keyType == key.Type &&
                    key.Type switch
                    {
                        LuaValueType.Number or LuaValueType.Boolean => entry.keyNumber == key.UnsafeReadDouble(),
                        LuaValueType.String => Unsafe.As<string>(entry.keyObject) == key.UnsafeReadString(),
                        _ => entry.keyObject == key.UnsafeReadObject()
                    })
                {
                    if (entry.value.Type is LuaValueType.Nil)
                    {
                        _nilCount--;
                    }

                    entry.value = value;
                    return;
                }

                i = entry.next;

                collisionCount++;
                if (collisionCount > (uint)entries.Length)
                {
                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                }
            }
        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            Debug.Assert(StartOfFreeList - entries[_freeList].next >= -1, "shouldn't overflow because `next` cannot underflow");
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            var count = _count;
            if (count == entries.Length)
            {
                Resize();
                bucket = ref GetBucket(hashCode);
            }

            index = count;
            _count = count + 1;
            entries = _entries;
        }

        {
            ref var entry = ref entries![index];
            entry.hashCode = hashCode;
            entry.next = bucket - 1; // Value in _buckets is 1-based
            entry.keyType = key.Type;
            entry.keyObject = key.UnsafeReadObject();
            entry.keyNumber = key.UnsafeReadDouble();
            entry.value = value;
            bucket = index + 1; // Value in _buckets is 1-based
            _version++;
        }
    }


    void Resize()
    {
        Resize(_entries!.Length * 2);
    }

    void Resize(int newSize)
    {
        // Value types never rehash
        Debug.Assert(_entries != null, "_entries should be non-null");
        Debug.Assert(newSize >= _entries.Length);

        var entries = new Entry[newSize];

        var count = _count;
        Array.Copy(_entries, entries, count);


        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
        _buckets = new int[newSize];

        for (var i = 0; i < count; i++)
        {
            if (entries[i].next >= -1)
            {
                ref var bucket = ref GetBucket(entries[i].hashCode);
                entries[i].next = bucket - 1; // Value in _buckets is 1-based
                bucket = i + 1;
            }
        }

        _entries = entries;
    }

    public bool Remove(LuaValue key)
    {
        // The overload Remove(LuaValue key, out LuaValue value) is a copy of this method with one additional
        // statement to copy the value for entry being removed into the output parameter.
        // Code has been intentionally duplicated for performance reasons.

        if (_buckets != null)
        {
            Debug.Assert(_entries != null, "entries should be non-null");
            uint collisionCount = 0;


            var hashCode = (uint)key.GetHashCode();

            ref var bucket = ref GetBucket(hashCode);
            var entries = _entries;
            var last = -1;
            var i = bucket - 1; // Value in buckets is 1-based
            while (i >= 0)
            {
                ref var entry = ref entries[i];

                if (entry.hashCode == hashCode && entry.keyType == key.Type &&
                    key.Type switch
                    {
                        LuaValueType.Number or LuaValueType.Boolean => entry.keyNumber == key.UnsafeReadDouble(),
                        LuaValueType.String => Unsafe.As<string>(entry.keyObject) == key.UnsafeReadString(),
                        _ => entry.keyObject == key.UnsafeReadObject()
                    })
                {
                    if (last < 0)
                    {
                        bucket = entry.next + 1; // Value in buckets is 1-based
                    }
                    else
                    {
                        entries[last].next = entry.next;
                    }

                    Debug.Assert(StartOfFreeList - _freeList < 0, "shouldn't underflow because max hashtable length is MaxPrimeArrayLength = 0x7FEFFFFD(2146435069) _freelist underflow threshold 2147483646");
                    entry.next = StartOfFreeList - _freeList;

                    if (entry.value.Type is LuaValueType.Nil)
                    {
                        _nilCount--;
                    }

                    entry.keyType = default;
                    entry.keyObject = null!;
                    entry.value = default;

                    _freeList = i;
                    _freeCount++;
                    return true;
                }

                last = i;
                i = entry.next;

                collisionCount++;
                if (collisionCount > (uint)entries.Length)
                {
                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                }
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(LuaValue key, out LuaValue value)
    {
        ref var valRef = ref FindValue(key, out _);
        if (!Unsafe.IsNullRef(ref valRef))
        {
            value = valRef;
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNext(LuaValue key, out KeyValuePair<LuaValue, LuaValue> pair)
    {
        ref var valRef = ref FindValue(key, out var index);
        if (!Unsafe.IsNullRef(ref valRef))
        {
            var entries = _entries!;
            while (++index < _count)
            {
                ref var entry = ref entries[index];
                if (entry is { next: >= -1, value.Type: not LuaValueType.Nil })
                {
                    pair = new(new(entry.keyType, entry.keyNumber, entry.keyObject), entry.value);
                    return true;
                }
            }
        }

        pair = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref int GetBucket(uint hashCode)
    {
        var buckets = _buckets!;
        return ref buckets[hashCode & (buckets.Length - 1)];
    }

    struct Entry
    {
        public uint hashCode;
        public LuaValueType keyType;
        public object? keyObject;
        public double keyNumber;

        /// <summary>
        /// 0-based index of next entry in chain: -1 means end of chain
        /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
        /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
        /// </summary>
        public int next;

        public LuaValue value; // Value of entry
    }

    internal int Version => _version;

    internal static bool MoveNext(LuaValueDictionary dictionary, int version, ref int index, out KeyValuePair<LuaValue, LuaValue> current)
    {
        if (version != dictionary._version)
        {
            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
        }

    SearchNext:
        // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
        // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
        while ((uint)index < (uint)dictionary._count)
        {
            ref var entry = ref dictionary._entries![index++];

            if (entry.next >= -1)
            {
                if (entry.value.Type is LuaValueType.Nil)
                {
                    goto SearchNext;
                }

                current = new(new(entry.keyType, entry.keyNumber, entry.keyObject), entry.value);
                return true;
            }
        }

        index = dictionary._count + 1;
        current = default;
        return false;
    }

    public struct Enumerator
    {
        readonly LuaValueDictionary dictionary;
        readonly int _version;
        int _index;
        KeyValuePair<LuaValue, LuaValue> _current;

        internal Enumerator(LuaValueDictionary dictionary)
        {
            this.dictionary = dictionary;
            _version = dictionary._version;
            _index = 0;
            _current = default;
        }

        public bool MoveNext()
        {
            return LuaValueDictionary.MoveNext(dictionary, _version, ref _index, out _current);
        }

        public KeyValuePair<LuaValue, LuaValue> Current => _current;
    }

    static class ThrowHelper
    {
        public static void ThrowInvalidOperationException_ConcurrentOperationsNotSupported()
        {
            throw new InvalidOperationException("Concurrent operations are not supported");
        }

        public static void ThrowArgumentOutOfRangeException(string paramName)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }

        public static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion()
        {
            throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
        }
    }
}