using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.Declared)]
[HideColumns("Error", "StdDev", "Rank")]
public class Comparison
{
    private const int RandomCount = 20;
    private global::TryoptCollections.RrbList<int> _tryoptChunk;
    private global::TryoptCollections.RrbList<int> _tryoptList;

    private int _middleIndex;
    private int[] _randomIndexes;

    private global::Collections.RrbList<int> _rrbChunk;
    private global::Collections.RrbList<int> _rrbList;

    [Params(100, 10000, 100000)] 
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        var data = Enumerable.Range(0, N).ToArray();

        _rrbList = global::Collections.RrbList<int>.Create(data);
        _tryoptList = global::TryoptCollections.RrbList<int>.Create(data);

        _middleIndex = N / 2;

        var chunkData = Enumerable.Range(0, 1000).ToArray();

        _rrbChunk = global::Collections.RrbList<int>.Create(chunkData);
        _tryoptChunk = global::TryoptCollections.RrbList<int>.Create(chunkData);

        var rnd = new Random(42);
        _randomIndexes = new int[RandomCount];
        for (var i = 0; i < RandomCount; i++) _randomIndexes[i] = rnd.Next(0, N);
    }

    [Benchmark(Description = "RrbList[i]")]
    [BenchmarkCategory("Indexing")]
    public int Indexer_RrbList()
    {
        return _rrbList[0] + _rrbList[_middleIndex] + _rrbList[N - 1];
    }

    [Benchmark(Description = "TryoptList[i]")]
    [BenchmarkCategory("Indexing")]
    public int Indexer_ImmutableList()
    {
        return _tryoptList[0] + _tryoptList[_middleIndex] + _tryoptList[N - 1];
    }

    [Benchmark(Description = "RrbList.SetItem")]
    [BenchmarkCategory("SetItem")]
    public object SetItem_RrbList()
    {
        var list = _rrbList;
        foreach (var idx in _randomIndexes) list = list.SetItem(idx, 999);
        return list;
    }

    [Benchmark(Description = "TryoptList.SetItem")]
    [BenchmarkCategory("SetItem")]
    public object SetItem_ImmutableList()
    {
        var list = _tryoptList;
        foreach (var idx in _randomIndexes) list = list.SetItem(idx, 999);
        return list;
    }

    [Benchmark(Description = "RrbList.Insert")]
    [BenchmarkCategory("Insert")]
    public object Insert_RrbList()
    {
        return _rrbList.Insert(_middleIndex, 999);
    }

    [Benchmark(Description = "TryoptList.Insert")]
    [BenchmarkCategory("Insert")]
    public object Insert_ImmutableList()
    {
        return _tryoptList.Insert(_middleIndex, 999);
    }

    [Benchmark(Description = "RrbList.RemoveAt")]
    [BenchmarkCategory("RemoveAt")]
    public object RemoveAt_RrbList()
    {
        return _rrbList.RemoveAt(_middleIndex);
    }

    [Benchmark(Description = "TryoptList.RemoveAt")]
    [BenchmarkCategory("RemoveAt")]
    public object RemoveAt_ImmutableList()
    {
        return _tryoptList.RemoveAt(_middleIndex);
    }

    [Benchmark(Description = "RrbList.Foreach")]
    [BenchmarkCategory("Iteration")]
    public int Foreach_RrbList()
    {
        var sum = 0;
        foreach (var x in _rrbList) sum += x;
        return sum;
    }

    [Benchmark(Description = "TryoptList.Foreach")]
    [BenchmarkCategory("Iteration")]
    public int Foreach_ImmutableList()
    {
        var sum = 0;
        foreach (var x in _tryoptList) sum += x;
        return sum;
    }

    [Benchmark(Description = "RrbList.Add")]
    [BenchmarkCategory("Add")]
    public object Add_RrbList()
    {
        global::Collections.RrbList<int> a = new global::Collections.RrbList<int>();
        for (var i = 0; i < N; i++)
        {
            a = a.Add(1);
        }
        return a;
    }

    [Benchmark(Description = "TryoptList.Add")]
    [BenchmarkCategory("Add")]
    public object Add_ImmutableList()
    {
        global::TryoptCollections.RrbList<int> a = global::TryoptCollections.RrbList<int>.Empty;
        for (int i = 0; i < N; i++)
        {
            a = a.Add(i);
        }
        return a;
    }

    [Benchmark(Description = "RrbList.Slice")]
    [BenchmarkCategory("Slice")]
    public object Slice_RrbList()
    {
        return _rrbList.Slice(_middleIndex / 2, N / 4);
    }

    [Benchmark(Description = "TryoptList.Slice")]
    [BenchmarkCategory("Slice")]
    public object Slice_ImmutableList()
    {
        return _tryoptList.Slice(_middleIndex / 2, N / 4);
    }

    [Benchmark(Description = "RrbList.Merge")]
    [BenchmarkCategory("Merge")]
    public object Merge_RrbList()
    {
        return _rrbList.Merge(_rrbChunk);
    }

    [Benchmark(Description = "TryoptList.Merge")]
    [BenchmarkCategory("Merge")]
    public object Merge_ImmutableList()
    {
        return _tryoptList.AddRange(_tryoptChunk);
    }
}
