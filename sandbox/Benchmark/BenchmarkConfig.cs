using BenchmarkDotNet.Configs;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.NetCoreApp;
using CommandLine;

static class BenchmarkConfig
{
    public static IConfig Create(ref string[] args)
    {
        var defaults = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default);
        var (success, parsed, options) = ConfigParser.Parse(args, NullLogger.Instance, defaults);
        if (
            !success
            || !options!.Runtimes.Any(runtime => CoreRuntime.Parse(runtime) == CoreRuntime.Core11_0)
        )
            return defaults;

        var effective = ManualConfig.Union(defaults, parsed!).CreateImmutableConfig();
        var net11 = new Net11RuntimeToolchain(
            new NetCoreAppSettings
            {
                CliPath = options.CliPath,
                PackagesPath = options.RestorePath,
                UseFreshPackages = options.UseFreshPackages,
            }
        );
        var jobs = effective
            .GetJobs()
            .Select(job =>
                job.Infrastructure.Runtime == CoreRuntime.Core11_0 ? job.WithToolchain(net11) : job
            )
            .ToArray();
        var first = jobs[0];
        // Copy settings without job metadata, so the template cannot overwrite baselines.
        var template = new Job([
            first.Environment,
            first.Run,
            first.Infrastructure,
            first.Accuracy,
        ]);
        template.Infrastructure.Runtime = null;

        // Runtime jobs are already configured; let BenchmarkDotNet handle the remaining CLI normally.
        options.Runtimes = [];
        args = Parser
            .Default.FormatCommandLineArgs(options, settings => settings.SkipDefault = true)
            .ToArray();
        return new RuntimeJobs(effective, [.. jobs, template.AsDefault()]);
    }

    sealed class RuntimeJobs : ManualConfig, IConfig
    {
        readonly Job[] jobs;

        public RuntimeJobs(IConfig source, Job[] jobs)
        {
            Add(source);
            this.jobs = jobs;
        }

        IEnumerable<Job> IConfig.GetJobs() => jobs;
    }
}
