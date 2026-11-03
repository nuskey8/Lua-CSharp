using System.Xml.Linq;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using BenchmarkDotNet.Toolchains.NetCoreApp;

// Build net10.0 with the existing SDK; execute it on the installed .NET 11 runtime.
sealed class Net11RuntimeToolchain(NetCoreAppSettings settings)
    : Toolchain(
        "Runtime11",
        CoreRuntime.Core11_0,
        new RuntimeBuilder(settings),
        new DotNetCliExecutor(settings.CliPath)
    )
{
    sealed class RuntimeBuilder(NetCoreAppSettings settings)
        : CsProjBuilder(settings with { TargetFrameworkMoniker = "net10.0" })
    {
        protected override void AddLateProperties(
            XElement project,
            BuildPartition partition,
            ArtifactsPaths paths,
            FileInfo projectFile
        )
        {
            base.AddLateProperties(project, partition, paths, projectFile);
            project.Add(
                new XElement(
                    "PropertyGroup",
                    new XElement("UseAppHost", false),
                    new XElement("RuntimeFrameworkVersion", "11.0.0-preview.1"),
                    new XElement("RollForward", "LatestPatch")
                )
            );
        }
    }
}
