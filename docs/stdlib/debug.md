# Debugging

[Reference index](README.md) · [Official Lua 5.2 debug library](https://www.lua.org/manual/5.2/manual.html#6.10) · [C Lua 5.2.4 source](https://www.lua.org/source/5.2/ldblib.c.html) · [Implementation](../../src/Lua/Standard/DebugLibrary.cs)

Install with `state.OpenDebugLibrary()`. Thread-aware functions accept an optional first `thread`; otherwise they inspect the current state. Metadata describes Lua bytecode and managed call frames, not a native Lua C stack.

| API / official reference | Inputs and result | Lua-CSharp implementation notes and differences |
| --- | --- | --- |
| [`debug.debug()`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.debug) | Officially enters an interactive debug command loop. | **Unavailable:** not registered. Interactive console/debugger integration must be provided by the host. |
| [`debug.gethook([thread])`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.gethook) | Returns the installed hook, mask, and count. After removing the hook: nil, empty mask, and zero count. | A host-installed hook is returned as its managed `LuaFunction`; C Lua reports a hook installed through its C API as the string `"external hook"`. The host hook's execution policy can differ from a Lua-installed hook; see below. |
| [`debug.getinfo([thread,] f [, what])`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.getinfo) | Returns function/frame metadata selected by `n`, `S`, `l`, `u`, `t`, `f`, and `L`, or nil for a missing stack level. | C# functions report `what="C#"` and `source="=[C#]"` instead of C-function labels. `u` exposes managed closure capture counts. **Limitation:** numeric strings are not accepted as stack levels, because the function/level dispatch tests the Lua value tag before numeric coercion. |
| [`debug.getlocal([thread,] f, index)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.getlocal) | Inspects a frame local/temporary/vararg, or a Lua function's parameter name; returns nil when no such local exists. | Managed frames expose their Lua-facing argument/temporary slots, not arbitrary C# local variables. The implementation operates on Lua-CSharp stack metadata. |
| [`debug.getmetatable(value)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.getmetatable) | Returns the actual metatable, bypassing `__metatable` protection. | May return a host-frozen generated metatable. Reading the actual table does not make its contents writable. |
| [`debug.getregistry()`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.getregistry) | Returns the state's registry table. | Registry contents are managed runtime state; C API integer-slot conventions and native pointers are not a portable contract for Lua-CSharp. |
| [`debug.getupvalue(f, index)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.getupvalue) | Returns an upvalue name/value, or no values for an invalid slot. | Supports Lua closures and `CSharpClosure.UpValues`. Managed capture names and stripped Lua upvalue names are empty strings, as with C-function captures and stripped Lua names in C Lua 5.2.4. |
| [`debug.getuservalue(u)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.getuservalue) | Reads a userdata's first user-value slot, or nil when absent. | Uses `ILuaUserData.UserValues`. Host userdata can expose no slots; values are `LuaValue` entries rather than Lua 5.2's table/nil-only uservalue model. |
| [`debug.sethook([thread,] hook, mask [, count])`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.sethook) | Installs/removes a hook; mask letters are `c`, `r`, and `l`, with an optional instruction count. | Lua-installed hooks cannot Lua-yield. They can call C# code that awaits. Counting is based on VM instructions, not time spent inside a C# function. See [hook details](#hook-details). |
| [`debug.setlocal([thread,] level, index, value)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.setlocal) | Writes a frame local and returns its name, or nil when unavailable. | Writes Lua-facing managed stack slots; it cannot set arbitrary CLR locals. |
| [`debug.setmetatable(value, mt)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.setmetatable) | Replaces/removes the actual metatable, bypassing `__metatable` protection; returns the value. | Host-frozen tables still reject changing their metatable because `LuaTable.Freeze()` protects managed storage. **Limitation:** light-userdata object tokens have no case in the managed metatable dispatcher, so setting their metatable has no effect. |
| [`debug.setupvalue(f, index, value)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.setupvalue) | Writes an upvalue and returns its name, or no values for an invalid slot. | Can update managed closure capture slots; their names are empty. Stripped Lua names are also empty, consistently with `getupvalue` and C Lua 5.2.4. |
| [`debug.setuservalue(u, value)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.setuservalue) | Writes the first user-value slot and returns the userdata. | **Difference/limitation:** any Lua value is accepted, rather than Lua 5.2's table/nil restriction. Userdata exposing no `UserValues` storage returns nil. This reflects the host-defined `Span<LuaValue>` storage API. |
| [`debug.traceback([thread,] [message [, level]])`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.traceback) | Builds a traceback; non-string/non-number messages pass through unchanged after level validation. | Formats managed/C# frames and Lua source metadata rather than reproducing C Lua's exact trace text. See [traceback details](#traceback-details) for argument and formatting limitations. |
| [`debug.upvalueid(f, index)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.upvalueid) | Returns an identity token for an upvalue slot; joined Lua slots share an identity. | Uses weakly associated managed identity objects instead of a native address. Retaining the token does not retain the captured payload; this fits managed GC without exposing movable CLR storage addresses. |
| [`debug.upvaluejoin(f1, n1, f2, n2)`](https://www.lua.org/manual/5.2/manual.html#pdf-debug.upvaluejoin) | Makes one Lua closure's slot share another Lua closure's upvalue. | Restricted to Lua closures, as in Lua 5.2. Managed capture arrays can be inspected/updated but are not joined by this API. |

## Hook details

Hook callbacks receive an event string and a line number or nil. Events include `call`, `return`, `tail call`, `line`, and `count`. Count hooks measure executed Lua instructions; they do not tick while execution is waiting inside a C# function. Nested Lua execution from C# can execute instructions and trigger hooks.

Hooks installed from C# through `LuaState.SetHook` retain the host hook policy, including supported async/yield behavior; they are distinguished internally from `debug.sethook` callbacks. This lets host instrumentation integrate with async execution while keeping Lua-installed callbacks compatible with non-yieldable Lua 5.2 hooks. See the [C# debug-hook guide](../../README.md#debug-hooks).

Stripped bytecode has no line/local names to recover. Source/line fields and local lookup reflect the metadata actually present; dumping and reloading a stripped chunk cannot reconstruct it. **Limitation:** `getinfo` can report an unknown Lua line as `0` instead of C Lua's `-1`, because the managed line lookup uses zero when line info is absent.

## Traceback details

The current formatter and argument handling have these limitations:

- Explicit negative levels return nil rather than a traceback string.
- Supplying the current thread explicitly defaults to level 0 instead of C Lua's current-thread level 1.
- Numeric messages use the managed value's `ToString`, independently of `NumberFormat`.
- The level is validated before passing through a non-string/non-number message. For example, `debug.traceback({}, 'bad')` errors; C Lua returns the table without inspecting the level.
