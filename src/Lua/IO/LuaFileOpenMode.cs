namespace Lua.IO
{
    public enum LuaFileOpenMode
    {
        /// <summary>
        /// r
        /// </summary>
        Read,

        /// <summary>
        /// w
        /// </summary>
        Write,

        /// <summary>
        /// a
        /// </summary>
        Append,

        /// <summary>
        /// r+
        /// </summary>
        ReadWriteOpen,

        /// <summary>
        /// w+
        /// </summary>
        ReadWriteCreate,

        /// <summary>
        /// a+
        /// </summary>
        ReadAppend,
    }
}