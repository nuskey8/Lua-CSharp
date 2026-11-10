using System.Runtime.InteropServices;

namespace Lua.Runtime;

[StructLayout(LayoutKind.Auto)]
public record struct CallStackFrame
{
    public required int Base;
    public required int ReturnBase;
    public required LuaFunction Function;
    public required int VariableArgumentCount;
    public int CallerInstructionIndex;
    internal CallStackFrameFlags Flags;
    public int Version;

    /// <summary>
    /// Prototype of the caller that was discarded by a tail call into a host function, and the
    /// index of its call instruction. Lua keeps that caller for C functions; this engine releases
    /// it, so the call site is recorded instead: a prototype does not keep the caller's values
    /// alive, unlike the frame itself.
    /// </summary>
    internal Prototype? DiscardedCallerPrototype;

    internal int DiscardedCallerPc;

    internal bool IsTailCall =>
        (Flags & CallStackFrameFlags.TailCall) == CallStackFrameFlags.TailCall;
}

[Flags]
public enum CallStackFrameFlags
{
    // None = 0,
    ReversedLe = 1,
    TailCall = 2,
    InHook = 4,
}
