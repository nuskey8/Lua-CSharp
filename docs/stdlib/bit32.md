# Bitwise operations

[Reference index](README.md) · [Official Lua 5.2 bit32 library](https://www.lua.org/manual/5.2/manual.html#6.7) · [C Lua 5.2.4 source](https://www.lua.org/source/5.2/lbitlib.c.html) · [Implementation](../../src/Lua/Standard/BitwiseLibrary.cs)

Install with `state.OpenBitwiseLibrary()`. Results are doubles representing unsigned 32-bit words, not a separate integer type.

Word arguments are rounded with .NET `Math.Round` (nearest, ties to even) and reduced modulo 2^32. Lua 5.2 permits implementation-dependent conversion of fractional words; these are Lua-CSharp's rules, and a C build's conversion may differ. Shift amounts, field positions, and widths are converted separately by truncating to managed integers. Use integral arguments when matching results across implementations.

| API / official reference | Inputs and result | Lua-CSharp implementation notes and differences |
| --- | --- | --- |
| [`bit32.arshift(x, disp)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.arshift) | Arithmetic right shift with sign extension; negative displacement shifts left. | Uses signed 32-bit shifting internally and returns the unsigned word. |
| [`bit32.band(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.band) | Bitwise AND; no arguments returns all 32 bits set. | |
| [`bit32.bnot(x)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.bnot) | Inverts all 32 bits. | |
| [`bit32.bor(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.bor) | Bitwise OR; no arguments returns zero. | |
| [`bit32.btest(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.btest) | Reports whether the AND of its arguments is nonzero. | |
| [`bit32.bxor(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.bxor) | Bitwise XOR; no arguments returns zero. | |
| [`bit32.extract(n, field [, width])`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.extract) | Extracts a field at bit offset `field`, with width defaulting to 1. | Uses managed integer field/width conversion and rejects fields outside the 32-bit word. |
| [`bit32.lrotate(x, disp)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.lrotate) | Rotates left; negative displacement rotates right. | Rotation displacement is masked modulo 32. |
| [`bit32.lshift(x, disp)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.lshift) | Logical left shift; negative displacement shifts right. Magnitudes at least 32 produce zero. | |
| [`bit32.replace(n, v, field [, width])`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.replace) | Replaces the selected field using the low bits of `v`; default width is 1. | Uses managed integer field/width conversion and checks the 32-bit field boundary. |
| [`bit32.rrotate(x, disp)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.rrotate) | Rotates right; negative displacement rotates left. | Rotation displacement is masked modulo 32. |
| [`bit32.rshift(x, disp)`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32.rshift) | Logical right shift with zero fill; negative displacement shifts left. Magnitudes at least 32 produce zero. | |

These library calls provide Lua 5.2 bit operations. Lua 5.3 integer bitwise operators and 64-bit integer-subtype behavior are outside this API's contract.
