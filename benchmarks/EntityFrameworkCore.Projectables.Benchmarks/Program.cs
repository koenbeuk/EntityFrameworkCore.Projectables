using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using EntityFrameworkCore.Projectables.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(PlainOverhead).Assembly).Run(args, DefaultConfig.Instance.WithOption(ConfigOptions.DisableOptimizationsValidator, true));
