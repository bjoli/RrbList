using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;
using Collections; // Ensure this matches your namespace

[MemoryDiagnoser]
// Orders the result table: Fast -> Slow
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
// Adds a column ranking them 1, 2, 3
[RankColumn] 
public class RrbListBenchmarks
{
    // Run for small, medium, and large lists to see scaling
    [Params(100, 10000, 100000)]
    public int N;

    private RrbList<int> _rrbList;
    private ImmutableList<int> _immutableList;
    private List<int> _list;

    // A secondary list for Merge/AddRange tests
    private RrbList<int> _rrbChunk;
    private ImmutableList<int> _immChunk;
    private List<int> _listChunk;

    private int _middleIndex;

    [GlobalSetup]
    public void Setup()
    {
        // 1. Create Main Data
        var data = Enumerable.Range(0, N).ToArray();

        // 2. Initialize Lists
        _rrbList = RrbList<int>.Create(data);
        _immutableList = ImmutableList.Create(data);
        _list = new List<int>(data);

        // 3. Setup Helpers
        _middleIndex = N / 2;
        
        // Chunk for merging (size 1000)
        var chunkData = Enumerable.Range(0, 1000).ToArray();
        _rrbChunk = RrbList<int>.Create(chunkData);
        _immChunk = ImmutableList.Create(chunkData);
        _listChunk = new List<int>(chunkData);
    }

    // --- 1. INDEXER (Random Access) ---
    // Critical for Vector performance.
    
    [Benchmark(Description = "RrbList[i]")]
    public int Indexer_RrbList()
    {
        // Sample start, middle, end to cover all tree depths
        return _rrbList[0] + _rrbList[_middleIndex] + _rrbList[N - 1];
    }

    [Benchmark(Description = "ImmutableList[i]")]
    public int Indexer_ImmutableList()
    {
        return _immutableList[0] + _immutableList[_middleIndex] + _immutableList[N - 1];
    }

    [Benchmark(Description = "List[i]")]
    public int Indexer_List()
    {
        return _list[0] + _list[_middleIndex] + _list[N - 1];
    }

    // --- 2. SET ITEM (Update) ---
    // Measures non-destructive update speed.
    
    [Benchmark(Description = "RrbList.SetItem")]
    public RrbList<int> SetItem_RrbList() => _rrbList.SetItem(_middleIndex, 999);

    [Benchmark(Description = "ImmutableList.SetItem")]
    public ImmutableList<int> SetItem_ImmutableList() => _immutableList.SetItem(_middleIndex, 999);

    [Benchmark(Description = "List[i] = x")]
    public void SetItem_List() => _list[_middleIndex] = 999; 

    // --- 3. INSERT (Middle) ---
    // Measures structural modification (Zipping vs Rebalancing vs Memory Copy).
    
    [Benchmark(Description = "RrbList.Insert")]
    public RrbList<int> Insert_RrbList() => _rrbList.Insert(_middleIndex, 999);

    [Benchmark(Description = "ImmutableList.Insert")]
    public ImmutableList<int> Insert_ImmutableList() => _immutableList.Insert(_middleIndex, 999);

    [Benchmark(Description = "List.Insert")]
    public void Insert_List()
    {
        _list.Insert(_middleIndex, 999);
        _list.RemoveAt(_middleIndex); // Immediate cleanup to keep N constant
    }

    // --- 4. REMOVE AT (Middle) ---
    
    [Benchmark(Description = "RrbList.RemoveAt")]
    public RrbList<int> RemoveAt_RrbList() => _rrbList.RemoveAt(_middleIndex);

    [Benchmark(Description = "ImmutableList.RemoveAt")]
    public ImmutableList<int> RemoveAt_ImmutableList() => _immutableList.RemoveAt(_middleIndex);

    [Benchmark(Description = "List.RemoveAt")]
    public void RemoveAt_List()
    {
        _list.RemoveAt(_middleIndex);
        _list.Insert(_middleIndex, 0); // Cleanup
    }

    // --- 5. ENUMERATION (Foreach) ---
    // Measures iterator throughput and struct optimization.
    
    [Benchmark(Description = "RrbList.Foreach")]
    public int Foreach_RrbList()
    {
        int sum = 0;
        foreach (var x in _rrbList) sum += x;
        return sum;
    }

    [Benchmark(Description = "ImmutableList.Foreach")]
    public int Foreach_ImmutableList()
    {
        int sum = 0;
        foreach (var x in _immutableList) sum += x;
        return sum;
    }

    [Benchmark(Description = "List.Foreach")]
    public int Foreach_List()
    {
        int sum = 0;
        foreach (var x in _list) sum += x;
        return sum;
    }

    // --- 6. ADD (Append) ---
    // Adding to the end is the most common operation.
    
    [Benchmark(Description = "RrbList.Add")]
    public RrbList<int> Add_RrbList() => _rrbList.Add(999);

    [Benchmark(Description = "ImmutableList.Add")]
    public ImmutableList<int> Add_ImmutableList() => _immutableList.Add(999);

    [Benchmark(Description = "List.Add")]
    public void Add_List()
    {
        _list.Add(999);
        _list.RemoveAt(_list.Count - 1); // Cleanup
    }
    
    // --- 7. SLICE / GET RANGE ---
    
    [Benchmark(Description = "RrbList.Slice")]
    public RrbList<int> Slice_RrbList() => _rrbList.Slice(_middleIndex / 2, 1000); // Slice 1000 items

    [Benchmark(Description = "ImmutableList.GetRange")]
    public IImmutableList<int> Slice_ImmutableList() => _immutableList.GetRange(_middleIndex / 2, 1000);

    [Benchmark(Description = "List.GetRange")]
    public List<int> Slice_List() => _list.GetRange(_middleIndex / 2, 1000); // Allocates new list copy
    
    // --- 8. MERGE / ADD RANGE ---
    
    [Benchmark(Description = "RrbList.Merge")]
    public RrbList<int> Merge_RrbList() => _rrbList.Merge(_rrbChunk); // O(log N) tree merge

    [Benchmark(Description = "ImmutableList.AddRange")]
    public IImmutableList<int> Merge_ImmutableList() => _immutableList.AddRange(_immChunk);

    [Benchmark(Description = "List.AddRange")]
    public void Merge_List()
    {
        _list.AddRange(_listChunk);
        // We let list grow here as remove range is expensive. 
        // This makes this specific benchmark slightly biased as list grows per iteration,
        // but for microbenchmark it's usually acceptable.
    }
}
