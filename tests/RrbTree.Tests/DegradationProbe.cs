using System.Diagnostics;
using Collections;

namespace rrbtests;

// Sequences of operations chosen to wear a tree down, and the shape each
// leaves: height, leaves against the fewest the items would fit in, the
// furthest a lookup walks, and what a lookup costs. Explicit: run by hand.
[TestFixture, Explicit]
public class DegradationProbe
{
    private static void Report(string name, RrbList<int> l, double opNs)
    {
        var rng = new Random(3);
        var idx = new int[200_000];
        for (var i = 0; i < idx.Length; i++) idx[i] = rng.Next(l.Count);

        var best = double.MaxValue;
        for (var rep = 0; rep < 5; rep++)
        {
            var sw = Stopwatch.StartNew();
            long sum = 0;
            foreach (var i in idx) sum += l[i];
            best = Math.Min(best, sw.Elapsed.TotalNanoseconds / idx.Length);
            if (sum == 42) Console.WriteLine();
        }

        var (height, leaves, internalNodes, fewest) = l.Shape();
        Console.WriteLine(
            $"{name,-26} n {l.Count,7}  height {height}  leaves {leaves,6} (fewest {fewest,6})  internal {internalNodes,5}  walk {l.MaxSearchDistance(),3}  lookup {best,5:F1} ns  op {opNs,7:F1} ns");
    }

    // The differential fuzz, with its wear check, over many more seeds.
    [Test]
    public void ManySeeds()
    {
        var failed = new List<string>();
        for (var seed = 9; seed < 309; seed++)
        {
            try { new DifferentialFuzz().RandomOperations_MatchList(seed); }
            catch (Exception e) { failed.Add(e.Message.Split('\n')[0]); }
        }

        Console.WriteLine($"{failed.Count} of 300 seeds failed");
        foreach (var f in failed.Take(5)) Console.WriteLine("  " + f);
    }

    private static RrbList<int> Dense(int n) => RrbList<int>.Create(Enumerable.Range(0, n).ToArray());

    [Test]
    public void Wear()
    {
        const int n = 100_000;

        Report("dense", Dense(n), 0);

        {
            var rng = new Random(1);
            var l = Dense(n);
            var sw = Stopwatch.StartNew();
            var ops = 0;
            while (l.Count > n / 100) { l = l.RemoveAt(rng.Next(l.Count)); ops++; }
            Report("remove 99% at random", l, sw.Elapsed.TotalNanoseconds / ops);
        }

        {
            var rng = new Random(2);
            var l = Dense(n);
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 90_000; k++) l = l.RemoveAt(rng.Next(Math.Min(2000, l.Count)));
            Report("remove 90% near the front", l, sw.Elapsed.TotalNanoseconds / 90_000);
        }

        {
            var rng = new Random(3);
            var l = RrbList<int>.Empty;
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 50_000; k++) l = l.Merge(Dense(rng.Next(1, 6)));
            Report("append 50k lists of 1-5", l, sw.Elapsed.TotalNanoseconds / 50_000);
        }

        {
            var rng = new Random(4);
            var l = RrbList<int>.Empty;
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 50_000; k++) l = Dense(rng.Next(1, 6)).Merge(l);
            Report("prepend 50k lists of 1-5", l, sw.Elapsed.TotalNanoseconds / 50_000);
        }

        {
            var rng = new Random(5);
            var l = RrbList<int>.Empty;
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 20_000; k++) l = l.Merge(Dense(rng.Next(30, 70)));
            Report("append 20k lists of 30-70", l, sw.Elapsed.TotalNanoseconds / 20_000);
        }

        {
            var rng = new Random(6);
            var l = Dense(n);
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 500_000; k++)
                l = k % 2 == 0 ? l.Insert(rng.Next(l.Count + 1), k) : l.RemoveAt(rng.Next(l.Count));
            Report("churn 500k insert/remove", l, sw.Elapsed.TotalNanoseconds / 500_000);
        }

        {
            var l = Dense(n);
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < n; k++) l = l.Insert(n / 2, k);
            Report("insert 100k at one index", l, sw.Elapsed.TotalNanoseconds / n);
        }

        {
            var rng = new Random(7);
            var l = Dense(n);
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 20_000; k++)
            {
                var (x, y) = l.Split(rng.Next(l.Count + 1));
                l = y.Merge(x);
            }

            Report("cut and swap 20k", l, sw.Elapsed.TotalNanoseconds / 20_000);
        }

        {
            var rng = new Random(8);
            var l = Dense(n);
            var sw = Stopwatch.StartNew();
            for (var k = 0; k < 20_000; k++)
            {
                // Take a small piece out of the middle.
                var at = rng.Next(l.Count - 10);
                var (x, rest) = l.Split(at);
                var (_, y) = rest.Split(rng.Next(1, 10));
                l = x.Merge(y).Merge(Dense(rng.Next(1, 10)));
            }

            Report("cut out and append 20k", l, sw.Elapsed.TotalNanoseconds / 20_000);
        }
    }
}
