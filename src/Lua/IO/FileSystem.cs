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


        ILuaIOStream Open(string path, LuaFileOpenMode luaMode, LuaFileContentType contentType)
        {
            var (mode, access) = GetFileMode(luaMode);
            Stream stream;

            if (luaMode == LuaFileOpenMode.ReadAppend)
            {
                stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            else
            {
                stream = File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
            }

            ILuaIOStream wrapper = contentType == LuaFileContentType.Binary
                ? new BinaryLuaIOStream(luaMode, stream)
                : new TextLuaIOStream(luaMode, stream);

            if (luaMode == LuaFileOpenMode.ReadAppend)
            {
                wrapper.Seek(0, SeekOrigin.End);
            }

            return wrapper;
        }

        public ILuaIOStream Open(string path, LuaFileMode mode)
        {
            if (mode is LuaFileMode.ReadBinaryOrText)
            {
                return new LuaChunkStream(File.OpenRead(path));
            }

            var openMode = mode.GetOpenMode();
            var contentType = mode.GetContentType();
            return Open(path, openMode, contentType);
        }

        public ILuaIOStream Open(string path, string mode)
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

        public ILuaIOStream OpenTempFileStream()
        {
            return new TextLuaIOStream(LuaFileOpenMode.ReadAppend, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite));
        }
    }
}