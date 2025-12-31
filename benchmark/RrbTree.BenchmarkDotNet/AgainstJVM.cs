using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Generic;
using System.Linq;

// Replace with your actual library namespace
using Collections; 

[MemoryDiagnoser]
[RankColumn]
public class AgainstJvm
{
    [Params(1_000_000)]
    public int Size;

    private RrbList<int> _vector;
    private RrbList<int> _otherVector;
    private int _splitIndex;
    private int _randomIndex;

    // GlobalSetup runs once before all benchmarks to prepare the data
    [GlobalSetup]
    public void Setup()
    {
        // 0. Setup: Creating base vectors for other operations
        _vector = new RrbList<int>(Enumerable.Range(0, Size));
        _otherVector = new RrbList<int>(Enumerable.Range(0, Size));
        
        _splitIndex = Size / 2;
        _randomIndex = Size / 2;
    }

    // 1. Indexing (Random Access)
    [Benchmark]
    public int Indexing() => _vector[_randomIndex];

    // 2. Building (Construction via Builder)
    [Benchmark]
    public RrbList<int> Building()
    {
        var b = new RrbBuilder<int>(1024);
        for (int i = 0; i < Size; i++)
        {
            b.Add(i);
        }
        // Assuming ToImmutable() or similar exists to finalize the builder
        return b.ToImmutable(); 
    }

    // 3. Slicing
    [Benchmark]
    public RrbList<int> Slicing() => _vector.Slice(1000, Size - 1000);

    // 4. Splitting
    [Benchmark]
    public (RrbList<int>, RrbList<int>) Splitting()
    {
        // Deconstructs the tuple returned by your split method
        (var left, var right) = _vector.Split(_splitIndex);
        return (left, right);
    }

    // 5. Merging (Concatenation)
    [Benchmark]
    public RrbList<int> Merging() => _vector.Merge(_otherVector);

    // 6. Inserting (at middle)
    [Benchmark]
    public RrbList<int> Inserting() => _vector.Insert(_randomIndex, 999);

    // 7. Removing (at middle)
    [Benchmark]
    public RrbList<int> Removing() => _vector.RemoveAt(_randomIndex);
    
    // 8. Adding (Appending to end)
    [Benchmark]
    public RrbList<int> Appending() => _vector.Add(999);
}