using Lua.IO;

namespace Lua.Tests.Helpers
{
    abstract class NotImplementedExceptionFileSystemBase : ILuaFileSystem
    {
        public virtual bool IsReadable(string path)
        {
            throw new NotImplementedException();
        }

        public virtual ValueTask<LuaFileContent> ReadFileContentAsync(string fileName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public virtual ILuaStream Open(string path, LuaFileMode mode)
        {
            throw new NotImplementedException();
        }

        public virtual void Rename(string oldName, string newName)
        {
            throw new NotImplementedException();
        }

        public virtual void Remove(string path)
        {
            throw new NotImplementedException();
        }

        public virtual string DirectorySeparator => Path.DirectorySeparatorChar.ToString();

        public virtual string GetTempFileName()
        {
            throw new NotImplementedException();
        }

        public ILuaStream OpenTempFileStream()
        {
            throw new NotImplementedException();
        }
    }
}