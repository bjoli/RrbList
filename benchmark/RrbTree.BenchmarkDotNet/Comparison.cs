using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;
using System.Net.Quic;
using Collections; // Ensure this matches your namespace

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.Declared)]
[HideColumns("Error", "StdDev", "Rank", "Gen0", "Gen1", "Gen2")]
public class RrbListBenchmarks
{
    // Run for small, medium, and large lists to see scaling
    [Params(100, 10000, 100000)]
    public int N;

    private RrbList<int> _rrbList;
    private RrbList<int> _rrbUnbalanced;
    private RrbBuilder<int> _rrbBuilder;
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
        _rrbBuilder = _rrbList.ToBuilder(1024);
        _rrbUnbalanced = misc.MakeUnbalanced(N);
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
    [BenchmarkCategory("Indexing")]
    public int Indexer_RrbList()
    {
        // Sample start, middle, end to cover all tree depths
        return _rrbList[0] + _rrbList[_middleIndex] + _rrbList[N - 1];
    }
    [Benchmark(Description = "RrbListUnbalanced[i]")]
    [BenchmarkCategory("Indexing")]
    public int Indexer_RrbListUnbalanced()
    {
        // Sample start, middle, end to cover all tree depths
        return _rrbUnbalanced[0] + _rrbUnbalanced[_middleIndex] + _rrbUnbalanced[^11];
    }

    [Benchmark(Description = "ImmutableList[i]")]
    [BenchmarkCategory("Indexing")]
    public int Indexer_ImmutableList()
    {
        return _immutableList[0] + _immutableList[_middleIndex] + _immutableList[N - 1];
    }

    [Benchmark(Description = "List[i]")]
    [BenchmarkCategory("Indexing")]
    public int Indexer_List()
    {
        return _list[0] + _list[_middleIndex] + _list[N - 1];
    }

    // --- 2. SET ITEM (Update) ---
    // Measures non-destructive update speed.
    
    [Benchmark(Description = "RrbList.SetItem")]
    [BenchmarkCategory("SetItem")]
    public RrbList<int> SetItem_RrbList() => _rrbList.SetItem(_middleIndex, 999);
    
    [Benchmark(Description = "RrbBuilder.SetItem")]
    [BenchmarkCategory("SetItem")]
    public void SetItem_RrbBuilder() => _rrbBuilder.SetItem(_middleIndex, 999);
    
    [Benchmark(Description = "RrbListUnbalanced.SetItem")]
    [BenchmarkCategory("SetItem")]
    public RrbList<int> SetItem_RrbListUnbalanced() => _rrbList.SetItem(_middleIndex, 999);

    [Benchmark(Description = "ImmutableList.SetItem")]
    [BenchmarkCategory("SetItem")]
    public ImmutableList<int> SetItem_ImmutableList() => _immutableList.SetItem(_middleIndex, 999);

    [Benchmark(Description = "List[i] = x")]
    [BenchmarkCategory("SetItem")]
    public void SetItem_List() => _list[_middleIndex] = 999; 

    // --- 3. INSERT (Middle) ---
    // Measures structural modification (Zipping vs Rebalancing vs Memory Copy).
    
    [Benchmark(Description = "RrbList.Insert")]
    [BenchmarkCategory("Insert")]
    public RrbList<int> Insert_RrbList() => _rrbList.Insert(_middleIndex, 999);
    
    
    [Benchmark(Description = "RrbListUnbalanced.Insert")]
    [BenchmarkCategory("Insert")]
    public RrbList<int> Insert_RrbListUnbalanced() => _rrbUnbalanced.Insert(_middleIndex, 900);

    [Benchmark(Description = "ImmutableList.Insert")]
    [BenchmarkCategory("Insert")]
    public ImmutableList<int> Insert_ImmutableList() => _immutableList.Insert(_middleIndex, 999);

    [Benchmark(Description = "List.Insert")]
    [BenchmarkCategory("Insert")]
    public void Insert_List()
    {
        _list.Insert(_middleIndex, 999);
        //_list.RemoveAt(_middleIndex);
    }

    // --- 4. REMOVE AT (Middle) ---
    
    [Benchmark(Description = "RrbList.RemoveAt")]
    [BenchmarkCategory("RemoveAt")]
    public RrbList<int> RemoveAt_RrbList() => _rrbList.RemoveAt(_middleIndex);
    [Benchmark(Description = "RrbListUnbalanced.RemoveAt")]
    [BenchmarkCategory("RemoveAt")]
    public RrbList<int> RemoveAt_RrbListUnbalanced() => _rrbUnbalanced.RemoveAt(_middleIndex);

    [Benchmark(Description = "ImmutableList.RemoveAt")]
    [BenchmarkCategory("RemoveAt")]
    public ImmutableList<int> RemoveAt_ImmutableList() => _immutableList.RemoveAt(_middleIndex);

    [Benchmark(Description = "List.RemoveAt")]
    [BenchmarkCategory("RemoveAt")]
    public void RemoveAt_List()
    {
        _list.RemoveAt(_middleIndex);
        _list.Add(0); // Cleanup
    }

    // --- 5. ENUMERATION (Foreach) ---
    // Measures iterator throughput and struct optimization.
    
    [Benchmark(Description = "RrbList.Foreach")]
    [BenchmarkCategory("Iteration")]
    public int Foreach_RrbList()
    {
        int sum = 0;
        foreach (var x in _rrbList) sum += x;
        return sum;
    }
    
    [Benchmark(Description = "RrbList.Fold")]
    [BenchmarkCategory("Iteration")]
    public int Fold_RrbList()
    {
        return _rrbList.Fold(0, (x, y) => x + y);
    }
    
    
    [Benchmark(Description = "RrbListUnbalanced.Foreach")]
    [BenchmarkCategory("Iteration")]
    public int Foreach_RrbListUnbalanced()
    {
        int sum = 0;
        foreach (var x in _rrbUnbalanced) sum += x;
        return sum;
    }


    [Benchmark(Description = "ImmutableList.Foreach")]
    [BenchmarkCategory("Iteration")]
    public int Foreach_ImmutableList()
    {
        int sum = 0;
        foreach (var x in _immutableList) sum += x;
        return sum;
    }

    [Benchmark(Description = "List.Foreach")]
    [BenchmarkCategory("Iteration")]
    public int Foreach_List()
    {
        int sum = 0;
        foreach (var x in _list) sum += x;
        return sum;
    }

    // --- 6. ADD (Append) ---
    // Adding to the end is the most common operation.
    
    [Benchmark(Description = "RrbList.Add")]
    [BenchmarkCategory("Add")]
    public RrbList<int> Add_RrbList() => _rrbList.Add(999);
    
    [Benchmark(Description = "RrbListUnbalanced.Add")]
    [BenchmarkCategory("Add")]
    public RrbList<int> Add_RrbListUnbalanced() => _rrbUnbalanced.Add(999);
    
    [Benchmark(Description = "RrbBuilder.Add")]
    [BenchmarkCategory("Add")]
    public void Add_RrbBuilder() => _rrbBuilder.Add(999);

    [Benchmark(Description = "ImmutableList.Add")]
    [BenchmarkCategory("Add")]
    public ImmutableList<int> Add_ImmutableList() => _immutableList.Add(999);

    [Benchmark(Description = "List.Add")]
    [BenchmarkCategory("Add")]
    public void Add_List()
    {
        _list.Add(999);
        _list.RemoveAt(_list.Count - 1); // Cleanup
    }
    
    // --- 7. SLICE / GET RANGE ---
    
    [Benchmark(Description = "RrbList.Slice")]
    [BenchmarkCategory("Slice")]
    public RrbList<int> Slice_RrbList() => _rrbList.Slice(_middleIndex / 2, N/4); // Slice 1000 items

    
    [Benchmark(Description = "RrbListUnbalanced.Slice")]
    [BenchmarkCategory("Slice")]
    public RrbList<int> Slice_RrbListUnbalanced() => _rrbUnbalanced.Slice(_middleIndex / 2, N/4);
    
    
    [Benchmark(Description = "ImmutableList.GetRange")]
    [BenchmarkCategory("Slice")]
    public IImmutableList<int> Slice_ImmutableList() => _immutableList.GetRange(_middleIndex / 2, N/4);

    [Benchmark(Description = "List.GetRange")]
    [BenchmarkCategory("Slice")]
    public List<int> Slice_List() => _list.GetRange(_middleIndex / 2, N/4); // Allocates new list copy
    
    // --- 8. MERGE / ADD RANGE ---
    
    [Benchmark(Description = "RrbList.Merge")]
    [BenchmarkCategory("Merge")]
    public RrbList<int> Merge_RrbList() => _rrbList.Merge(_rrbChunk); // O(log N) tree merge
    
    
    [Benchmark(Description = "RrbListUnbalanced.Merge")]
    [BenchmarkCategory("Merge")]
    public RrbList<int> Merge_RrbListUnbalanced() => _rrbUnbalanced.Merge(_rrbChunk); // O(log N) tree merge

    [Benchmark(Description = "ImmutableList.AddRange")]
    [BenchmarkCategory("Merge")]
    public IImmutableList<int> Merge_ImmutableList() => _immutableList.AddRange(_immChunk);

    [IterationSetup(Target = nameof(Merge_List))]
    public void ResetList()
    {
        // Reset the list to a clean state before the single run
        
        var data = Enumerable.Range(0, N).ToArray();
        _list = new List<int>(data); 
    }

    [Benchmark]
    [InvocationCount(10000)] // <--- Tells BDN: "Run this exactly once, then reset."
    public void Merge_List()
    {
        _list.AddRange(_listChunk);
    }
}
