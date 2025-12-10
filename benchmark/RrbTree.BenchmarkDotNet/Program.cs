using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Collections;

[MemoryDiagnoser] // CRITICAL: This tracks GC Allocations per operation
public class RrbBenchmarks
{
    private const int N = 10000;
    private RrbList<int> _list;
    private RrbList<int> unbalanced;
    private List<int> _list2;
    

    [GlobalSetup]
    public void Setup()
    {
        _list = new RrbList<int>(Enumerable.Range(0, N));
        _list2 = new List<int>(Enumerable.Range(0, N));

        unbalanced = misc.MakeUnbalanced(35000);
    }


    [Benchmark]
    public void Build_List()
    {
        var l = new List<int>();
        for (var i = 0; i < N; i++) l.Add(i);
    }

    [Benchmark]
    public long IterList()
    {
        long sum = 0;
        foreach (var i in _list2) sum += i;

        return sum;
    }



    [Benchmark]
    public long IterRrb()
    {
        long sum = 0;
        foreach (var i in _list) sum += i;

        return sum;
    }
    
    [Benchmark]
    public long IndexUnbalanced()
    {
        long sum = 0;
        for (int i = 0; i < unbalanced.Count; i+=2)
            sum += unbalanced[i];
        
        return sum;
    }

    [Benchmark]
    public long FoldRrb()
    {
        return _list.Fold(0, (i, i1) => i + i1);
    }


    [Benchmark]
    public List<int> Slice_MiddleList()
    {
        return _list2.Slice(N / 4, N / 2);
    }


    [Benchmark]
    public void Build_Transient()
    {
        var builder = new RrbBuilder<int>(1024);
        for (var i = 0; i < N; i++) builder.Add(i);
        var res = builder.ToImmutable();
    }

    [Benchmark]
    public void Build_Transient_Standard_Leaf()
    {
        var builder = new RrbBuilder<int>(32);
        for (var i = 0; i < N; i++) builder.Add(i);
        var res = builder.ToImmutable();
    }


    [Benchmark]
    public int RandomAccessRrb()
    {
        // Access a value deep in the tree
        return _list[N / 2];
    }

    [Benchmark]
    public int RandomAccessList()
    {
        return _list2[N / 2];
    }

    [Benchmark]
    public RrbList<int> Slice_MiddleRrb()
    {
        return _list.Slice(N / 4, N / 2);
    }
}

internal class Program
{
    private static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}