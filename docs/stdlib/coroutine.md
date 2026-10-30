# Coroutines

[Reference index](README.md) · [Official Lua 5.2 coroutine library](https://www.lua.org/manual/5.2/manual.html#6.2) · [Implementation](../../src/Lua/Standard/CoroutineLibrary.cs)

Install with `state.OpenCoroutineLibrary()`.

| API / official reference | Inputs and result | Lua-CSharp differences and reasons |
| --- | --- | --- |
| [`coroutine.create(f)`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine.create) | Creates a suspended coroutine whose body is `f`; returns a `thread`. | Also accepts a C# `LuaFunction` body, since host functions participate in the same managed call machinery. |
| [`coroutine.resume(co [, ...])`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine.resume) | Starts/resumes `co`; returns `true` followed by yielded or final results, or `false, error` for a script failure. Resume arguments become body arguments or yield results. | Can await asynchronous C# work. Host cancellation follows the managed execution cancellation protocol rather than becoming an ordinary script failure. |
| [`coroutine.running()`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine.running) | Returns the current thread and a boolean identifying the main thread. | Threads are represented by `LuaState` objects. They are not dedicated OS threads. |
| [`coroutine.status(co)`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine.status) | Reports `running`, `suspended`, `normal`, or `dead`. | Awaiting C# work does not itself create a Lua yield/resume exchange. |
| [`coroutine.wrap(f)`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine.wrap) | Returns a resume function that forwards results and raises script failures instead of returning a success boolean. | The wrapper is a managed closure capturing the coroutine. Its capture is visible to the supported debug upvalue APIs. |
| [`coroutine.yield(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-coroutine.yield) | Suspends the current coroutine and passes values to its resumer. Later resume arguments become the yield's results. | Main-thread yields and yields across non-yieldable library callbacks fail. C# `await` is separate: an async host callback can await without being allowed to call Lua `yield`. |

See [async and cancellation](README.md#async-calls-cancellation-and-errors) and the [C# coroutine guide](../../README.md#coroutines). No Lua 5.3/5.4 additions such as `coroutine.isyieldable` or `coroutine.close` are installed; this library targets Lua 5.2.
