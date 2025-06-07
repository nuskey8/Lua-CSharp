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
                LuaFileOpenMode.ReadWriteOpen => (FileMode.Open, FileAccess.ReadWrite),
                LuaFileOpenMode.ReadWriteCreate => (FileMode.Truncate, FileAccess.ReadWrite),
                LuaFileOpenMode.ReadAppend => (FileMode.Append, FileAccess.ReadWrite),
                _ => throw new ArgumentOutOfRangeException(nameof(luaFileOpenMode), luaFileOpenMode, null)
            };
        }

        public bool IsReadable(string path)
        {
            return File.Exists(path);
        }


        ILuaStream Open(string path, LuaFileOpenMode openMode, LuaFileContentType contentType)
        {
            var (mode, access) = GetFileMode(openMode);
            Stream stream;

            if (openMode == LuaFileOpenMode.ReadAppend)
            {
                stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            else
            {
                stream = File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
            }

            var fileMode = LuaFileModeExtensions.GetMode(openMode, contentType);
            ILuaStream wrapper = contentType == LuaFileContentType.Binary
                ? new BinaryLuaStream(fileMode, stream)
                : new TextLuaStream(fileMode, stream);

            if (openMode == LuaFileOpenMode.ReadAppend)
            {
                wrapper.Seek(0, SeekOrigin.End);
            }

            return wrapper;
        }

        public ValueTask<ILuaStream> Open(string path, LuaFileMode mode, CancellationToken cancellationToken)
        {
            if (mode is LuaFileMode.Load)
            {
                return new ( new LuaChunkStream(File.OpenRead(path)));
            }

            var openMode = mode.GetOpenMode();
            var contentType = mode.GetContentType();
            return new(Open(path, openMode, contentType));
        }

        public ValueTask Rename(string oldName, string newName,CancellationToken cancellationToken)
        {
            if (oldName == newName) return default;
            if (File.Exists(newName)) File.Delete(newName);
            File.Move(oldName, newName);
            File.Delete(oldName);
            return default;
        }

        public ValueTask Remove(string path,CancellationToken cancellationToken)
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
            return new( new TextLuaStream(LuaFileMode.ReadUpdateText, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite)));
        }
    }
}