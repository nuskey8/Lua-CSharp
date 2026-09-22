using Lua.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lua.SourceGenerator.Tests;

public class LuaObjectGeneratorTests
{
    [Test]
    public void Test_AllowNullRejectsNonNullableValueTypes()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            using System.Diagnostics.CodeAnalysis;
            using Lua;

            [LuaObject]
            public partial class InvalidAllowNullMember
            {
                [LuaMember, AllowNull]
                public int Value;

                [LuaMember]
                public static void Method([AllowNull] int value) { }
            }
            """,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
        );
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(LuaValue).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(LuaObjectAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "InvalidAllowNullMember",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new LuaObjectGenerator().AsSourceGenerator()
        );
        driver = driver.RunGenerators(compilation);

        var diagnostics = driver.GetRunResult().Diagnostics;
        Assert.That(diagnostics, Has.Exactly(2).Matches<Diagnostic>(x => x.Id == "LUACS008"));
    }
}
