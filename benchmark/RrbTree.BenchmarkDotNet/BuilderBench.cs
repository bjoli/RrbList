using BenchmarkDotNet.Attributes;
using Collections;

namespace Benchmarks;

[MemoryDiagnoser]
public class BuilderBench
{
    // You can add more sizes here (e.g., 100_000, 10_000_000) if needed
    [Params(1_000_000)]
    public int N;

    [Benchmark]
    public RrbList<int> BuildDenseList()
    {
        // Standard builder usage
        var builder = new RrbBuilder<int>();

        for (int i = 0; i < N; i++)
        {
            builder.Add(i);
        }

        return builder.ToImmutable();
    }
}