# Packages and modules

[Reference index](README.md) · [Official Lua 5.2 package library](https://www.lua.org/manual/5.2/manual.html#6.3) · [C Lua 5.2.4 source](https://www.lua.org/source/5.2/loadlib.c.html) · [Implementation](../../src/Lua/Standard/ModuleLibrary.cs)

Install with `state.OpenModuleLibrary()`. This installs global `require` and the `package` table. Each opened standard library is registered in the loaded-module table.

| API / official reference | Inputs and result | Lua-CSharp implementation notes and differences |
| --- | --- | --- |
| [`require(name)`](https://www.lua.org/manual/5.2/manual.html#pdf-require) | Returns a truthy cached module or searches for a loader. Calls the loader with `name` and searcher data; caches a non-nil result, or uses the loaded entry/default `true`. A false cache entry allows a later reload. | **Host-dependent:** a configured `ILuaModuleLoader` is consulted before package searchers when it reports the module exists. This lets C# supply resources/assets without native filesystem loading. The package object is captured when the library is opened, so replacing global `package` does not retarget an existing `require`. Loader/searcher callbacks cannot Lua-yield, but host loaders can await. |
| [`package.config`](https://www.lua.org/manual/5.2/manual.html#pdf-package.config) | A newline-delimited string describing directory, path-list, substitution, executable-directory, and ignore markers. | The directory character is the host .NET separator; the remaining markers are `;`, `?`, `!`, and `-`. **Limitation:** the initializer omits C Lua's final newline, so a parser requiring a newline after every marker can miss the last one. |
| [`package.cpath`](https://www.lua.org/manual/5.2/manual.html#pdf-package.cpath) | Officially configures native C module search paths. | **Unavailable:** no field or native C-module searcher is installed. Lua-CSharp uses managed bindings and host module loaders rather than Lua's C ABI. |
| [`package.loaded`](https://www.lua.org/manual/5.2/manual.html#pdf-package.loaded) | The table used to cache module values. | Backed by the state's managed loaded-module table. As in Lua 5.2, replacing the `package.loaded` field does not replace the cache used by `require`; mutate its entries instead. |
| [`package.loadlib(libname, funcname)`](https://www.lua.org/manual/5.2/manual.html#pdf-package.loadlib) | Officially loads a native library and its Lua C entry point. | **Unavailable:** not registered. A native Lua C function cannot run directly against Lua-CSharp's managed state/stack representation. Expose C# functions or an `ILuaModuleLoader` instead. |
| [`package.path`](https://www.lua.org/manual/5.2/manual.html#pdf-package.path) | A semicolon-separated list of Lua file templates used by the Lua searcher; `?` is replaced with the module path. | Starts as `?.lua`. The library does not initialize it from `LUA_PATH`, versioned environment variables, or an executable installation directory. Configure it explicitly or supply a host module loader. |
| [`package.preload`](https://www.lua.org/manual/5.2/manual.html#pdf-package.preload) | Maps module names to preloaded loader functions, used by searcher 1. | Stored in the managed preload table. Preload entries may be Lua or C# functions. |
| [`package.searchers`](https://www.lua.org/manual/5.2/manual.html#pdf-package.searchers) | Ordered searcher functions returning a loader and optional data, or an error-description string. A missing integer entry ends the search. | Initially contains only preload and Lua-file searchers, in that order. Native C and C-root searchers are absent because there is no native C-module loader. Applications may add Lua/C# searchers. |
| [`package.searchpath(name, path [, sep [, rep]])`](https://www.lua.org/manual/5.2/manual.html#pdf-package.searchpath) | Replaces `sep` (default `.`) in the name with `rep`, tries templates in `path`, and returns the matching filename or `nil, diagnostics`. | **Host-dependent:** readability is decided by `ILuaFileSystem.IsReadable`; the default uses `File.Exists`, not an actual open attempt. Default `rep` comes from the filesystem provider's directory separator. |

## Host configuration

Paths returned to Lua use the state's string representation. Paths passed to filesystem/module providers are .NET text, decoded from byte strings where necessary. See [module loading from C#](../../README.md#module-loading).

The default package initializer does not consult the C Lua path environment variables:

| Official environment setting | Lua-CSharp behavior and reason |
| --- | --- |
| [`LUA_PATH_5_2`](https://www.lua.org/manual/5.2/manual.html#pdf-LUA_PATH_5_2) | Not read; `package.path` starts as `?.lua` and is configured by Lua/C# code. |
| [`LUA_PATH`](https://www.lua.org/manual/5.2/manual.html#pdf-LUA_PATH) | Not used as a fallback; the initializer has no environment-path expansion step. |
| [`LUA_CPATH_5_2`](https://www.lua.org/manual/5.2/manual.html#pdf-LUA_CPATH_5_2) | Not read; native C-module search/loading is absent. |
| [`LUA_CPATH`](https://www.lua.org/manual/5.2/manual.html#pdf-LUA_CPATH) | Not read for the same native-loader limitation. |

The deprecated `module`/`package.seeall` compatibility helpers and `package.loaders` alias are not installed. Use Lua 5.2 module tables and `package.searchers`; see [official library changes](https://www.lua.org/manual/5.2/manual.html#8.2).
