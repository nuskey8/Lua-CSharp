namespace Lua.IO
{
    public sealed class FileSystem : ILuaFileSystem
    {
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

        public bool IsReadable(string path)
        {
            return File.Exists(path);
        }


        public ValueTask<ILuaStream> Open(string path, LuaFileOpenMode openMode, CancellationToken cancellationToken)
        {
            var (mode, access) = GetFileMode(openMode);
            Stream stream;

            if (openMode == LuaFileOpenMode.AppendUpdate)
            {
                stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            else
            {
                stream = File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
            }

            ILuaStream wrapper =
                new TextLuaStream(openMode, stream);

            if (openMode == LuaFileOpenMode.AppendUpdate)
            {
                wrapper.Seek(0, SeekOrigin.End);
            }

            return new(wrapper);
        }

        public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken)
        {
            if (oldName == newName) return default;
            if (File.Exists(newName)) File.Delete(newName);
            File.Move(oldName, newName);
            File.Delete(oldName);
            return default;
        }

        public ValueTask Remove(string path, CancellationToken cancellationToken)
        {
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
            return new(new TextLuaStream(LuaFileOpenMode.WriteUpdate, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite)));
        }
    }
}