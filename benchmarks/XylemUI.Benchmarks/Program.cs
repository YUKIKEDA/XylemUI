using BenchmarkDotNet.Running;
using XylemUI.Benchmarks;

// Runs the benchmarks the arguments pick: `dotnet run -c Release --project benchmarks/XylemUI.Benchmarks -- --filter *`.
_ = BenchmarkSwitcher.FromAssembly(typeof(TextStyleCompareBenchmarks).Assembly).Run(args);
