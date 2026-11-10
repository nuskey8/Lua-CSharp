using System.Runtime.CompilerServices;
using Lua.Runtime;

namespace Lua.Debugging;

public sealed partial class LuaDebugger
{
    const int WeakReferenceCleanupInterval = 64;
    readonly ConditionalWeakTable<Prototype, ExecutionCode> executionCodes = new();
    readonly List<WeakReference<ExecutionCode>> codeReferences = new();
    int additionsSinceCleanup;

    LuaDebugCode ILuaDebugSession.GetExecutionCode(LuaClosure closure)
    {
        if (executionCodes.TryGetValue(closure.Proto, out var existing))
            return existing;
        lock (gate)
        {
            // GetValue instead of TryGetValue/Add: the execution thread and the protocol thread
            // can both reach a prototype first, and Add would throw on the loser.
            return executionCodes.GetValue(
                closure.Proto,
                prototype =>
                {
                    if (!registered.TryGetValue(prototype, out var source))
                    {
                        RegisterSource(closure);
                        source = registered.GetValue(
                            prototype,
                            _ => throw new InvalidOperationException()
                        );
                    }
                    var code = new ExecutionCode(identity, prototype, source)
                    {
                        HasBreakpointLine = HasBreakpointLine(prototype, source),
                    };
                    Track(code);
                    UpdateControls(code, IsAttached);
                    return code;
                }
            );
        }
    }

    void Track(ExecutionCode code)
    {
        if (++additionsSinceCleanup >= WeakReferenceCleanupInterval)
        {
            additionsSinceCleanup = 0;
            codeReferences.RemoveAll(static reference => !reference.TryGetTarget(out _));
        }
        codeReferences.Add(new WeakReference<ExecutionCode>(code));
    }

    /// <summary>Applies the current controls to every execution copy. Cheap: no prototype is scanned.</summary>
    void UpdateExecutionCodes()
    {
        var attached = IsAttached;
        for (var i = codeReferences.Count - 1; i >= 0; i--)
        {
            if (codeReferences[i].TryGetTarget(out var code))
                UpdateControls(code, attached);
            else
                codeReferences.RemoveAt(i);
        }
    }

    void UpdateControls(ExecutionCode code, bool attached)
    {
        code.SetEnabled(attached && (pauseRequested || step != null || code.HasBreakpointLine));
    }

    /// <summary>
    /// Recomputes which execution copies can hit a breakpoint. Only breakpoint or source changes
    /// need this; stops and steps reuse the cached answer instead of rescanning every prototype.
    /// </summary>
    void RefreshBreakpointLines()
    {
        for (var i = codeReferences.Count - 1; i >= 0; i--)
        {
            if (!codeReferences[i].TryGetTarget(out var code))
            {
                codeReferences.RemoveAt(i);
                continue;
            }
            code.HasBreakpointLine = HasBreakpointLine(code.Prototype, code.Source);
        }
    }

    static bool HasBreakpointLine(Prototype prototype, SourceEntry source)
    {
        if (source.Points.Count == 0)
            return false;
        var lines = prototype.LineInfo;
        for (var i = 0; i < lines.Length; i++)
            if (source.Points.ContainsKey(lines[i]))
                return true;
        return false;
    }

    sealed class ExecutionCode(object identity, Prototype prototype, SourceEntry source)
        : LuaDebugCode(identity, prototype)
    {
        public readonly SourceEntry Source = source;
        public bool HasBreakpointLine;
    }
}
