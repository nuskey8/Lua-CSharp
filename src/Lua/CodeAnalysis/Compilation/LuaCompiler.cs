using Lua.Runtime;

namespace Lua.CodeAnalysis.Compilation
{
    public static class LuaCompiler
    {
        /// <summary>
        ///  Lua bytecode signature. If the bytes start with this signature, they are considered as Lua bytecode.
        /// </summary>
        public static ReadOnlySpan<byte> LuaByteCodeSignature => Header.LuaSignature;

        /// <summary>
        ///  Converts a Lua bytecode to a Prototype object.
        /// </summary>
        /// <param name="span">binary bytecode</param>
        /// <param name="name">chunk name</param>
        /// <returns></returns>
        public static Prototype UnDump(ReadOnlySpan<byte> span, ReadOnlySpan<char> name) => Parser.UnDump(span, name);

        /// <summary>
        ///  Converts a Prototype object to a Lua bytecode.
        ///  </summary>
        ///  <param name="prototype">Prototype object</param>
        ///  <param name="useLittleEndian">true if the bytecode should be in little endian format, false if it should be in big endian format</param>
        /// <returns>binary bytecode</returns>
        public static byte[] Dump(Prototype prototype, bool useLittleEndian = true) => Parser.Dump(prototype, useLittleEndian);
    }
}