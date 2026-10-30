# Strings

[Reference index](README.md) · [Official Lua 5.2 string library](https://www.lua.org/manual/5.2/manual.html#6.4) · [Implementation](../../src/Lua/Standard/StringLibrary.cs)

Install with `state.OpenStringLibrary()`. String method calls such as `s:sub(2)` use this library through the string metatable.

The central difference is representation: operations use UTF-16 code units by default and bytes in `LuaStringMode.Bytes`. This makes default-mode operations convenient for .NET strings; byte mode preserves Lua's binary-string model. See [String modes](../string-modes.md). Indices are 1-based; negative substring indices count from the end.

| API / official reference | Inputs and result | Lua-CSharp differences and reasons |
| --- | --- | --- |
| [`string.byte(s [, i [, j]])`](https://www.lua.org/manual/5.2/manual.html#pdf-string.byte) | Returns the numeric units in the inclusive range, defaulting to the first unit. | Default mode returns UTF-16 code-unit values, including surrogate values. Byte mode returns 0–255. The managed string representation determines the units. |
| [`string.char(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.char) | Constructs a string from numeric character values. | Byte mode requires 0–255, as Lua byte strings do. Default mode casts the integer values to UTF-16 `char`, rather than enforcing a byte range; use ordinary text or byte mode according to the intended data. |
| [`string.dump(f)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.dump) | Serializes a Lua function's prototype as a raw binary string. Captured values are not serialized. C# functions cannot be dumped. | Available in both string modes; the result always holds raw byte values. See [dumping and loading](#dumping-and-loading). |
| [`string.find(s, pattern [, init [, plain]])`](https://www.lua.org/manual/5.2/manual.html#pdf-string.find) | Returns match start/end and captures, or nil. `plain` selects literal substring search. | Positions use the state's string units. Pattern character classes use the rules below; plain search uses ordinal string matching. |
| [`string.format(format, ...)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.format) | Formats values using Lua's printf-style conversions, including `%s`, `%q`, integer, floating-point, and hexadecimal floating-point conversions. | Width/precision count managed string units. `%c` constructs a UTF-16 unit in default mode and masks to one byte in byte mode. Numeric text and special values follow `NumberFormat` where applicable. Integer formatting uses 64-bit signed/unsigned ranges; these can differ from a C Lua build's configured integer formatting width. |
| [`string.gmatch(s, pattern)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.gmatch) | Returns an iterator over captures, or whole matches when there are no captures. | Positions and classes follow the selected string mode. |
| [`string.gsub(s, pattern, replacement [, n])`](https://www.lua.org/manual/5.2/manual.html#pdf-string.gsub) | Returns the replaced string and match count. Replacement may be text, a number, a table, or a function. False/nil replacement results retain the matched text. | Numeric replacements use `NumberFormat`. Table `__index` and replacement callbacks can await C# work but cannot Lua-yield through this library call. |
| [`string.len(s)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.len) | Returns the string length, including embedded zero units. | Counts UTF-16 units by default and bytes in byte mode; neither counts Unicode grapheme clusters. |
| [`string.lower(s)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.lower) | Converts uppercase units to lowercase. | Default mode uses .NET current-culture Unicode casing through `String.ToLower`; byte mode changes ASCII A–Z only. Native Lua uses its C locale. `os.setlocale` does not set .NET's current culture. |
| [`string.match(s, pattern [, init])`](https://www.lua.org/manual/5.2/manual.html#pdf-string.match) | Returns captures or the whole match, or nil. | Positions and classes follow the selected string mode. |
| [`string.rep(s, n [, sep])`](https://www.lua.org/manual/5.2/manual.html#pdf-string.rep) | Repeats `s`, inserting `sep` between copies. Non-positive counts produce an empty string. | Counts are truncated as in Lua 5.2; resulting lengths are bounded by .NET string/int capacity. |
| [`string.reverse(s)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.reverse) | Reverses the sequence of string units. | Default mode can split/reverse surrogate pairs; byte mode reverses bytes and can split UTF-8 sequences. This operation is not Unicode text-element reversal. |
| [`string.sub(s, i [, j])`](https://www.lua.org/manual/5.2/manual.html#pdf-string.sub) | Returns the inclusive slice, with `j` defaulting to the last unit. | Slices UTF-16 units or bytes according to the mode, including partial surrogate pairs/UTF-8 sequences. |
| [`string.upper(s)`](https://www.lua.org/manual/5.2/manual.html#pdf-string.upper) | Converts lowercase units to uppercase. | Default mode uses .NET current-culture Unicode casing through `String.ToUpper`; byte mode changes ASCII a–z only. The host's .NET culture, rather than the process C locale, controls default-mode casing. |

## Patterns

[Official pattern syntax](https://www.lua.org/manual/5.2/manual.html#6.4.1) applies: Lua patterns are not .NET regular expressions. Captures, position captures `()`, balanced matches `%b`, frontier patterns `%f`, anchors, and Lua repetition operators are handled by the Lua pattern matcher.

Default-mode character classes use .NET character classification. In byte mode, classes and case conversion use ASCII C-locale rules. Thus non-ASCII letters may match `%a` in default mode but not in byte mode. `os.setlocale` does not change these rules. Pattern recursion and capture counts are bounded by the managed matcher.

## Dumping and loading

```lua
local bytes = string.dump(function() return 42 end)
local f = assert(load(bytes, 'saved', 'b'))
assert(f() == 42)
```

Use byte-mode streams when saving `bytes` from Lua. In C#, use `LuaByteString.ToBytes` to extract its raw bytes. A UTF-16 stream encodes characters as UTF-8 and can corrupt arbitrary binary strings even if opened with `wb`.

Chunks use the C Lua 5.2 layout, with the architecture/number sizes recorded in their header. Do not assume portability across different Lua versions or incompatible binary layouts. Older Lua-CSharp chunks generated with the previous incorrect header/tag layout must be regenerated from source. `Prototype.StringMode` preserves the representation of constants during dumping.

Lua 5.3/5.4 additions such as `string.pack`, `string.unpack`, and `string.packsize` are not part of this Lua 5.2 library.
