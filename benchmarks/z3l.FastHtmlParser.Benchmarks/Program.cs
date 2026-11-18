using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

// dotnet run -c Release                     → choose benchmarks interactively
// dotnet run -c Release -- --filter '*'     → run everything
// dotnet run -c Release -- --runtime-variants --filter '*FastHtmlParser*'
//                                           → JIT and NativeAOT, each with workstation and server GC
var config = DefaultConfig.Instance.AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams);

if (args.Contains("--runtime-variants"))
{
    args = args.Where(a => a != "--runtime-variants").ToArray();
    config = config
        .AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0).WithGcServer(false).WithId("JIT"))
        .AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0).WithGcServer(true).WithId("JIT-ServerGC"))
        .AddJob(Job.Default.WithRuntime(NativeAotRuntime.Net10_0).WithGcServer(false).WithId("AOT"))
        .AddJob(Job.Default.WithRuntime(NativeAotRuntime.Net10_0).WithGcServer(true).WithId("AOT-ServerGC"));
}

BenchmarkSwitcher
    .FromAssembly(typeof(z3l.FastHtmlParser.Benchmarks.Corpus).Assembly)
    .Run(args, config);
