# String modes

## Choose a mode

`LuaStringMode.Utf16` is the default and uses ordinary .NET UTF-16 strings. Use `LuaStringMode.Bytes` for Lua byte semantics or binary data; each C# `char` then holds one byte (0–255).

```csharp
using Lua;
using Lua.Platforms;
using Lua.Standard;

using var textState = LuaState.Create(); // Utf16
using var byteState = LuaState.Create(LuaPlatform.DefaultByteStrings);
byteState.OpenStandardLibraries();
```

Keep the mode fixed for a state's lifetime. Changing its platform does not convert existing strings, table keys or compiled chunks. Values shared between states with different modes need explicit conversion.

## Lua string operations

For UTF-8 source text:

| Operation | `Utf16` | `Bytes` |
| --- | --- | --- |
| `#"ế"` | 1 | 3 |
| `#"😀"` | 2 | 4 |
| `string.len`, `string.sub`, `string.byte` | UTF-16 code units | Bytes |
| Case conversion and pattern classes | .NET character rules | ASCII C-locale rules |

Lengths and slices do not count user-perceived characters. UTF-16 slicing can split a surrogate pair; byte slicing can split a UTF-8 sequence. Byte positions from Lua cannot be reused as indices into decoded .NET text. Use .NET text-element APIs on decoded text for user-perceived-character editing.

In byte mode, `string.char` accepts only 0–255. Non-ASCII letters remain unchanged by `string.upper`/`string.lower` and do not match `%a`. Decode text before applying Unicode case conversion or classification in C#.

Neither mode normalizes Unicode. For example, C# `"é"` and `"e\u0301"` remain different strings and table keys.

## Passing values between C# and Lua

UTF-16 values can use .NET strings directly. In byte mode, convert values according to their contents:

| Conversion | API |
| --- | --- |
| .NET text → Lua byte string | `LuaByteString.FromString(text)` |
| UTF-8 or binary bytes → Lua byte string | `LuaByteString.FromBytes(bytes)` |
| Lua byte string → .NET text | `LuaByteString.ToString(value)` |
| Lua byte string → raw bytes | `LuaByteString.ToBytes(value)` |

C# UTF-8 literals (`u8`, C# 11+) can supply bytes directly. `FromString("é")` and `FromBytes("é"u8)` produce the same bytes, `C3 A9`.

```csharp
byteState.Environment["greeting"] = LuaByteString.FromBytes("こんにちは"u8);
var values = await byteState.DoStringAsync("return greeting");
string greeting = LuaByteString.ToString(values[0].Read<string>());

byteState.Environment["packet"] = LuaByteString.FromBytes(new byte[] { 0xFF, 0, 0x80 });
var packets = await byteState.DoStringAsync("return packet");
byte[] packet = LuaByteString.ToBytes(packets[0].Read<string>());
```

### Convert text once

Both representations use C# `string`; the type does not identify the representation. A string read from a byte-mode state is already a byte string. Calling `FromString` on it again changes non-ASCII data.

Value conversion is explicit for table keys, C# function arguments/results and generated bindings too. ASCII-only testing can hide missing conversions: directly assigning .NET `"é"` in byte mode gives one byte, `E9`, rather than the UTF-8 bytes `C3 A9`.

### Preserve binary data

Use `FromBytes`/`ToBytes` for NUL bytes, invalid UTF-8 and other binary data. `FromString` replaces unpaired UTF-16 surrogates; `ToString` replaces invalid UTF-8 sequences with U+FFFD. These replacements lose the original data.

Valid byte strings contain only chars up to U+00FF. `ToBytes` and raw output replace higher chars with `?`.

## Loading source and naming files

Source-loading APIs handle conversion for you:

```csharp
// .NET text: encoded as UTF-8 in byte mode.
var textChunk = byteState.Load("return 'é'", "example");

// Already encoded UTF-8: pass bytes directly.
var utf8Chunk = byteState.Load("return 'é'"u8, "example");
```

- `DoStringAsync`, character-source `Load` and text `LuaModule` sources accept normal .NET text. Applying `FromString` first would encode the source twice in byte mode.
- In byte mode, byte-source `Load`, byte modules and source files preserve source bytes, except for an initial UTF-8 BOM. Lua's `load` uses strings in the state's representation without encoding them again.
- C# chunk names and paths are normal .NET strings in both modes, including names passed to `ILuaFileSystem`, `ILuaFileLoader` and `ILuaModuleLoader`.

## Configuring file and standard I/O

`LuaPlatform.DefaultByteStrings` configures matching byte-mode file and standard I/O. Changing `StringMode` alone does not reconfigure streams. Custom providers must use the same representation as the state; wrap raw streams with `ILuaStream.CreateFromStream(stream, openMode, LuaStringMode.Bytes)`.

A file root remains a normal .NET path:

```csharp
var platform = LuaPlatform.DefaultByteStrings with
{
    FileSystem = new Lua.IO.FileSystem("scripts", LuaStringMode.Bytes)
};
```

| I/O behavior | `Utf16` | `Bytes` |
| --- | --- | --- |
| File and standard stream contents | UTF-8 text decoded to/from UTF-16 | Raw bytes |
| `file:read(n)` | Up to `n` UTF-16 code units | Up to `n` bytes |
| `*l` reading `hello\r\n` | `hello` | `hello\r` |
| `*L` reading `hello\r\n` | `hello\r\n` | `hello\r\n` |
| Initial UTF-8 BOM read as data | Skipped | Preserved |

The `b` flag in `io.open(path, "rb")` does not select byte-string mode. Built-in streams use their configured `LuaStringMode` for both `"r"` and `"rb"`. Byte-mode line reads stop at LF; remove a trailing CR explicitly when the application expects a line without either newline character.
