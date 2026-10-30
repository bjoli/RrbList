using Collections;

namespace rrbtests;

/// <summary>
///     <see cref="RrbList{T}.Concat" /> of random pieces gives their items in
///     order, in a sound tree, which later operations work on. The pieces are
///     of every shape a vec has: a tail only, dense, with a head and a Gap,
///     relaxed from merges and inserts, slices, and empty; small ones, which
///     Concat copies, and big ones, which it merges, in runs and mixed.
/// </summary>
[TestFixture]
public class ConcatFuzz
{
    private static void Check(RrbList<int> list, IReadOnlyList<int> expected, string what)
    {
        list.VerifyIntegrity();
        if (list.Count != expected.Count) throw new Exception($"{what}: count {list.Count}, expected {expected.Count}");
        var i = 0;
        foreach (var x in list)
        {
            if (x != expected[i]) throw new Exception($"{what}: at {i} is {x}, expected {expected[i]}");
            i++;
        }
        for (var j = 0; j < expected.Count; j += 1 + expected.Count / 97)
            if (list[j] != expected[j]) throw new Exception($"{what}: index {j} is {list[j]}, expected {expected[j]}");
    }

    // A piece of `n` items from `next` on, of a random shape.
    private static RrbList<int> Piece(Random rng, int n, int next)
    {
        var items = Enumerable.Range(next, n).ToArray();
        switch (rng.Next(5))
        {
            case 0:
                return RrbList<int>.Create(items);
            case 1:
            {
                // A Gap and a head, from items taken off the front.
                var drop = rng.Next(1, 100);
                var list = RrbList<int>.Create(Enumerable.Range(next - drop, n + drop).ToArray());
                for (var k = 0; k < drop; k++) list = list.PopFirst();
                return list;
            }
            case 2:
            {
                // A slice of a longer list.
                var before = rng.Next(0, 70);
                var whole = RrbList<int>.Create(Enumerable.Range(next - before, n + before + rng.Next(0, 70)).ToArray());
                return RrbFun.Slice(whole, before, n);
            }
            case 3:
            {
                // Relaxed: merged from two parts.
                var cut = rng.Next(0, n + 1);
                return RrbList<int>.Create(items[..cut]).Merge(RrbList<int>.Create(items[cut..]));
            }
            default:
            {
                // Relaxed: built with inserts.
                var list = RrbList<int>.Empty;
                var at = 0;
                foreach (var x in items)
                {
                    list = rng.Next(4) == 0 && at > 0 ? RrbFun.Insert(list, at, x) : list.Add(x);
                    at++;
                }
                return list;
            }
        }
    }

    private static int Size(Random rng) => rng.Next(6) switch
    {
        0 => 0,
        1 => rng.Next(1, 33),
        2 => rng.Next(1, RrbList<int>.ConcatMergeAtLeast),
        3 => RrbList<int>.ConcatMergeAtLeast + rng.Next(-2, 3),
        4 => rng.Next(RrbList<int>.ConcatMergeAtLeast, 5000),
        _ => rng.Next(20_000, 70_000),
    };

    [Test]
    public void ConcatOfRandomPieces([Values(1, 2, 3, 4, 5, 6, 7, 8)] int seed)
    {
        var rng = new Random(seed);
        for (var round = 0; round < 40; round++)
        {
            var count = rng.Next(4) == 0 ? rng.Next(0, 4) : rng.Next(1, 120);
            var pieces = new List<RrbList<int>>();
            var next = 0;
            for (var p = 0; p < count; p++)
            {
                var n = Size(rng);
                pieces.Add(Piece(rng, n, next));
                next += n;
            }

            var expected = Enumerable.Range(0, next).ToList();
            var what = $"seed {seed} round {round}: {count} pieces, {next} items";
            var joined = RrbList<int>.Concat(pieces.ToArray());
            Check(joined, expected, what);

            // What is made after it: an item at each end, a merge and a slice.
            var grown = joined.Add(-1);
            Check(grown, expected.Append(-1).ToList(), $"{what}, then Add");
            if (next > 0)
                Check(joined.PopFirst(), expected.Skip(1).ToList(), $"{what}, then PopFirst");
            Check(joined.Merge(joined), expected.Concat(expected).ToList(), $"{what}, merged with itself");
            if (next > 10)
            {
                var from = rng.Next(0, next / 2);
                Check(RrbFun.Slice(joined, from, next / 3), expected.Skip(from).Take(next / 3).ToList(), $"{what}, then a slice");
            }
        }
    }

    [Test]
    public void ConcatOfNoneOrOne()
    {
        Assert.That(RrbList<int>.Concat(ReadOnlySpan<RrbList<int>>.Empty).Count, Is.EqualTo(0));
        var one = RrbList<int>.Create(Enumerable.Range(0, 5000).ToArray());
        Assert.That(RrbList<int>.Concat(new[] { one }), Is.SameAs(one));
        var small = RrbList<int>.Create(new[] { 1, 2, 3 });
        Assert.That(RrbList<int>.Concat(new[] { RrbList<int>.Empty, small, RrbList<int>.Empty }).ToList(), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void SmallPiecesGiveADenseTree()
    {
        var pieces = Enumerable.Range(0, 5000).Select(k => RrbList<int>.Create(Enumerable.Range(k * 20, 20).ToArray())).ToArray();
        var joined = RrbList<int>.Concat(pieces);
        Check(joined, Enumerable.Range(0, 100_000).ToList(), "5000 pieces of 20");
        Assert.That(joined.TreeIsRelaxed, Is.False);
    }
}
