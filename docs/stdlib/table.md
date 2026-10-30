# Tables

[Reference index](README.md) · [Official Lua 5.2 table library](https://www.lua.org/manual/5.2/manual.html#6.5) · [C Lua 5.2.4 source](https://www.lua.org/source/5.2/ltablib.c.html) · [Implementation](../../src/Lua/Standard/TableLibrary.cs)

Install with `state.OpenTableLibrary()`. These functions require a Lua table. They access elements raw, following Lua 5.2 rather than later-version proxy-table behavior. Operations that need a default length honor `__len`; that callback cannot Lua-yield across the library call, although C# code can await.

| API / official reference | Inputs and result | Lua-CSharp implementation notes and differences |
| --- | --- | --- |
| [`table.concat(t [, sep [, i [, j]]])`](https://www.lua.org/manual/5.2/manual.html#pdf-table.concat) | Joins string/number elements in the inclusive range; defaults are empty separator, `i=1`, and `j=#t`. | Numbers use the platform's `NumberFormat`. Managed string capacity bounds the output size. |
| [`table.insert(t, [pos,] value)`](https://www.lua.org/manual/5.2/manual.html#pdf-table.insert) | Inserts at `pos` and shifts following sequence elements; omitting `pos` appends. Returns no values. | Length and insertion positions are bounded by managed integer indices. A host-frozen table rejects mutation because its published contents are immutable. |
| [`table.pack(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-table.pack) | Returns an argument table with an `n` field holding the argument count, including nil arguments. | |
| [`table.remove(t [, pos])`](https://www.lua.org/manual/5.2/manual.html#pdf-table.remove) | Removes the selected sequence element, shifts later elements, and returns the removed value. Defaults to `#t`. | Host-frozen tables reject mutation. |
| [`table.sort(t [, comp])`](https://www.lua.org/manual/5.2/manual.html#pdf-table.sort) | Sorts the sequence in place using `<` or the supplied comparison function; returns no values. Sorting is not stable. | Comparator/ordering metamethod callbacks cannot Lua-yield; C# comparators can await. Host-frozen tables reject writes. These are managed storage and callback constraints. |
| [`table.unpack(t [, i [, j]])`](https://www.lua.org/manual/5.2/manual.html#pdf-table.unpack) | Returns elements from `i` to `j`, including nils. Defaults to `1` and `#t`; an empty range returns no values. | The result range must fit Lua-CSharp's managed stack capacity; oversized ranges raise `too many results to unpack`. |

As in Lua 5.2, use a sequence when relying on an implicit length. A table with holes does not have a uniquely useful sequence boundary, and a managed table need not choose the same boundary as a particular C Lua table layout.

The deprecated `table.maxn` helper and global `unpack` alias are not installed. `table.move` is a later-version addition. See [official library changes](https://www.lua.org/manual/5.2/manual.html#8.2).
