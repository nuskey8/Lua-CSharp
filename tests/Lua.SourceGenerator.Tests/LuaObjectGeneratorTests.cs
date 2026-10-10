using System.Collections.Immutable;
using Lua.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lua.SourceGenerator.Tests;

public class LuaObjectGeneratorTests
{
    [TestCase("LuaValue[]", "System.Array.Empty<LuaValue>()", "Lua.LuaValue[]")]
    [TestCase("LuaValue[,]", "new LuaValue[0, 0]", "Lua.LuaValue[*,*]")]
    [TestCase("int[]", "System.Array.Empty<int>()", "int[]")]
    [TestCase("string[]?", "null", "string[]?")]
    [TestCase(
        "Task<LuaValue[]>",
        "Task.FromResult(System.Array.Empty<LuaValue>())",
        "Lua.LuaValue[]"
    )]
    [TestCase(
        "ValueTask<LuaValue[]>",
        "new ValueTask<LuaValue[]>(System.Array.Empty<LuaValue>())",
        "Lua.LuaValue[]"
    )]
    [TestCase("int*", "null", "int*")]
    [TestCase("delegate*<int>", "null", "delegate*<int>")]
    public void UnsupportedReturnType_DoesNotStopOtherLuaObjects(
        string returnType,
        string returnExpression,
        string diagnosticType
    )
    {
        var usingTasks = returnType.Contains("Task") ? "using System.Threading.Tasks;" : "";
        var result = CompileGeneratedSource(
            $$"""
            #nullable enable
            using Lua;
            {{usingTasks}}
            [LuaObject]
            internal unsafe partial class UnsupportedObject
            {
                [LuaMember] public {{returnType}} Read() => {{returnExpression}};
            }
            [LuaObject]
            internal partial class HealthyObject
            {
                [LuaMember] public int Value => 1;
            }
            internal static class Consumer
            {
                public static LuaValue Convert() => new HealthyObject();
            }
            """
        );

        AssertUnsupportedMemberDiagnostic(
            result.Diagnostics,
            result.UserSource,
            "LUACS005",
            "Read",
            diagnosticType
        );
        Assert.That(
            result.GeneratedTrees.Select(x => Path.GetFileName(x.FilePath)),
            Is.EquivalentTo(new[] { "HealthyObject.LuaObject.g.cs" })
        );
    }

    [TestCase("LuaValue[] values", "Lua.LuaValue[]")]
    [TestCase("ref LuaValue[] values", "Lua.LuaValue[]")]
    [TestCase("out LuaValue[] values", "Lua.LuaValue[]")]
    public void UnsupportedParameterType_HasALocatedDiagnostic(
        string parameter,
        string diagnosticType
    )
    {
        var result = CompileGeneratedSource(
            $$"""
            using Lua;
            [LuaObject]
            internal partial class UnsupportedObject
            {
                [LuaMember] public void Read({{parameter}}) => throw new System.NotSupportedException();
            }
            [LuaObject]
            internal partial class HealthyObject
            {
                [LuaMember] public int Value => 1;
            }
            internal static class Consumer
            {
                public static LuaValue Convert() => new HealthyObject();
            }
            """
        );

        AssertUnsupportedMemberDiagnostic(
            result.Diagnostics,
            result.UserSource,
            "LUACS006",
            "values",
            diagnosticType
        );
    }

    static void AssertUnsupportedMemberDiagnostic(
        ImmutableArray<Diagnostic> diagnostics,
        SyntaxTree userSource,
        string diagnosticId,
        string memberName,
        string typeName
    )
    {
        Assert.That(diagnostics, Has.Length.EqualTo(1), string.Join("\n", diagnostics));
        var diagnostic = diagnostics[0];
        Assert.That(diagnostic.Id, Is.EqualTo(diagnosticId));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.GetMessage(), Does.Contain($"'{typeName}'"));
        Assert.That(diagnostic.Location.SourceTree, Is.SameAs(userSource));
        Assert.That(
            userSource.GetText().ToString(diagnostic.Location.SourceSpan),
            Is.EqualTo(memberName)
        );
    }

    [TestCase("public int Read(ref int value, out int extra) { extra = ++value; return value; }")]
    [TestCase("public static void Read(out int extra, ref int value) { extra = ++value; }")]
    [TestCase("public Task<int> Read() => Task.FromResult(1);")]
    [TestCase("public ValueTask<int> Read() => new ValueTask<int>(1);")]
    [TestCase("public Task Read() => Task.CompletedTask;")]
    [TestCase("public ValueTask Read() => default;")]
    public void SupportedMethods_StillGenerateWithoutDiagnostics(string method)
    {
        var usingTasks = method.Contains("Task") ? "using System.Threading.Tasks;" : "";
        var result = CompileGeneratedSource(
            $$"""
            using Lua;
            {{usingTasks}}
            [LuaObject]
            internal partial class HealthyObject
            {
                [LuaMember] {{method}}
            }
            internal static class Consumer
            {
                public static LuaValue Convert() => new HealthyObject();
            }
            """
        );

        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [TestCase(
        "[LuaMetamethod(LuaObjectMetamethod.Call)] public static BaseObject New() => new();",
        "[LuaMetamethod(LuaObjectMetamethod.Call)] public new static DerivedObject New() => new();"
    )]
    [TestCase(
        "[LuaMember] public virtual int Value { get; set; }",
        "[LuaMember] public override int Value { get; set; }"
    )]
    [TestCase("[LuaMember] public int Value;", "[LuaMember] public new int Value;")]
    [TestCase(
        "[LuaMember] public virtual int Read() => 1;",
        "[LuaMember] public override int Read() => 2;"
    )]
    [TestCase(
        "[LuaMember(\"read\")] public int BaseRead() => 1;",
        "[LuaMember(\"read\")] public int DerivedRead() => 2;"
    )]
    [TestCase(
        "[LuaMember(\"value\")] public int BaseValue { get; set; }",
        "[LuaMember(\"value\")] public int DerivedValue;"
    )]
    [TestCase(
        "[LuaMember(\"value\")] public int BaseValue { get; set; }",
        "[LuaMember(\"value\")] public int DerivedRead() => 2;"
    )]
    [TestCase(
        "[LuaMember(\"value\")] public int BaseRead() => 1;",
        "[LuaMember(\"value\")] public int DerivedValue { get; set; }"
    )]
    [TestCase(
        "[LuaMetamethod(LuaObjectMetamethod.Len)] public int BaseLength() => 1;",
        "[LuaMetamethod(LuaObjectMetamethod.Len)] public int DerivedLength() => 2;"
    )]
    [TestCase("[LuaMember] public virtual int Read() => 1;", "public override int Read() => 2;")]
    public void DerivedBindings_ReplaceInheritedBindings(string baseMember, string derivedMember)
    {
        var result = CompileGeneratedSource(
            $$"""
            using Lua;
            [LuaObject]
            internal partial class BaseObject { {{baseMember}} }
            [LuaObject]
            internal partial class DerivedObject : BaseObject { {{derivedMember}} }
            [LuaObject]
            internal partial class LeafObject : DerivedObject { }
            """
        );

        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void DuplicateMetamethods_InTheSameTypeRemainErrors()
    {
        var result = CompileGeneratedSource(
            """
            using Lua;
            [LuaObject]
            internal partial class BaseObject
            {
                [LuaMetamethod(LuaObjectMetamethod.Len)] public int Length() => 1;
            }
            [LuaObject]
            internal partial class DerivedObject : BaseObject
            {
                [LuaMetamethod(LuaObjectMetamethod.Len)] public int FirstLength() => 2;
                [LuaMetamethod(LuaObjectMetamethod.Len)] public int SecondLength() => 3;
            }
            """
        );

        Assert.That(
            result.Diagnostics,
            Has.Exactly(1).Matches<Diagnostic>(x => x.Id == "LUACS007")
        );
    }

    [Test]
    public void DuplicateLuaNames_InTheSameTypeRemainErrors()
    {
        var result = CompileGeneratedSource(
            """
            using Lua;
            [LuaObject]
            internal partial class DuplicateObject
            {
                [LuaMember("read")] public int FirstRead() => 1;
                [LuaMember("read")] public int SecondRead() => 2;
            }
            """
        );

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<Diagnostic>(x => x.Id == "CS0102"));
    }

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
        SyntaxTree UserSource,
        ImmutableArray<SyntaxTree> GeneratedTrees
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
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true
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
        return (
            result.Success,
            diagnostics.AddRange(result.Diagnostics),
            syntaxTree,
            driver.GetRunResult().GeneratedTrees
        );
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
