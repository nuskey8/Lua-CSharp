using System.Runtime.CompilerServices;
using Lua.Runtime;

namespace Lua.Debugging;

public sealed partial class LuaDebugger
{
    readonly ConditionalWeakTable<Prototype, ExecutionCode> executionCodes = new();
    readonly List<WeakReference<ExecutionCode>> codeReferences = new();

    LuaDebugCode ILuaDebugSession.GetExecutionCode(LuaClosure closure)
    {
        if (executionCodes.TryGetValue(closure.Proto, out var existing))
            return existing;
        lock (gate)
        {
            if (!registered.TryGetValue(closure.Proto, out var source))
            {
                RegisterSource(closure);
                source = registered.GetValue(
                    closure.Proto,
                    _ => throw new InvalidOperationException()
                );
            }
            var code = new ExecutionCode(identity, closure.Proto, source);
            executionCodes.Add(closure.Proto, code);
            if (codeReferences.Count == codeReferences.Capacity)
                codeReferences.RemoveAll(static reference => !reference.TryGetTarget(out _));
            codeReferences.Add(new WeakReference<ExecutionCode>(code));
            UpdateControls(code, IsAttached);
            return code;
        }
    }

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
        var enabled = pauseRequested || step != null;
        if (!enabled && code.Source.Points.Count != 0)
            foreach (var line in code.Prototype.LineInfo)
                if (code.Source.Points.ContainsKey(line))
                {
                    enabled = true;
                    break;
                }
        code.Enabled = attached && enabled;
    }

    sealed class ExecutionCode(object identity, Prototype prototype, SourceEntry source)
        : LuaDebugCode(identity, prototype)
    {
        public readonly SourceEntry Source = source;
    }
}
