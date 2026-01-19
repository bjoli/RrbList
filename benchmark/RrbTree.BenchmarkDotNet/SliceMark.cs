using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Collections; // Assuming your namespace is Collections

[MemoryDiagnoser]
[ShortRunJob] // Remove this for final production-grade numbers
[EventPipeProfiler(EventPipeProfile.CpuSampling)]
public class SliceMark
{
    // 10k (Medium), 100k (Large), 1M (Very Large)
    [Params(10_000, 100_000, 1_000_000)]
    public int N;

    private RrbList<int> _denseList;
    private RrbList<int> _relaxedList;

    [GlobalSetup]
    public void Setup()
    {
        // 1. Construct a Dense List
        // Since I don't have your Builder code, I assume repeated adding.
        // (In a real app, you'd use a transient builder for speed).
        var list = RrbList<int>.Empty;
        for (int i = 0; i < N; i++)
        {
            list = list.Add(i);
        }
        _denseList = list;

        // 2. Construct a Relaxed List
        // We create a relaxed list by slicing the dense list in a way 
        // that misaligns the 32-block boundaries (e.g., removing the first 1 item).
        // This forces the offsets to be non-uniform.
        _relaxedList = _denseList.Slice(1, N - 1);
    }

    // --- CASE 1: The "Center Slice" (Your specific 25% to 75% benchmark) ---
    // This forces LCA calculation + Left Cut + Right Cut + Spine Reconstruction.
    
    [Benchmark(Description = "Dense: Slice Middle (25% -> 75%)")]
    public RrbList<int> Dense_Slice_Middle()
    {
        int start = N / 4;
        int count = N / 2;
        return _denseList.Slice(start, count);
    }

    [Benchmark(Description = "Relaxed: Slice Middle (25% -> 75%)")]
    public RrbList<int> Relaxed_Slice_Middle()
    {
        int start = N / 4;
        int count = N / 2;
        return _relaxedList.Slice(start, count);
    }

    // --- CASE 2: The "Take" (Slice from 0) ---
    // This tests your Density Optimization. 
    // Slicing a Dense list from the right should preserve density and avoid SizeTable allocations.

    [Benchmark(Description = "Dense: Take (0 -> 75%)")]
    public RrbList<int> Dense_Take()
    {
        // Should be faster than Slice Middle because Left recursion is skipped
        // and SizeTables are not allocated.
        return _denseList.Slice(0, (int)(N * 0.75));
    }

    [Benchmark(Description = "Relaxed: Take (0 -> 75%)")]
    public RrbList<int> Relaxed_Take()
    {
        // Must allocate SizeTables because input is already relaxed.
        return _relaxedList.Slice(0, (int)(N * 0.75));
    }

    // --- CASE 3: The "Skip" (Slice from Left) ---
    // Tests Left recursion heavy path.

    [Benchmark(Description = "Dense: Skip (25% -> End)")]
    public RrbList<int> Dense_Skip()
    {
        int start = (int)(N * 0.25);
        return _denseList.Slice(start, N - start);
    }
}
