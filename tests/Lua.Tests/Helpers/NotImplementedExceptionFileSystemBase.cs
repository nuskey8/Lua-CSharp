using Lua.IO;

namespace Lua.Tests.Helpers
{
    abstract class NotImplementedExceptionFileSystemBase : ILuaFileSystem
    {
        public virtual bool IsReadable(string path)
        {
            throw new NotImplementedException();
        }

        public virtual ValueTask<ILuaStream> Open(string path, LuaFileMode mode, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public virtual ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public virtual ValueTask Remove(string path, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public virtual string DirectorySeparator => Path.DirectorySeparatorChar.ToString();

        public virtual string GetTempFileName()
        {
            throw new NotImplementedException();
        }

        public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}