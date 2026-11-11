using Lua.Runtime;

namespace Lua.Debugging;

public sealed partial class LuaDebugger
{
    public Task<LuaDebugStackFrame[]> GetStackTraceAsync(
        int stopId,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            (context, _) =>
                new ValueTask<LuaDebugStackFrame[]>(
                    ReadStackTrace(context, 0, int.MaxValue).Frames
                ),
            cancellationToken
        );

    /// <summary>
    /// Reads one page of the suspended stack. The cost is proportional to the page, not to the
    /// stack depth, which keeps protocol paging usable for deeply recursive scripts.
    /// </summary>
    internal Task<LuaDebugStackTrace> GetStackTraceAsync(
        int stopId,
        int start,
        int count,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            (context, _) =>
                new ValueTask<LuaDebugStackTrace>(ReadStackTrace(context, start, count)),
            cancellationToken
        );

    LuaDebugStackTrace ReadStackTrace(PauseContext context, int start, int count)
    {
        var frames = context.State.GetCallStackFrames();
        var total = frames.Length;
        if (start < 0 || start >= total || count <= 0)
            return new LuaDebugStackTrace(Array.Empty<LuaDebugStackFrame>(), total);
        var end = (int)Math.Min(total, (long)start + count);
        var result = new LuaDebugStackFrame[end - start];
        for (var position = start; position < end; position++)
        {
            // Frames are reported top first, while the call stack stores the oldest frame first.
            var i = total - 1 - position;
            var frame = frames[i];
            var pc = position == 0 ? context.Pc : frames[i + 1].CallerInstructionIndex;
            LuaDebugSource? source = null;
            var line = 0;
            var name = frame.Function.Name;
            if (frame.Function is LuaClosure closure)
            {
                lock (gate)
                {
                    RegisterPrototype(closure.Proto);
                    source = registered
                        .GetValue(closure.Proto, _ => throw new InvalidOperationException())
                        .Source;
                }
                if ((uint)pc < (uint)closure.Proto.LineInfo.Length)
                    line = closure.Proto.LineInfo[pc];
                name =
                    closure.Proto.LineDefined == 0
                        ? "main chunk"
                        : $"function at {source.Name}:{closure.Proto.LineDefined}";
            }
            result[position - start] = new LuaDebugStackFrame(i + 1, name, source, line);
        }
        return new LuaDebugStackTrace(result, total);
    }

    public Task<LuaDebugVariable[]> GetVariablesAsync(
        int stopId,
        int frameId,
        LuaDebugScope scope,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            (context, _) => new ValueTask<LuaDebugVariable[]>(ReadScope(context, frameId, scope)),
            cancellationToken
        );

    public Task<LuaDebugVariable[]> GetVariablesAsync(
        int stopId,
        int frameId,
        LuaDebugScope scope,
        int start,
        int count,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            (context, _) =>
                new ValueTask<LuaDebugVariable[]>(
                    scope == LuaDebugScope.Globals
                        ? ReadChildren(GetEnvironment(context, frameId), start, count)
                        : ReadScope(context, frameId, scope).Skip(start).Take(count).ToArray()
                ),
            cancellationToken
        );

    /// <summary>Reads raw table entries or userdata uservalues without invoking metamethods.</summary>
    public Task<LuaDebugVariable[]> GetChildrenAsync(
        int stopId,
        LuaValue value,
        int start = 0,
        int count = 100,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            (_, _) => new ValueTask<LuaDebugVariable[]>(ReadChildren(value, start, count)),
            cancellationToken
        );

    /// <summary>Evaluates a Lua expression against copied locals/upvalues and the selected frame's environment.</summary>
    public Task<LuaDebugVariable> EvaluateAsync(
        int stopId,
        int frameId,
        string expression,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            async (context, token) =>
                Variable(expression, await EvaluateCoreAsync(context, frameId, expression, token)),
            cancellationToken
        );

    /// <summary>Evaluates a new value and writes a named local, upvalue, or global while stopped.</summary>
    public Task<LuaDebugVariable> SetVariableAsync(
        int stopId,
        int frameId,
        LuaDebugScope scope,
        string name,
        string expression,
        CancellationToken cancellationToken = default
    ) =>
        InspectAsync(
            stopId,
            async (context, token) =>
            {
                var value = await EvaluateCoreAsync(context, frameId, expression, token);
                WriteScope(context, frameId, scope, name, value);
                return Variable(name, value);
            },
            cancellationToken
        );

    public string FormatValue(LuaValue value) =>
        value.Type switch
        {
            LuaValueType.String => "\""
                + root.Platform.DecodeText(value.Read<string>())
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t")
                    .Replace("\0", "\\0")
                + "\"",
            LuaValueType.Function => "function " + value.Read<LuaFunction>().Name,
            LuaValueType.Table => "table",
            LuaValueType.Thread => "thread",
            LuaValueType.UserData => "userdata",
            LuaValueType.LightUserData => "light userdata",
            _ => value.ToLuaString(root.Platform.NumberFormat),
        };

    LuaDebugVariable Variable(string name, LuaValue value) =>
        new(root.Platform.DecodeText(name), value, FormatValue(value));

    string TableKeyName(LuaValue key) =>
        key.Type == LuaValueType.String ? key.Read<string>() : "[" + FormatValue(key) + "]";

    static int ParseUpValueIndex(string name) =>
        name.StartsWith("upvalue[", StringComparison.Ordinal)
        && name.EndsWith(']')
        && int.TryParse(name.AsSpan(8, name.Length - 9), out var index)
        && index > 0
            ? index - 1
            : -1;

    static (CallStackFrame Frame, int Pc) GetFrame(PauseContext context, int id)
    {
        var frames = context.State.GetCallStackFrames();
        var index = id - 1;
        if ((uint)index >= (uint)frames.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        return (
            frames[index],
            index == frames.Length - 1 ? context.Pc : frames[index + 1].CallerInstructionIndex
        );
    }

    LuaDebugVariable[] ReadScope(PauseContext context, int frameId, LuaDebugScope scope)
    {
        var (frame, pc) = GetFrame(context, frameId);
        if (scope == LuaDebugScope.Globals)
            return ReadChildren(GetEnvironment(context, frameId), 0, int.MaxValue);
        var result = new List<LuaDebugVariable>();
        if (frame.Function is LuaClosure closure)
        {
            if (scope == LuaDebugScope.Locals)
            {
                var slot = 0;
                foreach (var local in closure.Proto.LocalVariables)
                {
                    if (local.StartPc <= pc && pc < local.EndPc)
                        result.Add(
                            Variable(
                                local.Name,
                                context.State.GetLocalReference(frame.Base + slot++)
                            )
                        );
                }
                for (var i = 0; i < frame.VariableArgumentCount; i++)
                    result.Add(
                        Variable(
                            $"...[{i + 1}]",
                            context.State.Stack[frame.Base - frame.VariableArgumentCount + i]
                        )
                    );
            }
            else
                for (var i = 0; i < closure.UpValues.Length; i++)
                {
                    var name = closure.Proto.UpValues[i].Name;
                    result.Add(
                        Variable(
                            string.IsNullOrEmpty(name) ? $"upvalue[{i + 1}]" : name,
                            closure.UpValues[i].GetValue()
                        )
                    );
                }
        }
        else if (scope == LuaDebugScope.UpValues && frame.Function is CSharpClosure cs)
            for (var i = 0; i < cs.UpValues.Length; i++)
                result.Add(Variable($"upvalue[{i + 1}]", cs.UpValues[i]));
        return result.ToArray();
    }

    LuaValue GetEnvironment(PauseContext context, int frameId)
    {
        var (frame, pc) = GetFrame(context, frameId);
        if (frame.Function is LuaClosure closure)
        {
            var slot = 0;
            var environmentSlot = -1;
            foreach (var local in closure.Proto.LocalVariables)
                if (local.StartPc <= pc && pc < local.EndPc)
                {
                    if (local.Name == "_ENV")
                        environmentSlot = slot;
                    slot++;
                }
            if (environmentSlot >= 0)
                return context.State.GetLocalReference(frame.Base + environmentSlot);
            for (var i = 0; i < closure.UpValues.Length; i++)
                if (closure.Proto.UpValues[i].Name == "_ENV")
                    return closure.UpValues[i].GetValue();
        }
        return context.State.Environment;
    }

    LuaDebugVariable[] ReadChildren(LuaValue value, int start, int count)
    {
        if (start < 0 || count < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        var result = new List<LuaDebugVariable>();
        if (value.TryRead<LuaTable>(out var table))
        {
            var index = 0;
            foreach (var pair in table)
            {
                if (index++ < start)
                    continue;
                if (result.Count == count)
                    break;
                result.Add(
                    Variable(TableKeyName(pair.Key), pair.Value) with
                    {
                        IsReadOnly = pair.Key.Type != LuaValueType.String,
                    }
                );
            }
        }
        else if (value.TryRead<ILuaUserData>(out var data))
        {
            var values = data.UserValues;
            for (var i = start; i < values.Length && result.Count < count; i++)
                result.Add(Variable($"uservalue[{i + 1}]", values[i]));
        }
        return result.ToArray();
    }

    async ValueTask<LuaValue> EvaluateCoreAsync(
        PauseContext context,
        int frameId,
        string expression,
        CancellationToken cancellationToken
    )
    {
        var bindings = new Dictionary<string, LuaValue>();
        foreach (var item in ReadScope(context, frameId, LuaDebugScope.UpValues))
            bindings[root.Platform.EncodeText(item.Name)] = item.Value;
        foreach (var item in ReadScope(context, frameId, LuaDebugScope.Locals))
            bindings[root.Platform.EncodeText(item.Name)] = item.Value;
        var globals = GetEnvironment(context, frameId);
        var environment = new LuaTable
        {
            Metatable = new LuaTable
            {
                ["__index"] = new LuaFunction(
                    async (call, token) =>
                    {
                        var key = call.GetArgument(1);
                        var value =
                            key.TryRead<string>(out var name)
                            && bindings.TryGetValue(name, out var local)
                                ? local
                                : await call.State.GetTableAsync(globals, key, token);
                        return call.Return(value);
                    }
                ),
            },
        };
        var function = context.State.Load(
            "return (" + expression + "\n)",
            "=debugger",
            environment
        );
        var previousPc = context.State.LastPc;
        var previousPrototype = context.State.LastCallerPrototype;
        var previousNativeTailCaller = context.State.LastNativeTailCaller;
        evaluating = true;
        context.State.NonYieldableCallCount++;
        try
        {
            var results = await context.State.CallAsync(function, [], cancellationToken);
            return results.Length == 0 ? LuaValue.Nil : results[0];
        }
        finally
        {
            context.State.NonYieldableCallCount--;
            context.State.LastPc = previousPc;
            context.State.LastCallerPrototype = previousPrototype;
            context.State.LastNativeTailCaller = previousNativeTailCaller;
            evaluating = false;
        }
    }

    void WriteScope(
        PauseContext context,
        int frameId,
        LuaDebugScope scope,
        string name,
        LuaValue value
    )
    {
        name = root.Platform.EncodeText(name);
        var (frame, pc) = GetFrame(context, frameId);
        if (
            scope == LuaDebugScope.Globals
            && GetEnvironment(context, frameId).TryRead<LuaTable>(out var globals)
        )
        {
            if (!globals.ContainsKey(name))
                foreach (var pair in globals)
                    if (pair.Key.Type != LuaValueType.String && TableKeyName(pair.Key) == name)
                        throw new ArgumentException(
                            "This global entry is read-only.",
                            nameof(name)
                        );
            globals[name] = value;
            return;
        }
        if (scope == LuaDebugScope.Locals && frame.Function is LuaClosure closure)
        {
            var slot = 0;
            var found = -1;
            foreach (var local in closure.Proto.LocalVariables)
                if (local.StartPc <= pc && pc < local.EndPc)
                {
                    if (local.Name == name)
                        found = slot;
                    slot++;
                }
            if (found >= 0)
            {
                context.State.GetLocalReference(frame.Base + found) = value;
                return;
            }
        }
        else if (scope == LuaDebugScope.UpValues)
        {
            var index = ParseUpValueIndex(name);
            if (frame.Function is LuaClosure lua)
            {
                if (index < 0)
                    for (var i = 0; i < lua.UpValues.Length; i++)
                        if (lua.Proto.UpValues[i].Name == name)
                        {
                            index = i;
                            break;
                        }
                if ((uint)index < (uint)lua.UpValues.Length)
                {
                    lua.UpValues[index].SetValue(value);
                    return;
                }
            }
            else if (frame.Function is CSharpClosure cs && (uint)index < (uint)cs.UpValues.Length)
            {
                cs.UpValues[index] = value;
                return;
            }
        }
        throw new ArgumentException("The variable is not writable in this scope.", nameof(name));
    }
}
