namespace Lua.Loaders;

public static class CompositeModuleLoader
{
    class CompositeLoader_2(ILuaModuleLoader loader0, ILuaModuleLoader loader1) : ILuaModuleLoader
    {
        public bool Exists(string moduleName)
        {
            return loader0.Exists(moduleName) ||
                   loader1.Exists(moduleName);
        }

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (loader0.Exists(moduleName))
            {
                return loader0.LoadAsync(moduleName, cancellationToken);
            }

            if (loader1.Exists(moduleName))
            {
                return loader1.LoadAsync(moduleName, cancellationToken);
            }

            throw new LuaModuleNotFoundException(moduleName);
        }
    }

    class CompositeLoader_3(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2) : ILuaModuleLoader
    {
        public bool Exists(string moduleName)
        {
            return loader0.Exists(moduleName) ||
                   loader1.Exists(moduleName) ||
                   loader2.Exists(moduleName);
        }

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (loader0.Exists(moduleName))
            {
                return loader0.LoadAsync(moduleName, cancellationToken);
            }

            if (loader1.Exists(moduleName))
            {
                return loader1.LoadAsync(moduleName, cancellationToken);
            }

            if (loader2.Exists(moduleName))
            {
                return loader2.LoadAsync(moduleName, cancellationToken);
            }

            throw new LuaModuleNotFoundException(moduleName);
        }
    }

    class CompositeLoader_4(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2, ILuaModuleLoader loader3) : ILuaModuleLoader
    {
        public bool Exists(string moduleName)
        {
            return loader0.Exists(moduleName) ||
                   loader1.Exists(moduleName) ||
                   loader2.Exists(moduleName) ||
                   loader3.Exists(moduleName);
        }

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (loader0.Exists(moduleName))
            {
                return loader0.LoadAsync(moduleName, cancellationToken);
            }

            if (loader1.Exists(moduleName))
            {
                return loader1.LoadAsync(moduleName, cancellationToken);
            }

            if (loader2.Exists(moduleName))
            {
                return loader2.LoadAsync(moduleName, cancellationToken);
            }

            if (loader3.Exists(moduleName))
            {
                return loader3.LoadAsync(moduleName, cancellationToken);
            }

            throw new LuaModuleNotFoundException(moduleName);
        }
    }

    class CompositeLoader_5(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2, ILuaModuleLoader loader3, ILuaModuleLoader loader4) : ILuaModuleLoader
    {
        public bool Exists(string moduleName)
        {
            return loader0.Exists(moduleName) ||
                   loader1.Exists(moduleName) ||
                   loader2.Exists(moduleName) ||
                   loader3.Exists(moduleName) ||
                   loader4.Exists(moduleName);
        }

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (loader0.Exists(moduleName))
            {
                return loader0.LoadAsync(moduleName, cancellationToken);
            }

            if (loader1.Exists(moduleName))
            {
                return loader1.LoadAsync(moduleName, cancellationToken);
            }

            if (loader2.Exists(moduleName))
            {
                return loader2.LoadAsync(moduleName, cancellationToken);
            }

            if (loader3.Exists(moduleName))
            {
                return loader3.LoadAsync(moduleName, cancellationToken);
            }

            if (loader4.Exists(moduleName))
            {
                return loader4.LoadAsync(moduleName, cancellationToken);
            }

            throw new LuaModuleNotFoundException(moduleName);
        }
    }

    class CompositeLoader_6(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2, ILuaModuleLoader loader3, ILuaModuleLoader loader4, ILuaModuleLoader loader5) : ILuaModuleLoader
    {
        public bool Exists(string moduleName)
        {
            return loader0.Exists(moduleName) ||
                   loader1.Exists(moduleName) ||
                   loader2.Exists(moduleName) ||
                   loader3.Exists(moduleName) ||
                   loader4.Exists(moduleName) ||
                   loader5.Exists(moduleName);
        }

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (loader0.Exists(moduleName))
            {
                return loader0.LoadAsync(moduleName, cancellationToken);
            }

            if (loader1.Exists(moduleName))
            {
                return loader1.LoadAsync(moduleName, cancellationToken);
            }

            if (loader2.Exists(moduleName))
            {
                return loader2.LoadAsync(moduleName, cancellationToken);
            }

            if (loader3.Exists(moduleName))
            {
                return loader3.LoadAsync(moduleName, cancellationToken);
            }

            if (loader4.Exists(moduleName))
            {
                return loader4.LoadAsync(moduleName, cancellationToken);
            }

            if (loader5.Exists(moduleName))
            {
                return loader5.LoadAsync(moduleName, cancellationToken);
            }

            throw new LuaModuleNotFoundException(moduleName);
        }
    }


    class CompositeLoader(ILuaModuleLoader[] loaders) : ILuaModuleLoader
    {
        public bool Exists(string moduleName)
        {
            foreach (var loader in loaders)
            {
                if (loader.Exists(moduleName)) return true;
            }

            return false;
        }

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            foreach (var loader in loaders)
            {
                if (loader.Exists(moduleName))
                {
                    return loader.LoadAsync(moduleName, cancellationToken);
                }
            }

            throw new LuaModuleNotFoundException(moduleName);
        }
    }

    public static ILuaModuleLoader Create(ILuaModuleLoader loader0, ILuaModuleLoader loader1)
    {
        return new CompositeLoader_2(loader0, loader1);
    }

    public static ILuaModuleLoader Create(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2)
    {
        return new CompositeLoader_3(loader0, loader1, loader2);
    }

    public static ILuaModuleLoader Create(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2, ILuaModuleLoader loader3)
    {
        return new CompositeLoader_4(loader0, loader1, loader2, loader3);
    }

    public static ILuaModuleLoader Create(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2, ILuaModuleLoader loader3, ILuaModuleLoader loader4)
    {
        return new CompositeLoader_5(loader0, loader1, loader2, loader3, loader4);
    }

    public static ILuaModuleLoader Create(ILuaModuleLoader loader0, ILuaModuleLoader loader1, ILuaModuleLoader loader2, ILuaModuleLoader loader3, ILuaModuleLoader loader4, ILuaModuleLoader loader5)
    {
        return new CompositeLoader_6(loader0, loader1, loader2, loader3, loader4, loader5);
    }

    public static ILuaModuleLoader Create(params ILuaModuleLoader[] loaders)
    {
        return new CompositeLoader(loaders);
    }
}