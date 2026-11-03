using BenchmarkDotNet.Running;

BenchmarkSwitcher
    .FromTypes([typeof(RuntimeExecutionComparison), typeof(RuntimeCompilationComparison)])
    .Run(args);
