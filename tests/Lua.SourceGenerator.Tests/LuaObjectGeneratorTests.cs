using System.Collections.Immutable;
using Lua.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lua.SourceGenerator.Tests;

public class LuaObjectGeneratorTests
{
    [TestCase("class DocumentedObject", false)]
    [TestCase("class DocumentedObject", true)]
    [TestCase("class DocumentedObject<T>", false)]
    [TestCase("class DocumentedObject<T>", true)]
    [TestCase("record DocumentedObject", false)]
    [TestCase("record DocumentedObject", true)]
    public void GeneratedMembers_DoNotRequireXmlDocumentation(
        string declaration,
        bool warningsAsErrors
    )
    {
        var typeParameterDocumentation = declaration.Contains("<T>")
            ? "/// <typeparam name=\"T\">The type argument.</typeparam>"
            : "";
        var result = CompileGeneratedSource(
            $$"""
            using Lua;

            /// <summary>A documented Lua object.</summary>
            {{typeParameterDocumentation}}
            [LuaObject]
            public partial {{declaration}}
            {
                /// <summary>Gets or sets the value.</summary>
                [LuaMember]
                public int Value { get; set; }
            }
            """,
            warningsAsErrors
        );

        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [TestCase("Obsolete")]
    [TestCase("Obsolete(\"Use the replacement member.\")")]
    public void GeneratedWrappers_CanReferenceUserSelectedObsoleteMembers(string attribute)
    {
        var result = CompileGeneratedSource(
            $$"""
            using System;
            using Lua;

            [LuaObject]
            internal partial class LegacyObject
            {
                [LuaMember, {{attribute}}]
                public int Field;

                [LuaMember, {{attribute}}]
                public int Property { get; set; }

                [LuaMember, {{attribute}}]
                public void Method() { }

                [LuaMetamethod(LuaObjectMetamethod.Add), {{attribute}}]
                public static int Add(LegacyObject left, LegacyObject right) => 1;
            }
            """
        );

        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GeneratedSuppressions_DoNotHideDiagnosticsInUserCode(bool warningsAsErrors)
    {
        var result = CompileGeneratedSource(
            """
            using System;
            using Lua;

            /// <summary>A Lua object with diagnostics in user-written code.</summary>
            [LuaObject]
            public partial class UserObject
            {
                [LuaMember]
                public int Undocumented { get; set; }

                /// <summary>A legacy member without a message.</summary>
                [LuaMember, Obsolete]
                public void LegacyWithoutMessage() { }

                /// <summary>A legacy member with a message.</summary>
                [LuaMember, Obsolete("Use the replacement member.")]
                public void LegacyWithMessage() { }

                /// <summary>Calls the legacy members from user-written code.</summary>
                public void CallLegacyMembers()
                {
                    LegacyWithoutMessage();
                    LegacyWithMessage();
                }
            }
            """,
            warningsAsErrors
        );

        Assert.That(
            result.Diagnostics.Select(x => x.Id),
            Is.EquivalentTo(new[] { "CS1591", "CS0612", "CS0618" })
        );
        Assert.That(
            result.Diagnostics.All(x => x.Location.SourceTree == result.UserSource),
            Is.True
        );
        Assert.That(
            result.Diagnostics.All(x =>
                x.Severity
                == (warningsAsErrors ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning)
            ),
            Is.True
        );
        Assert.That(result.Success, Is.EqualTo(!warningsAsErrors));
    }

    [Test]
    public void GeneratedSuppressions_DoNotHideObsoleteErrors()
    {
        var result = CompileGeneratedSource(
            """
            using System;
            using Lua;

            [LuaObject]
            internal partial class UnsupportedObject
            {
                [LuaMember, Obsolete("This API must not be used.", true)]
                public void Method() { }
            }
            """
        );

        Assert.That(result.Success, Is.False);
        Assert.That(
            result.Diagnostics,
            Has.Some.Matches<Diagnostic>(x =>
                x.Id == "CS0619" && x.Severity == DiagnosticSeverity.Error
            )
        );
    }

    static (
        bool Success,
        ImmutableArray<Diagnostic> Diagnostics,
        SyntaxTree UserSource
    ) CompileGeneratedSource(string source, bool warningsAsErrors = true)
    {
        var parseOptions = CSharpParseOptions
            .Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithDocumentationMode(DocumentationMode.Diagnose);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, "UserObject.cs");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(LuaValue).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(LuaObjectAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "GeneratedWarningConsumer",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithGeneralDiagnosticOption(
                warningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default
            )
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new LuaObjectGenerator().AsSourceGenerator()],
            parseOptions: parseOptions
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var diagnostics
        );
        Assert.That(driver.GetRunResult().GeneratedTrees, Is.Not.Empty);
        using var assembly = new MemoryStream();
        using var documentation = new MemoryStream();
        var result = output.Emit(assembly, xmlDocumentationStream: documentation);
        return (result.Success, diagnostics.AddRange(result.Diagnostics), syntaxTree);
    }

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
