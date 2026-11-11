using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lua.Runtime;

namespace Lua.Debugging;

public sealed partial class LuaDebugger
{
    readonly Dictionary<string, SourceEntry> sources = new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal
    );
    readonly Dictionary<int, SourceEntry> sourcesById = new();
    readonly ConditionalWeakTable<Prototype, SourceEntry> registered = new();
    readonly Dictionary<string, List<RequestedBreakpoint>> breakpoints;

    /// <summary>Replaces all breakpoints for one source; unresolved lines are reported as pending.</summary>
    public LuaBreakpoint[] SetBreakpoints(string source, IReadOnlyList<int> lines)
    {
        var key = SourceKey(source);
        lock (gate)
        {
            sources.TryGetValue(key, out var entry);
            var requests = new List<RequestedBreakpoint>(lines.Count);
            foreach (var line in lines)
            {
                if (line <= 0)
                    throw new ArgumentOutOfRangeException(nameof(lines));
                var request = new RequestedBreakpoint(
                    line,
                    new LuaBreakpoint(
                        ++nextBreakpointId,
                        line,
                        false,
                        "Source has not been loaded."
                    )
                );
                Resolve(request, entry);
                requests.Add(request);
            }
            if (requests.Count == 0)
                breakpoints.Remove(key);
            else
                breakpoints[key] = requests;
            if (entry != null)
            {
                entry.Points.Clear();
                foreach (var request in requests)
                    if (request.Resolved.Verified)
                        entry.Points[request.Resolved.Line] = request.Resolved;
            }
            UpdateExecutionCodes(breakpointsChanged: true);
            return requests.Select(x => x.Resolved).ToArray();
        }
    }

    public void ClearBreakpoints()
    {
        lock (gate)
        {
            ClearBreakpointsCore();
            UpdateExecutionCodes(breakpointsChanged: true);
        }
    }

    void ClearBreakpointsCore()
    {
        breakpoints.Clear();
        foreach (var entry in sources.Values)
            entry.Points.Clear();
    }

    /// <summary>Registers executable lines and optional .NET source text before execution starts.</summary>
    public LuaDebugSource RegisterSource(LuaClosure closure, string? content = null)
    {
        lock (gate)
            return RegisterSourceCore(closure.Proto, content).Source;
    }

    SourceEntry RegisterSourceCore(Prototype rootPrototype, string? content = null)
    {
        var work = new Stack<Prototype>();
        var controlsChanged = false;
        work.Push(rootPrototype);
        while (work.Count != 0)
        {
            var prototype = work.Pop();
            controlsChanged |= RegisterPrototype(prototype);
            foreach (var child in prototype.ChildPrototypes)
                work.Push(child);
        }
        var entry = registered.GetValue(rootPrototype, _ => throw new InvalidOperationException());
        if (content != null)
            entry.Source = entry.Source with { Content = content };
        if (controlsChanged)
            UpdateExecutionCodes(breakpointsChanged: true);
        return entry;
    }

    public LuaDebugSource[] GetSources()
    {
        lock (gate)
            return sourcesById.Values.Select(x => x.Source).ToArray();
    }

    public LuaDebugSource GetSource(int id)
    {
        lock (gate)
            return sourcesById.TryGetValue(id, out var entry)
                ? entry.Source
                : throw new ArgumentException("Unknown source reference.", nameof(id));
    }

    public async ValueTask<string> ReadSourceAsync(
        int id,
        CancellationToken cancellationToken = default
    )
    {
        var source = GetSource(id);
        if (source.Content != null)
            return source.Content;
        if (options.SourceTextProvider != null)
        {
            var content = await options.SourceTextProvider(source, cancellationToken);
            if (content != null)
                return content;
        }
        if (source.Path == null)
            throw new InvalidOperationException("Source text was not registered by the host.");
        using var input = await root.Platform.FileSystem.Open(
            source.Path,
            IO.LuaFileOpenMode.Read,
            cancellationToken
        );
        return root.Platform.DecodeText(await input.ReadAllAsync(cancellationToken));
    }

    bool RegisterPrototype(Prototype prototype)
    {
        if (registered.TryGetValue(prototype, out _))
            return false;
        var name = root.Platform.DecodeText(prototype.ChunkName);
        var path = options.SourcePathResolver?.Invoke(name);
        if (path == null && name.StartsWith('@'))
            path = name[1..];
        var key = path != null ? SourceKey(path) : "=" + name.TrimStart('=');
        if (!sources.TryGetValue(key, out var entry))
        {
            path = path != null ? key : null;
            entry = new SourceEntry(
                new LuaDebugSource(
                    sourcesById.Count + 1,
                    path == null ? name.TrimStart('=') : Path.GetFileName(path),
                    path,
                    null
                )
            );
            sources.Add(key, entry);
            sourcesById.Add(entry.Source.Id, entry);
        }
        foreach (var line in prototype.LineInfo)
            if (line > 0)
                entry.Lines.Add(line);
        registered.Add(prototype, entry);
        var controlsChanged = false;
        if (breakpoints.TryGetValue(key, out var requests))
        {
            entry.Points.Clear();
            foreach (var request in requests)
            {
                var previous = request.Resolved;
                Resolve(request, entry);
                if (request.Resolved.Verified)
                    entry.Points[request.Resolved.Line] = request.Resolved;
                if (request.Resolved != previous)
                {
                    controlsChanged = true;
                    BreakpointChanged?.Invoke(request.Resolved);
                }
            }
        }
        return controlsChanged;
    }

    static void Resolve(RequestedBreakpoint request, SourceEntry? entry)
    {
        if (entry == null)
            return;
        var line = entry.Lines.GetViewBetween(request.Line, int.MaxValue).Min;
        request.Resolved =
            line == 0
                ? request.Resolved with
                {
                    Verified = false,
                    Message = "No executable line at or after this location.",
                }
                : request.Resolved with
                {
                    Line = line,
                    Verified = true,
                    Message = null,
                };
    }

    static string SourceKey(string source)
    {
        if (source.StartsWith('='))
            return source;
        return Path.GetFullPath(source.TrimStart('@'))
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    sealed class SourceEntry(LuaDebugSource source)
    {
        public LuaDebugSource Source = source;
        public readonly SortedSet<int> Lines = new();
        public readonly Dictionary<int, LuaBreakpoint> Points = new();
    }

    sealed class RequestedBreakpoint(int line, LuaBreakpoint resolved)
    {
        public readonly int Line = line;
        public LuaBreakpoint Resolved = resolved;
    }
}
