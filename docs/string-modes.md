# String modes

`LuaStringMode.Utf16` is the default. Lua strings contain .NET UTF-16 code units.
`LuaStringMode.Bytes` stores each byte in one C# `char` (0–255), including arbitrary binary data.

Choose the mode before running scripts:

```csharp
using Lua;
using Lua.Platforms;
using Lua.Standard;

using var textState = LuaState.Create(); // Utf16
using var byteState = LuaState.Create(LuaPlatform.DefaultByteStrings);
byteState.OpenStandardLibraries();
```

## Lua behavior

The examples below assume UTF-8 source text.

| Operation | `Utf16` | `Bytes` |
| --- | --- | --- |
| `#"ế"` | 1 | 3 |
| `#"😀"` | 2 | 4 |
| `string.len`, `string.sub`, `string.byte` | UTF-16 code units | Bytes |
| Case conversion and pattern classes | .NET character rules | ASCII C-locale rules |
| Built-in file and standard I/O | UTF-8 text decoded to/from UTF-16 | Raw bytes |

Neither mode counts user-perceived characters. In byte mode, `string.sub` can split a UTF-8 sequence and `string.char` accepts only 0–255.

## C# string values

UTF-16 values can use .NET strings directly. In byte mode, convert text and binary data explicitly:

```csharp
byteState.Environment["greeting"] = LuaByteString.FromString("こんにちは");
var values = await byteState.DoStringAsync("return greeting");
string greeting = LuaByteString.ToString(values[0].Read<string>());

byteState.Environment["packet"] = LuaByteString.FromBytes(new byte[] { 0xFF, 0, 0x80 });
var packets = await byteState.DoStringAsync("return packet");
byte[] packet = LuaByteString.ToBytes(packets[0].Read<string>());
```

`FromString` and `ToString` encode/decode UTF-8. Invalid sequences are replaced with U+FFFD when decoding. `FromBytes` and `ToBytes` preserve binary data; chars above U+00FF become `?` when converted to bytes.

Value conversion is also explicit for non-ASCII table keys, C# function arguments/results, and generated bindings.

## Source, paths and streams

- C# `DoStringAsync`, character-source `Load`, and text `LuaModule` sources accept normal .NET text. Byte mode encodes that source as UTF-8.
- In byte mode, byte-source `Load`, byte modules and source files preserve source bytes, except for an initial UTF-8 BOM. Lua's `load` consumes strings in the state's representation without encoding them again.
- C# chunk names and paths, including names passed to `ILuaFileSystem`, `ILuaFileLoader` and `ILuaModuleLoader`, are normal .NET strings in both modes.
- Byte-mode I/O preserves a BOM read as data. Line reads stop at LF and retain CR; `*L` also retains LF. Raw output replaces chars above U+00FF with `?`.

`LuaPlatform.DefaultByteStrings` configures matching byte-mode file and standard I/O. Changing `StringMode` alone does not reconfigure streams. Custom providers must use the same representation as the state; wrap raw streams with `ILuaStream.CreateFromStream(stream, openMode, LuaStringMode.Bytes)`.

To set a file root, keep the path as .NET text:

```csharp
var platform = LuaPlatform.DefaultByteStrings with
{
    FileSystem = new Lua.IO.FileSystem(scriptsDirectory, LuaStringMode.Bytes)
};
```
