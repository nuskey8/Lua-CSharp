# Lua standard library reference

This reference describes the Lua-facing APIs installed by Lua-CSharp. Each API links to its entry in the [official Lua 5.2 manual](https://www.lua.org/manual/5.2/manual.html#6). The manual defines the Lua contract; these pages explain the managed implementation, available configuration, and known limitations. Comparisons with C Lua use the [official Lua 5.2.4 source](https://www.lua.org/source/5.2/), with platform/build differences called out where relevant.

## Open libraries

```csharp
using Lua;
using Lua.Platforms;
using Lua.Standard;

using var state = LuaState.Create();
state.OpenStandardLibraries();

// For Lua byte strings and raw stream contents:
using var bytes = LuaState.Create(LuaPlatform.DefaultByteStrings with
{
    NumberFormat = LuaNumberFormat.Lua,
});
bytes.OpenStandardLibraries();
```

Libraries are opt-in. `LuaState.Create()` alone does not install them. To install a subset, call the corresponding extension in the table below. Configure standard I/O before opening its library: changing the provider later does not replace existing handles. Existing module closures also retain their captured package table.

Opening a library again republishes its globals/table; it is not an ensure-loaded operation. For example, reopening math replaces its random generator, and reopening I/O resets the default handles.

| Reference | C# initialization | Official library/global |
| --- | --- | --- |
| [Basic functions and globals](basic.md) | `OpenBasicLibrary()` | [Basic library](https://www.lua.org/manual/5.2/manual.html#6.1) |
| [Coroutines](coroutine.md) | `OpenCoroutineLibrary()` | [`coroutine`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine) |
| [Packages and modules](package.md) | `OpenModuleLibrary()` | [`package`](https://www.lua.org/manual/5.2/manual.html#pdf-package) |
| [Strings](string.md) | `OpenStringLibrary()` | [`string`](https://www.lua.org/manual/5.2/manual.html#pdf-string) |
| [Tables](table.md) | `OpenTableLibrary()` | [`table`](https://www.lua.org/manual/5.2/manual.html#pdf-table) |
| [Mathematics](math.md) | `OpenMathLibrary()` | [`math`](https://www.lua.org/manual/5.2/manual.html#pdf-math) |
| [Bitwise operations](bit32.md) | `OpenBitwiseLibrary()` | [`bit32`](https://www.lua.org/manual/5.2/manual.html#pdf-bit32) |
| [Files and standard I/O](io.md) | `OpenIOLibrary()` | [`io`](https://www.lua.org/manual/5.2/manual.html#pdf-io) |
| [Operating system](os.md) | `OpenOperatingSystemLibrary()` | [`os`](https://www.lua.org/manual/5.2/manual.html#pdf-os) |
| [Debugging](debug.md) | `OpenDebugLibrary()` | [`debug`](https://www.lua.org/manual/5.2/manual.html#pdf-debug) |

## Reading the API tables

Signatures use `[optional]` arguments and `...` for multiple values. The last column includes both implementation details and compatibility differences; a managed backend alone does not imply different Lua behavior. A blank notes cell means no additional note is documented for that API; the shared behavior below still applies. These are implementation notes, not a guarantee that every edge case matches every C Lua build.

Notes marked **Limitation** describe incomplete compatibility, not a recommended Lua idiom. Notes marked **Host-dependent** depend on a replaceable provider. Explanations identify the implementation mechanism; they do not imply that an existing limitation is intentional.

## Shared behavior

### Strings and positions

The default `LuaStringMode.Utf16` uses .NET UTF-16 code units. `LuaStringMode.Bytes` stores one byte per C# `char`, matching Lua 5.2's byte-string representation. This affects lengths, slices, pattern positions, character classes, and stream counts. Neither mode counts grapheme clusters. See [String modes](../string-modes.md) for conversions and stream configuration.

This difference allows ordinary C# strings to be used directly in the default mode. Choose byte mode for Lua scripts that depend on byte positions or binary file contents. The `b` in `io.open` does not select the state's string mode.

### Numbers and text

Lua numbers use .NET `double`. `LuaPlatform.NumberFormat` defaults to `DotNet`, using invariant .NET numeric text. `Lua` selects 14 significant digits for implicit conversions and portable signed `inf`/`nan` spellings. This is a formatting policy; it does not change arithmetic or parsing.

This setting affects numeric `tostring`, `print`, numeric I/O, `table.concat`, `gsub` replacements, and applicable `string.format` conversions. A particular C runtime may spell special values differently, including Windows `nan(ind)` or `nan(snan)`; the `Lua` policy deliberately uses portable spellings. See [number formatting](../../README.md#number-formatting).

### Async calls, cancellation, and errors

C# functions and platform providers can await asynchronous work. Lua execution awaits them; an incomplete `Task` is not a Lua `coroutine.yield`. Lua callbacks entered through non-yieldable library operations, such as a sort comparator or a `gsub` replacement, cannot yield across that boundary. This preserves Lua 5.2 behavior while allowing C# async integration.

Host execution cancellation propagates to C# rather than becoming a normal `pcall` result. Built-in stream reads check cancellation at entry and before buffer fills; cancellation cannot interrupt an already blocking synchronous `Stream.Read`. Error wording and tracebacks can contain .NET exception messages and C# frames.

### Memory and host objects

The .NET garbage collector owns object lifetimes. Lua weak-table `__mode` semantics and Lua `__gc` finalizers are not implemented; .NET collection does not supply these Lua protocols. Lua GC tuning is also unavailable, and `collectgarbage` has significant limitations documented in [Basic functions](basic.md). Close files explicitly when their close timing matters.

C# objects can be exposed as userdata, and host functions are wrapped as `LuaFunction` values. Their identity strings and debug metadata describe managed objects rather than native pointers. Host-frozen tables, including generated C# metatables, reject mutation even through raw table operations; this keeps their published interop definitions immutable.

Metatable-bearing host userdata implements `ILuaUserData`. Light-userdata object tokens have no metatable slot in the current managed metatable dispatch, so `debug.setmetatable` does not attach one to those tokens.

## Scope

The pages include missing APIs from Lua 5.2, not just registered functions. Legacy Lua 5.1 compatibility aliases and later-version additions are listed separately where useful. This reference covers Lua script APIs; it does not replace the C# API documentation.

Implementation entry point: [OpenLibsExtensions.cs](../../src/Lua/Standard/OpenLibsExtensions.cs).
