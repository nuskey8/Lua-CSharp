using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lua.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public partial class LuaObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Lua.LuaObjectAttribute",
                static (node, cancellation) =>
                {
                    return node is ClassDeclarationSyntax
                        or RecordDeclarationSyntax;
                },
                static (context, cancellation) => { return context; })
            .Combine(context.CompilationProvider)
            .WithComparer(Comparer.Instance);

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(provider.Collect()),
            (sourceProductionContext, t) =>
            {
                var (compilation, list) = t;
                var references = SymbolReferences.Create(compilation);
                if (references == null) return;

                var builder = new CodeBuilder();

                var metaDict = new Dictionary<INamedTypeSymbol, TypeMetadata>(SymbolEqualityComparer.Default);

                foreach (var (x, _) in list)
                {
                    var symbol = (INamedTypeSymbol)x.TargetSymbol;
                    var typeMeta = new TypeMetadata((TypeDeclarationSyntax)x.TargetNode, symbol, references);
                    metaDict.Add(symbol, typeMeta);
                }

                var tempCollections = new TempCollections();
                foreach (var pair in metaDict)
                {
                    var typeMeta = pair.Value;
                    if (TryEmit(typeMeta, builder, references, compilation, in sourceProductionContext, metaDict, tempCollections))
                    {
                        var fullType = typeMeta.FullTypeName
                            .Replace("global::", "")
                            .Replace("<", "_")
                            .Replace(">", "_");

                        sourceProductionContext.AddSource($"{fullType}.LuaObject.g.cs", builder.ToString());
                    }

                    tempCollections.Clear();
                    builder.Clear();
                }
            });
    }
}