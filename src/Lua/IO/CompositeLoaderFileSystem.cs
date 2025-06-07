namespace Lua.IO;

public class CompositeLoaderFileSystem(ILuaFileLoader[] loaders, ILuaFileSystem? system = null) : ILuaFileSystem
{
    public static CompositeLoaderFileSystem Create(ILuaFileSystem system, params ILuaFileLoader[] loaders)
    {
        if (loaders == null || loaders.Length == 0)
        {
            throw new ArgumentException("Loaders cannot be null or empty", nameof(loaders));
        }

        return new(loaders, system);
    }

    public static CompositeLoaderFileSystem Create(params ILuaFileLoader[] loaders)
    {
        return new(loaders);
    }

    private (int index, string path)? cached;

    public bool IsReadable(string path)
    {
        for (int index = 0; index < loaders.Length; index++)
        {
            ILuaFileLoader? loader = loaders[index];
            if (loader.Exists(path))
            {
                cached = (index, path);
                return true;
            }
        }

        if (system != null)
        {
            cached = (loaders.Length, path);
            return system.IsReadable(path);
        }

        return false;
    }

    public async ValueTask<ILuaStream> Open(string path, LuaFileMode mode, CancellationToken cancellationToken)
    {
        if (cached != null)
        {
            var cachedValue = cached.Value;
            if (path == cachedValue.path)
            {
                if (cachedValue.index < loaders.Length)
                {
                    if (mode.CanWrite())
                        throw new NotSupportedException("Cannot write to a file opened with a loader.");
                    return ILuaStream.CreateFromFileContent(await loaders[cachedValue.index].LoadAsync(path, cancellationToken));
                }
            }
        }
        else
        {
            foreach (var loader in loaders)
            {
                if (loader.Exists(path))
                {
                    if (mode.CanWrite())
                        throw new NotSupportedException("Cannot write to a file opened with a loader.");
                    return ILuaStream.CreateFromFileContent(await loader.LoadAsync(path, cancellationToken));
                }
            }
        }


        return system != null ? await system.Open(path, mode, cancellationToken) : throw new NotSupportedException();
    }

    public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken)
    {
        return system?.Rename(oldName, newName, cancellationToken) ?? throw new NotSupportedException();
    }

    public ValueTask Remove(string path, CancellationToken cancellationToken)
    {
        return system?.Remove(path, cancellationToken) ?? throw new NotSupportedException();
    }

    public string DirectorySeparator => system?.DirectorySeparator ?? "/";

    public string GetTempFileName()
    {
        return system?.GetTempFileName() ?? throw new NotSupportedException();
    }

    public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken)
    {
        return system?.OpenTempFileStream(cancellationToken) ?? throw new NotSupportedException();
    }
}