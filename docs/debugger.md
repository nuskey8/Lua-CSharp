# C# Lua debugger and DAP adapter

`Lua.Debugging.LuaDebugger`, in the optional `LuaCSharp.Debugger` package, provides protocol-independent controls for .NET 6, 8, 10 and .NET Standard 2.1. `LuaCSharp.Debugger.Dap` implements the [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/overview) in C# for an existing Lua host and references that package. The adapter targets .NET 8 and .NET Standard 2.1. Applications own the scripts and execution lifecycle, and can choose asynchronous or blocking stops. See [hosting user-owned scripts](#hosting-user-owned-scripts) and the [practical application sample](#practical-application-sample).

The interpreter retains only private instruction instrumentation and an internal session bridge. Breakpoints, stepping, source management, inspection and pause policies live in `Lua.Debugger`; protocol, JSON and transport live in `Lua.Debugger.Dap`. The interpreter has no dependency on either debugger assembly and still uses one VM loop. Direct debugger hosts reference `LuaCSharp.Debugger`; DAP hosts need only `LuaCSharp.Debugger.Dap`. The `Lua.Debugging` namespace is unchanged.

## Embedded host

Use an `@`-prefixed absolute filename so editor breakpoints match the compiled source. Registering source text lets the editor retrieve it without reading the host filesystem.

```csharp
using Lua;
using Lua.Debugger.Dap;
using Lua.Standard;

using var state = LuaState.Create();
state.OpenStandardLibraries();
var path = Path.GetFullPath("script.lua");
var source = await File.ReadAllTextAsync(path);
var closure = state.Load(source, "@" + path);

using var server = new LuaDapServer(state, port: 4711);
server.Debugger.RegisterSource(closure, source);
var listening = server.RunAsync();
try
{
    await server.WaitForConfigurationAsync();
    await state.ExecuteAsync(closure);
    await server.NotifyTerminatedAsync();
}
finally
{
    server.Dispose();
    await listening;
}
```

The server binds to loopback by default; another bind address is an explicit host choice. It serves one client at a time and supports reconnecting. It does not own the state, launch the host process, or terminate it on disconnect. `WaitForConfigurationAsync` waits for the first client's `configurationDone`, including installing an entry-stop request, before execution starts. Hosts that should run without an editor can omit that wait.

Disconnect and EOF clear session controls and release a stop. The server keeps its debugger installed for later clients; server disposal detaches it. Host functions used by an evaluation must honor their cancellation token so disconnect can cancel that evaluation. Pass the application's lifetime token to execution and the server when needed. A pause cannot interrupt arbitrary C# code or an outstanding host await. Normal Lua-state ownership rules apply: do not dispose the state or execute it concurrently while it is running or stopped.

For another transport, use `LuaDapSession(debugger, inputStream, outputStream)` and await `RunAsync`. The caller owns the streams. Use one active DAP session per debugger.

## Hosting user-owned scripts

The debug target is the application's `LuaState`, including every user file, module and coroutine loaded into it. The application owns its state, script loader, source storage, execution schedule and shutdown. The DAP server exposes that existing runtime; it does not decide which user code the application should run.

Use `LuaDebuggerOptions` with either `LuaDebugger` or `LuaDapServer`:

```csharp
var options = new LuaDebuggerOptions
{
    PauseMode = LuaDebugPauseMode.Asynchronous,
    // Optional: map uploaded/resource/generated chunk names to editor files.
    SourcePathResolver = chunkName => FindEditorPath(chunkName),
    // Optional: provide the source snapshot that the application actually compiled.
    SourceTextProvider = (source, token) => ReadApplicationSourceAsync(source, token),
};
using var server = new LuaDapServer(state, options, port: 4711);
```

`SourcePathResolver` receives the decoded chunk name, including its `@` or `=` prefix. Return null to retain the usual filename/in-memory mapping. `SourceTextProvider` runs when the editor requests source, possibly on the protocol thread; it can read an application cache, resource store, upload or database without exposing the Lua stack. Use host-owned source data rather than reading live Lua storage. Registered source text takes precedence, and a null provider result falls back to the state's filesystem. For fileless scripts, a source reference and registered/provided text work without any editor file path. These callbacks concern source ownership and display; the application still loads and executes its code normally.

Choose the pause policy to match the host:

| Policy | Lua execution at a stop | Application behavior |
| --- | --- | --- |
| `Asynchronous` (default) | Returns a pending execution task; resumes through the host's synchronization context. | A UI/game loop can continue processing unrelated work while that script call is suspended. Do not start another call on the same state until the pending call completes. |
| `Synchronous` | Keeps the execution thread inside the stop and services debugger commands there. | A UI/game thread's ordinary updates stop. A service's other threads can continue. Awaited debugger evaluation uses a private synchronization context to return to the stopped thread; ordinary UI callbacks are not pumped. |

Both modes use the same VM loop and checkpoints. The choice is consulted only for an actual stop, not on each instruction. Asynchronous pause does not move ordinary execution to a background thread or introduce time slicing. Synchronous mode does not make normal asynchronous host functions synchronous; it chooses how a debugger stop waits. A synchronous stop blocks the thread currently executing Lua; a previous asynchronous host call may already have released its original caller. Work explicitly posted to the application's blocked dispatcher remains queued, so use asynchronous pause for evaluations that depend on that dispatcher. Neither policy automatically freezes other states or the application's world. If the application needs a global pause, its own scheduling policy can respond to `Stopped`/`StopEnded` or `CurrentStop`. Cancellation and disconnect release either kind of stop.

Install the debugger/listener before starting execution. The editor may connect later while the application is running. Waiting for `WaitForConfigurationAsync` is optional and belongs at the point where the application wants to hold user-code execution for the first editor, rather than unconditionally holding its startup. Keep the listener alive across repeated calls and disconnects. Call `NotifyTerminatedAsync` when the debugged application/runtime ends, not after every update callback returns.

Each listener currently represents one root state and its coroutines; independently executing states can use separate listeners/ports. A single session does not multiplex independent states.

## Practical application sample

[sandbox/DebuggerHost](../sandbox/DebuggerHost) is a long-running embedding example. It loads a user-selected entry file and modules from an application-owned directory. The game entry returns `update(frame, dt)`; the service entry returns `handle(job)`. The application invokes either without overlapping calls. The game scripts maintain an actor across updates; the service scripts price incoming jobs and return results to C#. Both use separate Lua modules and a C# text-loading function. Logical `=user/...` chunk names map back to editor files, while source requests receive the text that was compiled.

In the development window described below, select **Debug user-script host** and press **F5**. Choose the game/service host, pause policy, script directory and optional entry file at the prompts. A blank entry uses `main.lua` for games or `worker.lua` for services. The game defaults use [UserScripts/main.lua](../sandbox/DebuggerHost/UserScripts/main.lua); set a breakpoint on line 10 or in [movement.lua](../sandbox/DebuggerHost/UserScripts/movement.lua) on line 9. Step across the module call, inspect `actor`, `step` and the `updates` upvalue, or evaluate `host.read_text("greeting.txt")`. For services, use [worker.lua](../sandbox/DebuggerHost/UserScripts/worker.lua) and [pricing.lua](../sandbox/DebuggerHost/UserScripts/pricing.lua), with a breakpoint on line 6 of `pricing.lua` to inspect/edit `subtotal` and `tax`.

The game host uses a single-thread dispatcher as a console stand-in for a UI/game application. Its `[app]` heartbeat and frame counter continue at an asynchronous stop and stop at a synchronous one. The service host has no UI synchronization context: its independent heartbeat continues even when the execution thread is blocked. No sample dispatcher is required in a real UI framework that already supplies its own synchronization context. A pending Lua update stays pending while the application continues; skipped frames do not cause concurrent/reentrant execution.

To start an application yourself and attach later:

```sh
dotnet run -c Release --project sandbox/DebuggerHost -- --scripts sandbox/DebuggerHost/UserScripts --host game --pause async
dotnet run -c Release --project sandbox/DebuggerHost -- --scripts /path/to/user/scripts --entry worker.lua --host service --pause sync --port 4712
```

Run one of these commands, then choose **Attach to Lua application** in the editor, adjusting its port when needed. That launch profile starts no application process and works with your own C# host as well. Omit `--wait-for-debugger` to run without an editor; add it to hold the first user call until configuration is complete. `--frames N` provides a finite run for batch use. Otherwise the host runs until Ctrl+C. Disconnecting resumes the script and leaves the application running, so the editor can reconnect. Stop the task/Ctrl+C to end the application. The sample loads code once; restart it to apply script edits. It does not implement hot reload.

## VS Code example

The small connector in [editor-extensions/vscode](../editor-extensions/vscode) registers the debugger type and connects to the C# server. It has no DAP request handling or Lua runtime.

The repository includes [launch configurations](../.vscode/launch.json) and a [sample task](../.vscode/tasks.json). With the .NET 10 SDK installed:

1. Open the repository root in VS Code. In Run and Debug, select **Run Lua debugger extension** and press **F5**. This opens an Extension Development Host with the local connector loaded and the repository available through a dedicated workspace file; no npm installation or extension build is needed.
2. In that new window, open [sandbox/Debugger/example.lua](../sandbox/Debugger/example.lua) and set a breakpoint on line 8.
3. Select **Debug Lua sample** and press **F5**. Its task builds and starts the C# host, waits for the listener's readiness message, and then attaches on `127.0.0.1:4711`.
4. Execution stops on entry. Press **F5** to reach the breakpoint, **F11** to enter `add`, **F10** to step over, or **Shift+F11** to step out. Inspect locals or evaluate `total` in the Debug Console.

The sample prints `total 6` in its task terminal and exits. Press F5 with **Debug Lua sample** selected to start a fresh run. To stop an active session, disconnect the debugger; the host resumes and finishes. If needed, terminate the sample task from the terminal. After loading the connector, subsequent runs need only the sample configuration in that development window.

VS Code skips a folder that is already open when launching an Extension Development Host. The launcher therefore opens [.vscode/lua-debugger.code-workspace](../.vscode/lua-debugger.code-workspace), which includes the repository under a distinct workspace identity. Close any previously opened empty development window before retrying the launcher; manually opening the repository folder can focus the original window instead.

## Stops and steps

A dedicated VM checkpoint runs before a selected instruction executes. It does not replace `LuaState.SetHook` or Lua's `debug.sethook`, insert Lua callback frames, change coroutine status, or patch shared bytecode. It covers existing and future coroutines sharing the global state. Install the core debugger before execution starts; `LuaDapServer.RunAsync` does this before accepting clients, so an editor can attach to an already-running host.

An asynchronous stop awaits a signal; a synchronous stop waits on its debugger command queue. Both process inspection commands on the suspended execution, and the protocol reader continues receiving requests without reading live stack storage itself. Frame/variable handles expire on resume, cancellation, or disconnect; table references are cleared when the stop ends.

DAP inspections are serialized separately from execution controls. Continue and stepping can cancel an awaiting evaluation; execution resumes after that inspection unwinds. Requests retain the stop ID observed when received, so queued inspections cannot operate on a later stop. An application resume racing an editor resume preserves the newer stop and produces one response to the editor request. Failures from inspection cancellation callbacks after an acknowledged resume are reported through DAP stderr output.

An editor connecting to an existing stop receives it after configuration completes. Disconnect releases the stop and clears session controls after pending requests finish. Replacing the debugger while execution is idle also covers coroutines that yielded under its predecessor. If posted work throws during a synchronous stop, pending inspections are cancelled and drained before the failure unwinds Lua execution.

The installation guard covers host execution on ordinary `CreateThread()` states and coroutine states, including `RunAsync`, `ExecuteAsync`, `CallAsync` and host metamethod calls, as well as coroutine resumes through `ResumeAsync(LuaStack)`. The outermost host entry and each coroutine resume are tracked until they return, yield or fail. A yielded coroutine is idle for debugger installation even though it still has suspended call frames. This tracking runs at execution boundaries, without per-instruction checks.

Session teardown unsubscribes handlers, resets controls and drains its protocol workers even if an inspection's cancellation callback throws. Server disposal also closes its transport and disposes its debugger before propagating a cancellation callback failure. Disposing a debugger releases its current stop; `StopEnded` is raised after pending inspection cleanup finishes, allowing the host to release its own application pause. Disposal does not wait for that cleanup, so the host must still await its execution before disposing the Lua state.

The synchronous pump drains accepted callbacks even when a stop is resumed immediately by a `Stopped` handler. It also finishes draining when a cancellation callback throws, then reports the original posted failure. While a client is connected, application-driven resume, reset or execution cancellation sends a `continued` event. Client continue/step requests already imply resumption and do not produce a duplicate event.

| Command | Stop rule |
| --- | --- |
| Pause / entry | At the next source/control-flow checkpoint; entry stops before the first instruction. |
| Source breakpoint | At a verified source-line event, including loop revisits. |
| Step in | At the next line event, including a called function or resumed coroutine. |
| Step over | At the next line in the original frame, or after it returns. A tail-call replacement is distinct. |
| Step out | After the original frame returns. A coroutine yielding/completing can transfer the stop to its caller. |

Frame identity supplements depth to handle tail calls. A pending step ends when its owning Lua stack has unwound and execution returns to the host; breakpoints remain installed for later invocations. A yielded coroutine retains its step across direct C# resumes because its Lua frames are still live. Hooks already executed for a stopped instruction are not executed again on resume. Locals, upvalues, varargs, globals, and raw table/userdata contents are inspected lazily with paging. Value display does not call user `__tostring`.

Source paths use host path/case rules. Breakpoints resolve to the first executable line at or after the requested line. Unknown sources remain pending until registered/executed, with breakpoint-change events on resolution. There is no basename-only fallback. For an in-memory chunk, use an `=name` chunk name, register its text, and use the source reference supplied by the adapter.

## Evaluation and supported requests

Evaluate accepts Lua expressions. Locals and upvalues are copied into an evaluation environment, with lookup falling back to the selected frame's environment. Explicit `_ENV` refers to that evaluation environment. Evaluation does not retain writable pointers to a paused frame. Expressions can call Lua/C# functions and therefore have side effects. Lua yielding is disabled across evaluation; C# functions can await. Evaluation failures do not invoke the program's `xpcall` handler. `setVariable` writes locals, upvalues, or globals explicitly; raw table/userdata children are read-only.

Supported requests: initialize, attach, configurationDone, setBreakpoints, pause, continue, next, stepIn, stepOut, threads, stackTrace, scopes, variables, evaluate, setVariable, source, loadedSources, and disconnect. Empty exception-breakpoint configuration is accepted. Client line/column bases and file-URI paths are respected. One DAP thread represents the global state's execution and its coroutines. Stack inspection shows the active coroutine, without stitching its parent's stack.

The adapter does not advertise launch/process control, exception breakpoints, conditional/hit-count/log breakpoints, reverse execution, instruction stepping, source maps, or remote path mappings. Stripped bytecode has no meaningful source lines/named locals; pause can still stop it, but source breakpoints cannot be verified. Concurrent execution against the same global state remains unsupported.

## Why checkpoints

The design follows [ChibiRuby's C# debugger / DAP separation](https://github.com/hadashiA/ChibiRuby/tree/main/src/ChibiRuby.Debugger.Dap), adapting suspension to Lua-CSharp's asynchronous VM. References also include the [earlier Lua-CSharp debugger](https://github.com/akeit0/Lua-CSharp/tree/debugger) and [Okojo's checkpoint design](https://github.com/akeit0/okojo/blob/main/docs/architecture/debugging/OKOJO_EXECUTION_CHECKPOINT_NOTE.md).

An ordinary Lua hook can be replaced by the script and adds callback/frame overhead. A separate checkpoint preserves Lua 5.2 hook behavior and reaches existing coroutines. Awaiting the stop keeps the host responsive without converting a debugger stop into `coroutine.yield`.

Other embedders informed the hosting API as well. [MoonSharp's server](https://github.com/moonsharp-devs/moonsharp/blob/master/src/MoonSharp.VsCodeDebugger/MoonSharpVsCodeDebugServer.cs) attaches application-owned scripts and accepts a source finder; its [AsyncDebugger](https://github.com/moonsharp-devs/moonsharp/blob/master/src/MoonSharp.VsCodeDebugger/DebuggerLogic/AsyncDebugger.cs) nevertheless blocks the script thread while waiting for debugger actions. [Jint's debug callbacks](https://github.com/sebastienros/jint/blob/main/Jint/Runtime/Debugger/DebugHandler.cs) run inside engine execution and resume it by returning. [ClearScript's document pause flag](https://github.com/ClearFoundry/ClearScript/blob/master/ClearScript/DocumentFlags.cs) separates a selected script's initial debugger wait from general engine setup. Lua-CSharp keeps application ownership and source mapping explicit, and offers both blocking and asynchronous stops because its host calls can already await.

The VM keeps one dispatch loop. Ordinary execution uses `Prototype.Code`; a debugger installs its own cached execution copy for each prototype. That copy contains fixed trap instructions at selected source-line and control-flow points. Breakpoint, pause, and step changes update control flags; the protocol thread never patches instructions. Inactive checkpoints immediately dispatch the original instruction. Loop targets provide a checkpoint even for a single-line infinite loop. Bytecode without source metadata still has control-flow checkpoints.

Trap handling lives in the dispatch switch's existing fallback path. There is no second interpreter loop or generic specialization. Keeping traps outside the ordinary opcode cases also avoids expanding the normal dispatch table.

A forward numeric-loop setup jump needs no separate header trap when it stays on the same source line. The setup records that untrapped header once, preserving the first backward jump's line event. Loop-body targets and source-line boundaries remain checkpoints. Call/return checkpoints remain available for pause requests around host awaits; unreachable fallthrough points after returns and tail calls are omitted.

An internal resume trap executes the stopped instruction once and restores its checkpoint. Only the VM owner writes that marker. It survives breakpoint edits during a stop and does not replay Lua hooks. Private execution copies keep shared prototypes, other Lua states, and bytecode dumps unchanged. Volatile control flags are checked only at traps, so ordinary instruction fetching needs no debugger flag or volatile read. Execution-copy metadata is weakly cached and released with its prototype. Attached execution pays for checkpoints, including when no breakpoint is hit; detached execution uses the ordinary instructions.

Execution copies carry an opaque session identity and their source metadata, without a reference back to the debugger. A yielded coroutine therefore does not retain a detached debugger or its application callbacks. The weak tracking list removes dead entries when it fills, before growing, as well as during control changes. Compiling temporary chunks therefore does not grow that list indefinitely while controls stay unchanged. The VM checks ownership at frame restart; within the instruction loop it reads the active debugger only at enabled checkpoints. Ownership requires no weak-reference lookup on the execution path.

## Performance

Debugging is opt-in. Detached execution uses the original bytecode. Starting a server installs checkpoints before accepting clients, so an installed debugger has an execution cost even while no editor is connected. The cost depends on the workload's line boundaries, calls, loop targets and enabled breakpoints. A breakpoint in an uncalled function does not activate matching in other functions from the same source.

First execution builds private instruction copies and source metadata. Reusing compiled chunks avoids that setup on later calls. Pause, stepping and inspection allocate only as needed; execution ownership is tracked at host-call and coroutine-resume boundaries. Control updates use the weak tracking list and add no per-instruction work.

Run benchmarks in Release mode from this repository:

```sh
# Warmed execution, detached versus installed, across six interpreter workloads.
dotnet run -c Release --project sandbox/Benchmark -- --filter '*DebuggerBenchmark*'

# An unhit breakpoint in another function versus a cold branch of the active function.
dotnet run -c Release --project sandbox/Benchmark -- --filter '*DebuggerBreakpointBenchmark*'

# Pause/reset cost with 1 or 2,000 activated functions.
dotnet run -c Release --project sandbox/Benchmark -- --filter '*DebuggerControlBenchmark*'

# First-use metadata cost; compilation is excluded.
dotnet run -c Release --project sandbox/Benchmark -- --filter '*DebuggerFirstUseBenchmark*'
```

`DebuggerFirstUseBenchmark` varies function count, source preregistration and an unhit breakpoint. Keep first-use measurements separate from warmed execution and measure allocations as well as elapsed time. Small timing differences are sensitive to JIT profiles, code layout and system load; compare the same workloads and runtime settings when assessing a change.
