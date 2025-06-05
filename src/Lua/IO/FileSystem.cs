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
            if (!File.Exists(path)) return false;
            try
            {
                File.Open(path, FileMode.Open, FileAccess.Read).Dispose();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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

        public ILuaStream Open(string path, LuaFileMode mode)
        {
            if (mode is LuaFileMode.ReadBinaryOrText)
            {
                return new LuaChunkStream(File.OpenRead(path));
            }

            var openMode = mode.GetOpenMode();
            var contentType = mode.GetContentType();
            return Open(path, openMode, contentType);
        }

        public ILuaStream Open(string path, string mode)
        {
            var flags = LuaFileModeExtensions.ParseModeString(mode);
            return Open(path, flags);
        }

        public void Rename(string oldName, string newName)
        {
            if (oldName == newName) return;
            if (File.Exists(newName)) File.Delete(newName);
            File.Move(oldName, newName);
            File.Delete(oldName);
        }

        public void Remove(string path)
        {
            File.Delete(path);
        }

        static readonly string directorySeparator = Path.DirectorySeparatorChar.ToString();
        public string DirectorySeparator => directorySeparator;

        public string GetTempFileName()
        {
            return Path.GetTempFileName();
        }

        public ILuaStream OpenTempFileStream()
        {
            return new TextLuaStream(LuaFileMode.ReadUpdateText, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite));
        }
    }
}