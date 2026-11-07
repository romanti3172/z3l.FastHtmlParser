using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

// dotnet run -c Release                     → choose benchmarks interactively
// dotnet run -c Release -- --filter '*'     → run everything
BenchmarkSwitcher
    .FromAssembly(typeof(z3l.FastHtmlParser.Benchmarks.Corpus).Assembly)
    .Run(args, DefaultConfig.Instance.AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams));
