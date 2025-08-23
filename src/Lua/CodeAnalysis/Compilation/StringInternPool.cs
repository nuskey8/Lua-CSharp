// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Lua.CodeAnalysis.Compilation;

[SuppressMessage("ReSharper", "InconsistentNaming")]
class StringInternPool : IDisposable
{
    int[] _buckets;
    Entry[] _entries;
    int _count;

    public StringInternPool(int capacity = 16)
    {
        var size = Math.Max(16, capacity);
        var buckets = ArrayPool<int>.Shared.Rent(size);
        buckets.AsSpan().Clear();
        var entries = ArrayPool<Entry>.Shared.Rent(size);

        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails

        _buckets = buckets;
        _entries = entries;
    }

    public int Count => _count;


    public void Clear()
    {
        var count = _count;
        if (count > 0)
        {
            Debug.Assert(_buckets != null, "_buckets should be non-null");
            Debug.Assert(_entries != null, "_entries should be non-null");
            _buckets.AsSpan().Clear();

            _count = 0;
            _entries.AsSpan(0, count).Clear();
        }
    }

    static int GetHashCode(ReadOnlySpan<char> span)
    {
        unchecked
        {
            int hash = 5381;
            foreach (var t in span)
            {
                hash = ((hash << 5) + hash) ^ t;
            }

            return hash & 0x7FFFFFFF;
        }
    }

    public string Intern(ReadOnlySpan<char> value)
    {
        Debug.Assert(_buckets != null);

        Entry[] entries = _entries;

        int hashCode;

        ref int bucket = ref Unsafe.NullRef<int>();
        {
            hashCode = GetHashCode(value);
            bucket = ref GetBucketRef(hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based
            while (i >= 0)
            {
                ref Entry entry = ref entries[i];
                if (entry.HashCode == hashCode && (value.SequenceEqual(entry.Value)))
                {
                    return entry.Value;
                }

                i = entry.Next;
            }
        }

        int index;


        int count = _count;
        if (count == entries.Length)
        {
            Resize();
            bucket = ref GetBucketRef(hashCode);
        }

        index = count;
        _count = count + 1;
        entries = _entries;


        var stringValue = value.ToString();
        stringValue = string.IsInterned(stringValue) ?? stringValue;
        {
            ref Entry entry = ref entries![index];
            entry.HashCode = hashCode;
            entry.Next = bucket - 1; // Value in _buckets is 1-based
            entry.Value = stringValue;
            bucket = index + 1;
        }

        return stringValue;
    }


    void Resize()
    {
        Resize(_entries!.Length * 2);
    }

    void Resize(int newSize)
    {
        // Value types never rehash
        Debug.Assert(newSize >= _entries.Length);

        var entries = ArrayPool<Entry>.Shared.Rent(newSize);

        var count = _count;
        Array.Copy(_entries, entries, count);

        ArrayPool<Entry>.Shared.Return(_entries, true);
        ArrayPool<int>.Shared.Return(_buckets);

        // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
        _buckets = ArrayPool<int>.Shared.Rent(newSize);
        _buckets.AsSpan().Clear();
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Next >= -1)
            {
                ref var bucket = ref GetBucketRef(entries[i].HashCode);
                entries[i].Next = bucket - 1; // Value in _buckets is 1-based
                bucket = i + 1;
            }
        }

        _entries = entries;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref int GetBucketRef(int hashCode)
    {
        var buckets = _buckets;
        return ref buckets[(uint)hashCode & (uint)(buckets.Length - 1)];
    }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_buckets);
        _buckets = null!;


        ArrayPool<Entry>.Shared.Return(_entries, true);
        _entries = null!;
    }

    struct Entry
    {
        public int HashCode;

        /// <summary>
        /// 0-based index of next entry in chain: -1 means end of chain
        /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
        /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
        /// </summary>
        public int Next;

        public string Value;
    }
}