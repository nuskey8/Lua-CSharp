namespace Lua;

/// <summary>Outcome of advancing a raw <c>next</c> iteration.</summary>
internal enum LuaNextStatus
{
    /// <summary>Another key/value pair was found.</summary>
    Found,

    /// <summary>The iteration reached the end of the table.</summary>
    End,

    /// <summary>
    /// The supplied key is not part of the table, so the iteration cannot continue.
    /// Lua raises <c>invalid key to 'next'</c> for this case; a key cleared during traversal keeps a
    /// dead entry and does not reach this state.
    /// </summary>
    InvalidKey,
}
