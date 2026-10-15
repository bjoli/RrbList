using Collections;

namespace rrbtests;

// Random operations at both ends of an RrbBuilder, and sets and lists taken
// from it, beside a List<int>. Every list taken is kept with what it held,
// and checked again at the end: a builder must not change what it handed out.
[TestFixture]
public class BuilderEndsFuzz
{
    private static void Check(List<int> expected, RrbBuilder<int> builder, string what)
    {
        if (builder.Count != expected.Count)
            throw new Exception($"{what}: count {builder.Count}, expected {expected.Count}");
        for (var i = 0; i < expected.Count; i++)
            if (builder[i] != expected[i])
                throw new Exception($"{what}: index {i} is {builder[i]}, expected {expected[i]}");
    }

    private static void CheckList(List<int> expected, RrbList<int> list, string what)
    {
        list.VerifyIntegrity();
        if (list.Count != expected.Count)
            throw new Exception($"{what}: count {list.Count}, expected {expected.Count}");
        var j = 0;
        foreach (var x in list)
        {
            if (x != expected[j]) throw new Exception($"{what}: at {j} is {x}, expected {expected[j]}");
            j++;
        }
    }

    // A builder of `size` items, made one of several ways, so that the ends
    // start from differently shaped trees, heads and tails.
    private static (RrbBuilder<int>, List<int>) Start(Random rng, int size, ref int next)
    {
        var items = new List<int>();
        for (var i = 0; i < size; i++) items.Add(next++);

        switch (rng.Next(4))
        {
            case 0:
            {
                var b = new RrbBuilder<int>();
                foreach (var x in items) b.Add(x);
                return (b, items);
            }
            case 1:
                return (RrbList<int>.Create(items.ToArray()).ToBuilder(), items);
            case 2:
            {
                // A list with a head and a Gap, from pops at the front.
                var extra = rng.Next(1, 100);
                var list = RrbList<int>.Create(items.ToArray());
                var front = new List<int>();
                for (var i = 0; i < extra; i++) front.Add(next++);
                list = RrbList<int>.Create(front.ToArray()).Merge(list);
                for (var i = 0; i < extra; i++) list = list.PopFirst();
                return (list.ToBuilder(), items);
            }
            default:
            {
                // Built from the front.
                var list = RrbList<int>.Empty;
                for (var i = size - 1; i >= 0; i--) list = list.Prepend(items[i]);
                return (list.ToBuilder(), items);
            }
        }
    }

    [Test]
    public void RandomEnds([Values(1, 2, 3, 4, 5, 6, 7, 8)] int seed)
    {
        var rng = new Random(seed);
        var next = 0;
        var sizes = new[] { 0, 1, 31, 32, 33, 64, 1000, 1100, 40_000 };
        var (builder, expected) = Start(rng, sizes[rng.Next(sizes.Length)], ref next);
        var taken = new List<(RrbList<int>, List<int>)>();

        for (var step = 0; step < 4000; step++)
        {
            var op = rng.Next(100);
            string what;
            if (op < 22)
            {
                // Runs, so that the ends cross leaves.
                var n = rng.Next(1, 70);
                for (var i = 0; i < n; i++)
                {
                    builder.Add(next);
                    expected.Add(next++);
                }
                what = $"add {n}";
            }
            else if (op < 44)
            {
                var n = Math.Min(rng.Next(1, 70), expected.Count);
                for (var i = 0; i < n; i++)
                {
                    var got = builder.Pop();
                    var want = expected[^1];
                    expected.RemoveAt(expected.Count - 1);
                    if (got != want) throw new Exception($"step {step}: pop gave {got}, expected {want}");
                }
                what = $"pop {n}";
            }
            else if (op < 64)
            {
                var n = rng.Next(1, 70);
                for (var i = 0; i < n; i++)
                {
                    builder.Prepend(next);
                    expected.Insert(0, next++);
                }
                what = $"prepend {n}";
            }
            else if (op < 84)
            {
                var n = Math.Min(rng.Next(1, 70), expected.Count);
                for (var i = 0; i < n; i++)
                {
                    var got = builder.PopFirst();
                    var want = expected[0];
                    expected.RemoveAt(0);
                    if (got != want) throw new Exception($"step {step}: pop-first gave {got}, expected {want}");
                }
                what = $"pop-first {n}";
            }
            else if (op < 94)
            {
                if (expected.Count == 0) continue;
                var i = rng.Next(expected.Count);
                builder.SetItem(i, next);
                expected[i] = next++;
                what = $"set {i}";
            }
            else
            {
                var list = builder.ToImmutable();
                CheckList(expected, list, $"step {step}: taken");
                taken.Add((list, new List<int>(expected)));
                what = "take";
            }

            Check(expected, builder, $"step {step}, {what}");
        }

        foreach (var (list, held) in taken) CheckList(held, list, "a list taken earlier");
        CheckList(expected, builder.ToImmutable(), "at the end");
    }

    [Test]
    public void EmptyEndsThrow()
    {
        var b = new RrbBuilder<int>();
        Assert.Throws<InvalidOperationException>(() => b.Pop());
        Assert.Throws<InvalidOperationException>(() => b.PopFirst());
        b.Add(1);
        Assert.That(b.PopFirst(), Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => b.Pop());
    }
}
