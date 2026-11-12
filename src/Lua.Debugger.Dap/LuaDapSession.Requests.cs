using System.Text.Json.Nodes;
using Lua.Debugging;

namespace Lua.Debugger.Dap;

public sealed partial class LuaDapSession
{
    async Task DispatchAsync(JsonObject request, int expectedStop, CancellationToken token)
    {
        var command = Text(request, "command");
        var args = request["arguments"] as JsonObject ?? new JsonObject();
        if (!initialized && command != "initialize")
            throw new InvalidOperationException("Initialize the debug adapter first.");
        switch (command)
        {
            case "initialize":
                if (initialized)
                    throw new InvalidOperationException("The adapter is already initialized.");
                linesStartAtOne = Boolean(args, "linesStartAt1", true);
                columnsStartAtOne = Boolean(args, "columnsStartAt1", true);
                uriPaths = Text(args, "pathFormat") == "uri";
                initialized = true;
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["supportsConfigurationDoneRequest"] = true,
                            ["supportsEvaluateForHovers"] = true,
                            ["supportsSetVariable"] = true,
                            ["supportsLoadedSourcesRequest"] = true,
                            ["exceptionBreakpointFilters"] = new JsonArray(),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                await connection
                    .WriteAsync(Event("initialized", null), token)
                    .ConfigureAwait(false);
                return;
            case "attach":
                if (attached)
                    throw new InvalidOperationException("A client is already attached.");
                debugger.Attach();
                attached = true;
                stopOnEntry = Boolean(args, "stopOnEntry", false);
                break;
            case "launch":
                throw new NotSupportedException(
                    "This embedded adapter supports attach. Start the Lua host application separately."
                );
            case "configurationDone":
                if (!attached)
                    throw new InvalidOperationException("Attach before completing configuration.");
                await RespondAsync(request, new JsonObject(), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (referenceGate)
                {
                    configured = true;
                    if (debugger.CurrentStop is { } existingStop)
                        OnStopped(existingStop);
                    else if (stopOnEntry)
                    {
                        entryPending = true;
                        debugger.Pause();
                    }
                }
                Configured?.Invoke();
                return;
            case "setBreakpoints":
                var source =
                    args["source"]?.AsObject()
                    ?? throw new ArgumentException("A source is required.");
                var path = Text(source, "path");
                if (path.Length != 0)
                    path = FromClientPath(path);
                else
                {
                    var known = debugger.GetSource(Integer(source, "sourceReference"));
                    path = known.Path ?? "=" + known.Name;
                }
                var specs = args["breakpoints"] as JsonArray ?? new JsonArray();
                var lines = new List<int>();
                foreach (var spec in specs)
                {
                    var item = spec!.AsObject();
                    if (
                        item["condition"] != null
                        || item["hitCondition"] != null
                        || item["logMessage"] != null
                    )
                        throw new NotSupportedException(
                            "Only unconditional source breakpoints are supported."
                        );
                    lines.Add(Integer(item, "line") + (linesStartAtOne ? 0 : 1));
                }
                var points = debugger.SetBreakpoints(path, lines);
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["breakpoints"] = new JsonArray(
                                points.Select(ToBreakpoint).Cast<JsonNode?>().ToArray()
                            ),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "setExceptionBreakpoints":
                if (args["filters"] is JsonArray filters && filters.Count != 0)
                    throw new NotSupportedException("Exception breakpoints are not supported.");
                break;
            case "threads":
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["threads"] = new JsonArray(
                                new JsonObject { ["id"] = 1, ["name"] = "Lua" }
                            ),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "pause":
                CheckThread(args);
                await RespondAsync(request, new JsonObject(), token).ConfigureAwait(false);
                debugger.Pause();
                return;
            case "continue":
            case "next":
            case "stepIn":
            case "stepOut":
                CheckThread(args);
                var current = CurrentStop(expectedStop);
                var mode = command switch
                {
                    "next" => LuaDebugStep.Over,
                    "stepIn" => LuaDebugStep.Into,
                    "stepOut" => LuaDebugStep.Out,
                    _ => LuaDebugStep.Continue,
                };
                await RespondAsync(
                        request,
                        command == "continue"
                            ? new JsonObject { ["allThreadsContinued"] = true }
                            : new JsonObject(),
                        token
                    )
                    .ConfigureAwait(false);
                lock (referenceGate)
                {
                    if (stopId != current)
                        return;
                    stopId = 0;
                    references.Clear();
                }
                try
                {
                    debugger.TryContinue(current, mode);
                }
                catch (AggregateException exception)
                {
                    events.Writer.TryWrite(
                        Event(
                            "output",
                            new JsonObject
                            {
                                ["category"] = "stderr",
                                ["output"] = exception + Environment.NewLine,
                            }
                        )
                    );
                }
                return;
            case "stackTrace":
                CheckThread(args);
                var start = Integer(args, "startFrame");
                var levels = Integer(args, "levels");
                if (start < 0 || levels < 0)
                    throw new ArgumentOutOfRangeException(nameof(args));
                var stack = await debugger
                    .GetStackTraceAsync(
                        CurrentStop(expectedStop),
                        start,
                        levels == 0 ? int.MaxValue : levels,
                        token
                    )
                    .ConfigureAwait(false);
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["totalFrames"] = stack.TotalFrames,
                            ["stackFrames"] = new JsonArray(
                                stack
                                    .Frames.Select(frame =>
                                        (JsonNode?)
                                            new JsonObject
                                            {
                                                ["id"] = frame.Id,
                                                ["name"] = frame.Name,
                                                ["source"] =
                                                    frame.Source == null
                                                        ? null
                                                        : ToSource(frame.Source),
                                                ["line"] =
                                                    Math.Max(frame.Line, 1)
                                                    - (linesStartAtOne ? 0 : 1),
                                                ["column"] = columnsStartAtOne ? 1 : 0,
                                            }
                                    )
                                    .ToArray()
                            ),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "scopes":
                var frameId = Integer(args, "frameId");
                var frameCount = debugger.GetFrameCount(CurrentStop(expectedStop));
                if (frameId < 1 || frameId > frameCount)
                    throw new ArgumentException("Unknown frame.");
                var scopes = new JsonArray();
                foreach (
                    var scope in new[]
                    {
                        LuaDebugScope.Locals,
                        LuaDebugScope.UpValues,
                        LuaDebugScope.Globals,
                    }
                )
                    scopes.Add(
                        new JsonObject
                        {
                            ["name"] = scope.ToString(),
                            ["variablesReference"] = AddReference(
                                new VariableReference(frameId, scope),
                                expectedStop
                            ),
                            ["expensive"] = scope == LuaDebugScope.Globals,
                        }
                    );
                await RespondAsync(request, new JsonObject { ["scopes"] = scopes }, token)
                    .ConfigureAwait(false);
                return;
            case "variables":
                var reference = GetReference(Integer(args, "variablesReference"));
                var offset = Integer(args, "start");
                var count = Integer(args, "count");
                if (offset < 0 || count < 0)
                    throw new ArgumentOutOfRangeException(nameof(args));
                LuaDebugVariable[] values;
                if (reference.Scope is { } variableScope)
                    values = await debugger
                        .GetVariablesAsync(
                            CurrentStop(expectedStop),
                            reference.FrameId,
                            variableScope,
                            offset,
                            count == 0 ? int.MaxValue : count,
                            token
                        )
                        .ConfigureAwait(false);
                else
                    values = await debugger
                        .GetChildrenAsync(
                            CurrentStop(expectedStop),
                            reference.Value,
                            offset,
                            count == 0 ? int.MaxValue : count,
                            token
                        )
                        .ConfigureAwait(false);
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["variables"] = new JsonArray(
                                values
                                    .Select(value =>
                                        (JsonNode?)ToVariable(
                                            value,
                                            reference.Scope == null,
                                            expectedStop
                                        )
                                    )
                                    .ToArray()
                            ),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "evaluate":
                var evaluationStop = CurrentStop(expectedStop);
                var evaluationFrame = Integer(args, "frameId");
                if (evaluationFrame == 0)
                    evaluationFrame = debugger.GetFrameCount(evaluationStop);
                var evaluated = await debugger
                    .EvaluateAsync(evaluationStop, evaluationFrame, Text(args, "expression"), token)
                    .ConfigureAwait(false);
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["result"] = evaluated.DisplayValue,
                            ["type"] = evaluated.Value.TypeToString(),
                            ["variablesReference"] = ValueReference(evaluated.Value, expectedStop),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "setVariable":
                var target = GetReference(Integer(args, "variablesReference"));
                if (target.Scope is not { } targetScope)
                    throw new NotSupportedException("This variable is read-only.");
                var changed = await debugger
                    .SetVariableAsync(
                        CurrentStop(expectedStop),
                        target.FrameId,
                        targetScope,
                        Text(args, "name"),
                        Text(args, "value"),
                        token
                    )
                    .ConfigureAwait(false);
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["value"] = changed.DisplayValue,
                            ["type"] = changed.Value.TypeToString(),
                            ["variablesReference"] = ValueReference(changed.Value, expectedStop),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "loadedSources":
                await RespondAsync(
                        request,
                        new JsonObject
                        {
                            ["sources"] = new JsonArray(
                                debugger
                                    .GetSources()
                                    .Select(source => (JsonNode?)ToSource(source))
                                    .ToArray()
                            ),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            case "source":
                var content = await debugger
                    .ReadSourceAsync(Integer(args, "sourceReference"), token)
                    .ConfigureAwait(false);
                await RespondAsync(
                        request,
                        new JsonObject { ["content"] = content, ["mimeType"] = "text/x-lua" },
                        token
                    )
                    .ConfigureAwait(false);
                return;
            default:
                throw new NotSupportedException($"Unsupported DAP request: {command}");
        }
        await RespondAsync(request, new JsonObject(), token).ConfigureAwait(false);
    }

    JsonObject ToBreakpoint(LuaBreakpoint point) =>
        new()
        {
            ["id"] = point.Id,
            ["verified"] = point.Verified,
            ["line"] = point.Line - (linesStartAtOne ? 0 : 1),
            ["message"] = point.Message,
        };

    JsonObject ToSource(LuaDebugSource source) =>
        new()
        {
            ["name"] = source.Name,
            ["path"] =
                source.Path == null ? null
                : uriPaths ? new Uri(source.Path).AbsoluteUri
                : source.Path,
            ["sourceReference"] = source.Id,
        };

    JsonObject ToVariable(LuaDebugVariable value, bool readOnly, int expectedStop)
    {
        var result = new JsonObject
        {
            ["name"] = value.Name,
            ["value"] = value.DisplayValue,
            ["type"] = value.Value.TypeToString(),
            ["variablesReference"] = ValueReference(value.Value, expectedStop),
        };
        if (readOnly || value.IsReadOnly || value.Name.StartsWith("...", StringComparison.Ordinal))
            result["presentationHint"] = new JsonObject
            {
                ["attributes"] = new JsonArray("readOnly"),
            };
        return result;
    }

    int ValueReference(LuaValue value, int expectedStop) =>
        value.Type is LuaValueType.Table or LuaValueType.UserData
            ? AddReference(new VariableReference(value), expectedStop)
            : 0;

    static void CheckThread(JsonObject args)
    {
        if (Integer(args, "threadId", 1) != 1)
            throw new ArgumentException("Unknown Lua thread.");
    }

    Task RespondAsync(JsonObject request, JsonNode? body, CancellationToken token) =>
        connection.WriteAsync(
            new JsonObject
            {
                ["type"] = "response",
                ["request_seq"] = Integer(request, "seq"),
                ["command"] = Text(request, "command"),
                ["success"] = true,
                ["body"] = body,
            },
            token
        );

    static JsonObject Event(string name, JsonNode? body) =>
        new()
        {
            ["type"] = "event",
            ["event"] = name,
            ["body"] = body,
        };

    static string Text(JsonObject value, string name) => value[name]?.GetValue<string>() ?? "";

    static int Integer(JsonObject value, string name, int fallback = 0) =>
        value[name]?.GetValue<int>() ?? fallback;

    static bool Boolean(JsonObject value, string name, bool fallback) =>
        value[name]?.GetValue<bool>() ?? fallback;

    string FromClientPath(string path) => uriPaths ? new Uri(path).LocalPath : path;
}
