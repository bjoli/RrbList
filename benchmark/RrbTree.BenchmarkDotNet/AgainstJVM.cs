using BenchmarkDotNet.Attributes;

// Replace with your actual library namespace

[MemoryDiagnoser]
[RankColumn]
public class AgainstJvm
{
    private global::Collections.RrbList<int> _otherVector;
    private int _randomIndex;
    private int _splitIndex;

    private global::Collections.RrbList<int> _vector;

    [Params(1_000_000)] public int Size;

    // GlobalSetup runs once before all benchmarks to prepare the data
    [GlobalSetup]
    public void Setup()
    {
        // 0. Setup: Creating base vectors for other operations
        _vector = new global::Collections.RrbList<int>(Enumerable.Range(0, Size));
        _otherVector = new global::Collections.RrbList<int>(Enumerable.Range(0, Size));

        _splitIndex = Size / 2;
        _randomIndex = Size / 2;
    }

    // 1. Indexing (Random Access)
    [Benchmark]
    public int Indexing()
    {
        return _vector[_randomIndex];
    }

    // 2. Building (Construction via Builder)
    [Benchmark]
    public object Building()
    {
        var b = new global::Collections.RrbBuilder<int>();
        for (var i = 0; i < Size; i++) b.Add(i);
        // Assuming ToImmutable() or similar exists to finalize the builder
        return b.ToImmutable();
    }

    // 3. Slicing
    [Benchmark]
    public object Slicing()
    {
        return _vector.Slice(1000, Size - 1000);
    }

    // 4. Splitting
    [Benchmark]
    public (global::Collections.RrbList<int>, global::Collections.RrbList<int>) Splitting()
    {
        // Deconstructs the tuple returned by your split method
        var (left, right) = _vector.Split(_splitIndex);
        return (left, right);
    }

    // 5. Merging (Concatenation)
    [Benchmark]
    public object Merging()
    {
        return _vector.Merge(_otherVector);
    }

    // 6. Inserting (at middle)
    [Benchmark]
    public object Inserting()
    {
        return _vector.Insert(_randomIndex, 999);
    }

    // 7. Removing (at middle)
    [Benchmark]
    public object Removing()
    {
        return _vector.RemoveAt(_randomIndex);
    }

    // 8. Adding (Appending to end)
    [Benchmark]
    public object Appending()
    {
        return _vector.Add(999);
    }
}