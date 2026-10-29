# Lua-CSharp

High performance Lua interpreter implemented in C# for .NET and Unity

![img](docs/Header.png)

[![NuGet](https://img.shields.io/nuget/v/LuaCSharp.svg)](https://www.nuget.org/packages/LuaCSharp)
[![Releases](https://img.shields.io/github/release/nuskey8/Lua-CSharp.svg)](https://github.com/nuskey8/Lua-CSharp/releases)
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

English | [日本語](README_JA.md)

## Overview

Lua-CSharp is a library that provides a Lua interpreter implemented in C#. By integrating Lua-CSharp, you can easily embed Lua scripts into your .NET applications.

Lua-CSharp uses modern C# features to reduce interpreter and embedding overhead. The [.NET 10 benchmark guide](sandbox/Benchmark/README.md) separates execution, compilation and startup costs, with reproducible MoonSharp/NuLua Lua 5.2 comparisons. The earlier MoonSharp/NLua chart below uses a different benchmark setup:

![img](docs/Benchmark.png)

Performance and allocation costs depend on the workload and embedding API. The interpreter runs in managed C# and supports AOT environments without runtime IL generation.

## Features

* Lua 5.2 interpreter implemented in C#
* Easy-to-use API integrated with async/await
* Support for exception handling with try-catch
* High-performance implementation utilizing modern C#
* Unity support (works with both Mono and IL2CPP)

## Installation

### NuGet packages

To use Lua-CSharp, .NET Standard 2.1 or higher is required. The package can be obtained from NuGet.

### .NET CLI

```ps1
dotnet add package LuaCSharp
```

### Package Manager

```ps1
Install-Package LuaCSharp
```

### Unity

You can also use Lua-CSharp with Unity. For details, see the [Lua.Unity](#luaunity) section.

## Quick Start

By using the `LuaState` class, you can execute Lua scripts from C#. Below is a sample code that evaluates a simple calculation written in Lua.

```cs
using Lua;

// Create a LuaState
var state = LuaState.Create();

// Execute a Lua script string with DoStringAsync
var results = await state.DoStringAsync("return 1 + 1");

// 2
Console.WriteLine(results[0]);
```

> [!WARNING]
> `LuaState` is not thread-safe. Do not access it from multiple threads simultaneously.

## LuaValue

Values in Lua scripts are represented by the `LuaValue` type. The value of a `LuaValue` can be read using `TryRead<T>(out T value)` or `Read<T>()`.

```cs
var results = await state.DoStringAsync("return 1 + 1");

// double
var value = results[0].Read<double>();
```

You can also get the type of the value from the `Type` property.

```cs
var isNil = results[0].Type == LuaValueType.Nil;
```

Below is a table showing the type mapping between Lua and C#.

| Lua               | C#                       |
| ----------------- | ------------------------ |
| `nil`             | `LuaValue.Nil`           |
| `boolean`         | `bool`                   |
| `string`          | `string`                 |
| `number`          | `double`, `float`, `int` |
| `table`           | `LuaTable`               |
| `function`        | `LuaFunction`            |
| (light)`userdata` | `object`                 |
| `userdata`        | `ILuaUserData`           |
| `thread`          | `LuaState`               |

When creating a `LuaValue` from the C# side, compatible types are implicitly converted into `LuaValue`.

```cs
LuaValue value;
value = 1.2;           // double   ->  LuaValue
value = "foo";         // string   ->  LuaValue
value = new LuaTable() // LuaTable ->  LuaValue
```

## LuaTable

Lua tables are represented by the `LuaTable` type. They can be used similarly to `LuaValue[]` or `Dictionary<LuaValue, LuaValue>`.

```cs
// Create a table in Lua
var results = await state.DoStringAsync("return { a = 1, b = 2, c = 3 }");
var table1 = results[0].Read<LuaTable>();

// 1
Console.WriteLine(table1["a"]);

// Create a table in C#
results = await state.DoStringAsync("return { 1, 2, 3 }");
var table2 = results[0].Read<LuaTable>();

// 1 (Note: Lua arrays are 1-indexed)
Console.WriteLine(table2[1]);
```

## Global Environment

You can access Lua's global environment through `state.Environment`. This table allows for easy value exchange between Lua and C#.

```cs
// Set a = 10
state.Environment["a"] = 10;

var results = await state.DoStringAsync("return a");

// 10
Console.WriteLine(results[0]);
```

## Standard Libraries

You can use Lua's standard libraries as well. By calling `state.OpenStandardLibraries()`, the standard library tables are added to the `LuaState`.

```cs
using Lua;
using Lua.Standard;

var state = LuaState.Create();

// Add standard libraries
state.OpenStandardLibraries();

var results = await state.DoStringAsync("return math.pi");
Console.WriteLine(results[0]); // 3.141592653589793
```

For more details on standard libraries, refer to the [Lua official manual](https://www.lua.org/manual/5.2/manual.html#6).

> [!WARNING]
> Lua-CSharp does not support all functions of the standard libraries. For details, refer to the [Compatibility](#compatibility) section.

## Functions

Lua functions are represented by the `LuaFunction` type. With `LuaFunction`, you can call Lua functions from C#, or define functions in C# that can be called from Lua.

### Calling Lua Functions from C#

```lua
-- lua2cs.lua

local function add(a, b)
    return a + b
end

return add;
```

```cs
var state = LuaState.Create();
var results = await state.DoFileAsync("lua2cs.lua");
var func = results[0];

// Execute the function or any callable with arguments
var funcResults = await state.CallAsync(func, [1, 2]);

// 3
Console.WriteLine(funcResults[0]);
```

To avoid array allocation, an API is also provided that passes arguments using the stack.

```cs
var func = results[0];
var basePos = state.Stack.Count;
state.Push(func);
state.Push(1);
state.Push(2);
var funcResultsCount = await state.CallAsync(funcIndex: basePos, returnBase: basePos);

using (var reader = state.ReadStack(funcResultsCount))
{
    var span = reader.AsSpan();
    for (int i = 0; i < span.Length; i++)
    {
        Console.WriteLine(span[i]);
    }
}
```

### Calling C# Functions from Lua

It is possible to create a `LuaFunction` from a lambda expression.

```cs
// Add the function to the global environment
state.Environment["add"] = new LuaFunction((context, ct) =>
{
    // Get the arguments using context.GetArgument<T>()
    var arg0 = context.GetArgument<double>(0);
    var arg1 = context.GetArgument<double>(1);

    // Set the return value to the context
    context.Return(arg0 + arg1);
    
    // If there are multiple values, you need to pass them together as follows.
    // context.Return(arg0, arg1);
    // context.Return([arg0, arg1]);

    // Return the number of values
    return new(1);
    // return new(context.Return(arg0 + arg1)); // or this way
});

// Execute a Lua script
var results = await state.DoFileAsync("cs2lua.lua");

// 3
Console.WriteLine(results[i]);
```

```lua
-- cs2lua.lua

return add(1, 2)
```

> [!TIP]  
> Defining functions with `LuaFunction` can be somewhat verbose. When adding multiple functions, it is recommended to use the Source Generator with the `[LuaObject]` attribute. For more details, see the [LuaObject](#luaobject) section.

## Low-Level API

In addition to normal function calls, it is possible to directly call Lua's low-level API.

```csharp
await state.CallAsync(func, [arg1, arg2]); // func(arg1,arg2) in lua

await state.AddAsync(arg1, arg2); // arg1 + arg2 in lua
await state.SubAsync(arg1, arg2); // arg1 - arg2 in lua
await state.MulAsync(arg1, arg2); // arg1 * arg2 in lua
await state.DivAsync(arg1, arg2); // arg1 / arg2 in lua
await state.ModAsync(arg1, arg2); // arg1 % arg2 in lua

await state.EqualsAsync(arg1, arg2); // arg1 == arg2 in lua
await state.LessThanAsync(arg1, arg2); // arg1 < arg2 in lua
await state.LessThanOrEqualsAsync(arg1, arg2); // arg1 <= arg2 in lua

await state.ConcatAsync([arg1, arg2, arg3]); // arg1 .. arg2 .. arg3 in lua

await state.GetTableAsync(table, key); // table[key] in lua
await state.GetTableAsync(table, "x"); // table.x in lua
await state.SetTableAsync(table, key, value); // table[key] = value in lua
await state.SetTableAsync(table, "x", value); // table.x = value in lua
```

## Integration with async/await

`LuaFunction` operates asynchronously. Therefore, you can define a function that waits for an operation in Lua, such as the example below:

```cs
// Define a function that waits for the given number of seconds using Task.Delay
state.Environment["wait"] = new LuaFunction(async (context, ct) =>
{
    var sec = context.GetArgument<double>(0);
    await Task.Delay(TimeSpan.FromSeconds(sec));
    return context.Return();
});

await state.DoFileAsync("sample.lua");
```

```lua
-- sample.lua

print "hello!"

wait(1.0) -- wait 1 sec

print "how are you?"

wait(1.0) -- wait 1 sec

print "goodbye!"
```

This code can resume the execution of the Lua script after waiting with await, as shown in the following figure. This is very useful when writing scripts to be incorporated into games.

![img](docs/img1.png)

## Debug Hooks

`state.SetHook(hook, mask, count)` installs a hook for that Lua thread. The mask combines `c` (call), `r` (return), and `l` (line); a positive `count` enables instruction events even with an empty mask. The callback receives the event name and a line number (`nil` for non-line events). Return values, including `false`, are ignored. `state.SetHook(null, "")` removes the hook.

A hook can call `SetHook` to replace or remove itself or change its interval. Setting a hook resets the instruction counter, so subsequent count events use the new interval. Hooks on that thread are suppressed while the callback executes, including Lua code it invokes on the same thread.

Host hook settings are copied when `CreateThread`, `CreateCoroutine`, `coroutine.create`, or `coroutine.wrap` creates a child. The child starts a fresh instruction counter and uses the same callback with its own `context.State`; use that state to inspect or reconfigure the executing thread. Parent and child settings are independent: later changes do not update existing children, and a child created before hook installation remains unconfigured. Hook suppression is also independent, so a new child can fire hooks even when created inside a parent hook. Lua `debug.sethook` callbacks remain thread-local, as in [Lua 5.2](https://www.lua.org/source/5.2/ldblib.c.html#hookf); a child inherits their mask/count metadata without running the parent's Lua callback. Scripts given access to `debug.sethook` can still replace or remove a host hook.

Count events measure Lua instructions, as in [Lua 5.2](https://www.lua.org/manual/5.2/manual.html#lua_sethook). They do not fire during a C# host function's own execution or while awaiting it. If that host function calls back into Lua, the nested Lua instructions can trigger hooks. A timeout checked by a hook therefore cannot interrupt a blocking C# function. Long-running host functions should check the supplied `CancellationToken` or pass it to cancellable operations. A host-caught exception or cancellation from a hook does not prevent hooks from running on later executions using the same state.

Hook-based memory sampling is approximate: one Lua instruction or host operation can allocate substantially before the next event. `GC.GetTotalMemory(false)` reports the managed heap, not memory attributable to one Lua state, so it cannot enforce a strict per-state memory limit.

A `LuaRuntimeException` thrown by a hook is a Lua error that `pcall` or `coroutine.resume` can catch; it is not an uncatchable execution limit.

## Coroutines

Lua coroutines are represented by the `LuaState` type.

Coroutines can not only be used within Lua scripts, but you can also await Lua-created coroutines from C#.

```lua
-- coroutine.lua

 local co = coroutine.create(function()
    for i = 1, 10 do
        print("lua:", coroutine.yield(i - 1))
    end
end)

return co
```

```cs
var results = await state.DoFileAsync("coroutine.lua");
var co = results[0].Read<LuaState>();
var stack = new LuaStack();
for (int i = 0; i < 10; i++)
{
    var resumeResultsCount = await co.ResumeAsync(stack);

    // Similar to coroutine.resume(), returns true on success and the return values afterward
    // 1, 2, 3, 4, ...
    if (resumeResultsCount > 1)
    {
        Console.WriteLine(stack[1]);
    }

    stack.Clear();
    stack.Push(i);
}
```

## LuaObject

By applying the `[LuaObject]` attribute, you can create custom classes that run within Lua. Adding this attribute to a class that you wish to use in Lua allows the Source Generator to automatically generate the code required for interaction from Lua.

The following is an example implementation of a wrapper class for `System.Numerics.Vector3` that can be used in Lua:

```cs
using System.Numerics;
using Lua;

var state = LuaState.Create();

// Add an instance of the defined LuaObject as a global variable
// (Implicit conversion to LuaValue is automatically defined for classes with the LuaObject attribute)
state.Environment["Vector3"] = new LuaVector3();

await state.DoFileAsync("vector3_sample.lua");

// Add LuaObject attribute and partial keyword
[LuaObject]
public partial class LuaVector3
{
    Vector3 vector;

    // Add LuaMember attribute to members that will be used in Lua
    // The argument specifies the name used in Lua (if omitted, the member name is used)
    [LuaMember("x")]
    public float X
    {
        get => vector.X;
        set => vector = vector with { X = value };
    }

    [LuaMember("y")]
    public float Y
    {
        get => vector.Y;
        set => vector = vector with { Y = value };
    }

    [LuaMember("z")]
    public float Z
    {
        get => vector.Z;
        set => vector = vector with { Z = value };
    }

    // Static methods are treated as regular Lua functions
    [LuaMember("create")]
    public static LuaVector3 Create(float x, float y, float z)
    {
        return new LuaVector3()
        {
            vector = new Vector3(x, y, z)
        };
    }

    // Instance methods implicitly receive the instance (this) as the first argument
    // In Lua, this is accessed with instance:method() syntax
    [LuaMember("normalized")]
    public LuaVector3 Normalized()
    {
        return new LuaVector3()
        {
            vector = Vector3.Normalize(vector)
        };
    }
}
```

```lua
-- vector3_sample.lua

local v1 = Vector3.create(1, 2, 3)
-- 1  2  3
print(v1.x, v1.y, v1.z)

local v2 = v1:normalized()
-- 0.26726123690605164  0.5345224738121033  0.8017836809158325
print(v2.x, v2.y, v2.z)
```

The types of fields/properties with the `[LuaMember]` attribute, as well as the argument and return types of methods, must be either `LuaValue` or convertible to/from `LuaValue`.

Return types such as `void`, `Task/Task<T>`, `ValueTask/ValueTask<T>`, `UniTask/UniTask<T>`, and `Awaitable/Awaitable<T>` are also supported.

If the type is not supported, the Source Generator will output a compile-time error.

### Variable-length arguments

A final `ReadOnlySpan<LuaValue>` or `ReadOnlyMemory<LuaValue>` parameter receives the remaining Lua arguments. An injected `CancellationToken` may follow it. Fixed arguments are read first; an instance method's `self` is excluded, and `out` parameters consume no Lua arguments. No remaining arguments produces an empty view, and `nil` values are preserved.

```cs
[LuaObject]
public partial class Calculator
{
    [LuaMember]
    public double Sum(double first, ReadOnlySpan<LuaValue> rest)
    {
        foreach (var value in rest)
            first += value.Read<double>();
        return first;
    }

    [LuaMember]
    public async Task<int> CountAsync(ReadOnlyMemory<LuaValue> values, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return values.Length;
    }
}
```

```lua
local total = calculator:Sum(1, 2, 3) -- 6
local count = calculator:CountAsync(1, nil, 3) -- 3
```

These are borrowed, read-only views of the Lua argument stack. Use `ReadOnlyMemory<LuaValue>` when an async method needs the values after an `await`. Copy with `ToArray()` if the values must be retained after the method returns. Mutable views, `ref`/`out`/`in` views, other element types and C# `params LuaValue[]` are not supported. Pass the view by value, after any fixed parameters and before an optional injected token.

### LuaMetamethod

By adding the `[LuaMetamethod]` attribute, you can designate a C# method to be used as a Lua metamethod.

Here is an example that adds the `__add`, `__sub`, and `__tostring` metamethods to the `LuaVector3` class:

```cs
[LuaObject]
public partial class LuaVector3
{
    // The previous implementation is omitted

    [LuaMetamethod(LuaObjectMetamethod.Add)]
    public static LuaVector3 Add(LuaVector3 a, LuaVector3 b)
    {
        return new LuaVector3()
        {
            vector = a.vector + b.vector
        };
    }
    
    [LuaMetamethod(LuaObjectMetamethod.Sub)]
    public static LuaVector3 Sub(LuaVector3 a, LuaVector3 b)
    {
        return new LuaVector3()
        {
            vector = a.vector - b.vector
        };
    }

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString()
    {
        return vector.ToString();
    }
}
```

```lua
local v1 = Vector3.create(1, 1, 1)
local v2 = Vector3.create(2, 2, 2)

print(v1) -- <1, 1, 1>
print(v2) -- <2, 2, 2>

print(v1 + v2) -- <3, 3, 3>
print(v1 - v2) -- <-1, -1, -1>
```

> [!NOTE]  
> `__index` and `__newindex` cannot be set as they are used internally by the code generated by `[LuaObject]`.

Generated default metatables are immutable and shared by all instances of the same C# type, across Lua states. You can still assign writable C# members from Lua. To customize an individual object's metatable, replace `ILuaUserData.Metatable` in C# or use `debug.setmetatable` in Lua; replacement tables remain mutable. Ordinary Lua tables and metatables remain mutable.

## Module Loading

In Lua, you can load modules using the `require` function. In regular Lua, modules are managed by searchers within the `package.searchers` function list. In addition to this, Lua-CSharp provides `ILuaModuleLoader` as a module loading mechanism.

> [!NOTE]
> Module resolution by `ILuaModuleLoader` is performed before `package.searchers`.

```cs
public interface ILuaModuleLoader
{
    bool Exists(string moduleName);
    ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default);
}
```

You can set the `LuaState.ModuleLoader` to change how modules are loaded.

You can also combine multiple loaders using `CompositeModuleLoader.Create(loader1, loader2, ...)`.

```cs
state.ModuleLoader = CompositeModuleLoader.Create(
    new CustomModuleLoader1(),
    new CustomModuleLoader2()
);
```

Loaded modules are cached in the `package.loaded` table, just like regular Lua. This can be accessed via `LuaState.LoadedModules`.

## LuaPlatform

In Lua-CSharp, environment abstraction is provided as `LuaPlatform` for sandboxing.

```cs
var platform = new LuaPlatform(
    FileSystem: new FileSystem(),
    OsEnvironment: new SystemOsEnvironment(),
    StandardIO: new ConsoleStandardIO(),
    TimeProvider: TimeProvider.System);

var state = LuaState.Create(platform);
```

These are used for `require`, `print`, `dofile`, and the `os` module.

## Exception Handling

Lua script runtime exceptions throw exceptions that inherit from `LuaException`. You can catch these to handle errors during execution.

```cs
try
{
    await state.DoFileAsync("filename.lua");
}
catch (LuaCompileException)
{
    // Handle parsing errors
}
catch (LuaRuntimeException)
{
    // Handle runtime exceptions
}
catch(OperationCanceledException)
{
    // Handle cancel exceptions
    // LuaCanceledException allows you to get the cancellation point within Lua.
}
```

## Lua.Unity

Lua-CSharp can also be used in Unity (works with both Mono and IL2CPP).

### Requirements

* Unity 2021.3 or higher

### Installation

1. Install [NugetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).

2. Open the NuGet window by going to `NuGet > Manage NuGet Packages`, search for the `LuaCSharp` package, and install it.

3. Open the Package Manager window by selecting `Window > Package Manager`, then click on `[+] > Add package from git URL` and enter the following URL:

    ```
    https://github.com/nuskey8/Lua-CSharp.git?path=src/Lua.Unity/Assets/Lua.Unity
    ```

### LuaImporter / LuaAsset

By introducing Lua.Unity, files with the `.lua` extension can be treated as `LuaAsset`.

![img](docs/img-lua-importer.png)

These assets can be used similarly to a standard `TextAsset`.

```cs
var asset = Resources.Load<LuaAsset>("example");
await state.DoStringAsync(asset.Text, ct);
```

### Resources(Addressables)ModuleLoader

Implementations of `ILuaModuleLoader` that utilize either Resources or Addressables internally are also provided.

```cs
// Use Resources for module loading
state.ModuleLoader = new ResourcesModuleLoader();

// Use Addressables for module loading (requires the Addressables package)
state.ModuleLoader = new AddressablesModuleLoader();
```

### UnityStandardIO / UnityApplicationOsEnvironment

`UnityStandardIO` and `UnityApplicationOsEnvironment` are provided as LuaPlatform elements for Unity.

```cs
var platform = new LuaPlatform(
    FileSystem: new FileSystem(),
    OsEnvironment: new UnityApplicationOsEnvironment(), // OsEnvironment for Unity
    StandardIO: new UnityStandardIO(), // StandardIO for Unity
    TimeProvider: TimeProvider.System);
var state = LuaState.Create(platform);
```

With `UnityStandardIO`, standard output such as `print` will be output to `Debug.Log()`. With `UnityApplicationOsEnvironment`, environment variables can be set using a Dictionary, and `os.exit()` will call `Application.Quit()`.

These are simple implementations only, so for actual use it is recommended creating your own implementations as needed.

## Compatibility

Lua-CSharp is designed with integration into .NET in mind, so there are several differences from the C implementation.

### Character Encoding

By default, Lua-CSharp uses UTF-16. Since standard Lua assumes a single-byte character encoding, string behavior differs significantly. See [String modes](docs/string-modes.md) for UTF-16 and byte-string behavior and C# conversions.

For example, in regular Lua, the following code outputs `15`, but in Lua-CSharp, it outputs `5`.

```lua
local l = string.len("あいうえお")
print(l)
```

In the default mode, string library functions handle strings as UTF-16.

#### Byte strings (opt-in)

If you need byte-exact strings like the reference C implementation (for example `#"ế"` is `3`, `string.sub` cuts inside a UTF-8 sequence, and files are read and written as raw bytes), use `LuaStringMode.Bytes`. `LuaPlatform.StringMode` defaults to `LuaStringMode.Utf16`. `LuaPlatform.DefaultByteStrings` provides byte strings and byte-oriented file and standard I/O:

```cs
using Lua;
using Lua.Platforms;
using Lua.Standard;

var state = LuaState.Create(LuaPlatform.DefaultByteStrings);
state.OpenStandardLibraries();

// Every char of a Lua string holds one byte, so convert .NET strings at the boundary.
state.Environment["name"] = state.Platform.EncodeText("Tiến Lên"); // UTF-8 bytes, #name == 11
var results = await state.DoStringAsync("return #name, name:upper()");
var upper = state.Platform.DecodeText(results[1].Read<string>()); // back to a .NET string
```

In this mode `string.upper`, `string.lower` and the pattern classes (`%a`, `%w`, `%l`, ...) follow the C locale (ASCII only), `string.char` rejects values above 255, and UTF-8 source files are loaded byte by byte (a UTF-8 BOM is skipped). The default stays UTF-16.

Choose one representation for a state and configure it before executing scripts. Setting `StringMode = LuaStringMode.Bytes` on an otherwise unchanged platform does not switch its streams to raw bytes. Use matching modes for the platform and its streams. When configuring a file root, keep that root as a normal .NET path:

```cs
var platform = LuaPlatform.DefaultByteStrings with
{
    FileSystem = new Lua.IO.FileSystem(scriptsDirectory, LuaStringMode.Bytes)
};
using var byteState = LuaState.Create(platform);
byteState.OpenStandardLibraries();

// C# source and filename APIs accept normal .NET text.
var values = await byteState.DoStringAsync("return 'こんにちは'");
var greeting = LuaByteString.DecodeUtf8(values[0].Read<string>());

// Raw packets can contain NUL, invalid UTF-8 and every value from 0 to 255.
byteState.Environment["packet"] = LuaByteString.FromBytes(new byte[] { 0xFF, 0, 0x80 });
var packet = await byteState.DoStringAsync("return packet:reverse()");
byte[] reversed = LuaByteString.ToBytes(packet[0].Read<string>());
```

Use `platform.EncodeText`/`DecodeText` for text in either mode: they return the input unchanged in UTF-16 mode and encode/decode UTF-8 in byte mode. `LuaByteString.EncodeUtf8`/`DecodeUtf8` always use UTF-8; use `FromBytes`/`ToBytes` for binary data. This also applies to non-ASCII table keys and the arguments/results of C# functions and generated bindings; value interop does not convert strings automatically. C# source APIs (`DoStringAsync`, `Load` with character source or a `LuaModule`) accept normal .NET text and encode it as UTF-8 in byte mode. Chunk names and paths passed to `LoadFileAsync`/`DoFileAsync` are also .NET text. Byte source passed to `Load`/`ExecuteAsync` and source read from files are preserved directly; their explicit chunk names are .NET text. Lua's `load`, `loadfile`, `dofile` and `require` continue to use Lua byte strings without re-encoding their source.

Paths and names passed to `ILuaFileSystem`, `ILuaFileLoader` and `ILuaModuleLoader` are normal .NET strings in both modes. Text modules are treated as .NET text and encoded to UTF-8 in byte mode; byte modules remain bytes. Unity text assets use the same conversion. Custom file systems and standard-I/O providers must implement the selected stream representation themselves; use `ILuaStream.CreateFromStream(stream, mode, LuaStringMode.Bytes)` for raw streams.

Files, temporary files and redirected standard I/O preserve bytes, including a BOM read as data. Raw line reads stop at LF and retain CR; `*L` also retains the LF. Invalid UTF-8 is replaced when decoding with `DecodeUtf8`, so keep binary data in byte strings or byte arrays. Passing unconverted .NET characters above U+00FF to raw output writes `?` for those characters.

### Garbage Collection

Since Lua-CSharp is implemented in C#, it relies on .NET's garbage collector. As a result, memory management behavior differs from regular Lua.

While `collectgarbage()` is available, it simply calls the corresponding .NET garbage collection method and may not exhibit the same behavior as C's Lua garbage collector.

## License

Lua-CSharp is licensed under the [MIT License](LICENSE).
