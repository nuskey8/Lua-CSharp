using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

sealed class Comparer : IEqualityComparer<(GeneratorAttributeSyntaxContext, Compilation)>
{
    public static readonly Comparer Instance = new();

    public bool Equals((GeneratorAttributeSyntaxContext, Compilation) x, (GeneratorAttributeSyntaxContext, Compilation) y)
    {
        return x.Item1.TargetNode.Equals(y.Item1.TargetNode);
    }

    public int GetHashCode((GeneratorAttributeSyntaxContext, Compilation) obj)
    {
        return obj.Item1.TargetNode.GetHashCode();
    }
}