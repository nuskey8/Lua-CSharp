namespace Lua.IO
{
    public sealed class FileSystem(string? baseDirectory = null) : ILuaFileSystem
    {
        public string BaseDirectory => baseDirectory ?? Directory.GetCurrentDirectory();

        public static (FileMode, FileAccess access) GetFileMode(LuaFileOpenMode luaFileOpenMode)
        {
            return luaFileOpenMode switch
            {
                LuaFileOpenMode.Read => (FileMode.Open, FileAccess.Read),
                LuaFileOpenMode.Write => (FileMode.Create, FileAccess.Write),
                LuaFileOpenMode.Append => (FileMode.Append, FileAccess.Write),
                LuaFileOpenMode.ReadUpdate => (FileMode.Open, FileAccess.ReadWrite),
                LuaFileOpenMode.WriteUpdate => (FileMode.Truncate, FileAccess.ReadWrite),
                LuaFileOpenMode.AppendUpdate => (FileMode.Append, FileAccess.ReadWrite),
                _ => throw new ArgumentOutOfRangeException(nameof(luaFileOpenMode), luaFileOpenMode, null)
            };
        }

        public string GetFullPath(string path)
        {
            if (baseDirectory == null || Path.IsPathFullyQualified(path))
            {
                return path;
            }

            return Path.Combine(baseDirectory, path);
        }

        public bool IsReadable(string path)
        {
            return File.Exists(GetFullPath(path));
        }


        public ValueTask<ILuaStream> Open(string path, LuaFileOpenMode openMode, CancellationToken cancellationToken)
        {
            var (mode, access) = GetFileMode(openMode);
            Stream stream;
            path = GetFullPath(path);
            if (openMode == LuaFileOpenMode.AppendUpdate)
            {
                stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            else
            {
                stream = File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
            }

            ILuaStream wrapper =
                new LuaStream(openMode, stream);

            if (openMode == LuaFileOpenMode.AppendUpdate)
            {
                wrapper.Seek(SeekOrigin.End, 0);
            }

            return new(wrapper);
        }

        public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken)
        {
            oldName = GetFullPath(oldName);
            newName = GetFullPath(newName);
            if (oldName == newName) return default;
            if (File.Exists(newName)) File.Delete(newName);
            File.Move(oldName, newName);
            File.Delete(oldName);
            return default;
        }

        public ValueTask Remove(string path, CancellationToken cancellationToken)
        {
            path = GetFullPath(path);
            File.Delete(path);
            return default;
        }

        static readonly string directorySeparator = Path.DirectorySeparatorChar.ToString();
        public string DirectorySeparator => directorySeparator;

        public string GetTempFileName()
        {
            return Path.GetTempFileName();
        }

        public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken)
        {
            return new(new LuaStream(LuaFileOpenMode.WriteUpdate, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite)));
        }
    }
}