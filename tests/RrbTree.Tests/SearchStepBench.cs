using System.Diagnostics;
using Collections;

namespace rrbtests;

// Rough timings of the operations the search step invariant touches, and of
// lookups in the trees they leave. Explicit: run by hand to compare.
[TestFixture, Explicit]
public class SearchStepBench
{
    private static double Time(Action body, int ops)
    {
        body(); // warm
        var best = double.MaxValue;
        for (var rep = 0; rep < 5; rep++)
        {
            var sw = Stopwatch.StartNew();
            body();
            best = Math.Min(best, sw.Elapsed.TotalNanoseconds / ops);
        }

        return best;
    }

    [Test]
    public void Bench()
    {
        const int n = 100_000;
        const int ops = 100_000;
        var dense = RrbList<int>.Create(Enumerable.Range(0, n).ToArray());
        var positions = new int[ops];
        var rng = new Random(7);
        for (var i = 0; i < ops; i++) positions[i] = rng.Next(n);

        RrbList<int> afterInserts = dense;
        var insert = Time(() =>
        {
            var l = dense;
            for (var i = 0; i < ops; i++) l = l.Insert(positions[i] % (l.Count + 1), i);
            afterInserts = l;
        }, ops);

        RrbList<int> afterRemoves = dense;
        var remove = Time(() =>
        {
            var l = dense.Merge(dense);
            for (var i = 0; i < ops; i++) l = l.RemoveAt(positions[i] % l.Count);
            afterRemoves = l;
        }, ops);

        var single = Time(() =>
        {
            for (var i = 0; i < ops; i++) _ = dense.Insert(positions[i], i);
        }, ops);

        var chain = Time(() =>
        {
            var l = RrbList<int>.Empty;
            var piece = RrbList<int>.Create(Enumerable.Range(0, 45).ToArray());
            for (var i = 0; i < ops / 10; i++) l = l.Merge(piece);
        }, ops / 10);

        var relaxed = afterInserts;
        var cuts = new (int, int)[ops];
        for (var i = 0; i < ops; i++)
        {
            var s = rng.Next(n);
            cuts[i] = (s, rng.Next(n - s));
        }

        var slice = Time(() =>
        {
            for (var i = 0; i < ops; i++) _ = relaxed.Slice(cuts[i].Item1, cuts[i].Item2);
        }, ops);

        var smallSlice = Time(() =>
        {
            for (var i = 0; i < ops; i++) _ = relaxed.Slice(cuts[i].Item1, 100 + cuts[i].Item2 % 2000);
        }, ops);

        var split = Time(() =>
        {
            for (var i = 0; i < ops; i++) _ = relaxed.Split(cuts[i].Item1);
        }, ops);

        Console.WriteLine($"slice, any            {slice,8:F1} ns");
        Console.WriteLine($"slice, 100-2100       {smallSlice,8:F1} ns");
        Console.WriteLine($"split                 {split,8:F1} ns");

        double Lookups(RrbList<int> l) => Time(() =>
        {
            long sum = 0;
            for (var i = 0; i < ops; i++) sum += l[positions[i] % l.Count];
            if (sum == 42) Console.WriteLine();
        }, ops);

        Console.WriteLine($"insert, chained       {insert,8:F1} ns");
        Console.WriteLine($"insert, one each      {single,8:F1} ns");
        Console.WriteLine($"remove, chained       {remove,8:F1} ns");
        Console.WriteLine($"merge 45, chained     {chain,8:F1} ns");
        Console.WriteLine($"lookup, dense         {Lookups(dense),8:F1} ns");
        Console.WriteLine($"lookup, after inserts {Lookups(afterInserts),8:F1} ns   (walk {afterInserts.MaxSearchDistance()})");
        Console.WriteLine($"lookup, after removes {Lookups(afterRemoves),8:F1} ns   (walk {afterRemoves.MaxSearchDistance()})");
    }
}
