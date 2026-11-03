using BenchmarkDotNet.Running;

var config = BenchmarkConfig.Create(ref args);
BenchmarkSwitcher.FromAssembly(typeof(InterpreterComparison).Assembly).Run(args, config);
