using System.Text.RegularExpressions;
using Collections;

namespace rrbtests;

// A dense list split, its pieces perhaps mapped, and merged back in order,
// comes back dense: no size tables, the same items, a sound tree. Pieces are
// merged left to right, and in pairs, as a parallel job would join them.
[TestFixture]
public class AlignedMergeFuzz
{
    private static int Tables(RrbList<int> list) => Regex.Matches(list.ToString(), @"\[TABLE").Count;

    private static void Check(RrbList<int> list, IReadOnlyList<int> expected, string what, bool dense = true)
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
        if (dense && Tables(list) != 0) throw new Exception($"{what}: {Tables(list)} size tables\n{list}");
    }

    // A dense list of `n`: from a builder, or with a Gap and a head from
    // removing items at the front.
    private static (RrbList<int>, List<int>) Dense(Random rng, int n)
    {
        var drop = rng.Next(2) == 0 ? 0 : rng.Next(1, 3000);
        var list = RrbList<int>.Create(Enumerable.Range(0, n + drop).ToArray());
        for (var i = 0; i < drop; i++) list = list.PopFirst();
        if (Tables(list) != 0) throw new Exception("the list to split is not dense");
        return (list, Enumerable.Range(drop, n).ToList());
    }

    [Test]
    public void SplitAndMergeBack([Values(1, 2, 3, 4, 5, 6)] int seed)
    {
        var rng = new Random(seed);
        var sizes = new[] { 33, 64, 100, 1024, 1025, 5000, 33_000, 100_000, 1_100_000 };
        for (var round = 0; round < 60; round++)
        {
            var n = sizes[rng.Next(sizes.Length)];
            var (list, expected) = Dense(rng, n);
            var at = rng.Next(0, n + 1);
            var (l, r) = list.Split(at);

            var mapped = rng.Next(2) == 0;
            if (mapped)
            {
                l = l.Map(x => x * 2);
                r = r.Map(x => x * 2);
                expected = expected.Select(x => x * 2).ToList();
            }

            Check(l.Merge(r), expected, $"seed {seed} round {round}: n {n}, split at {at}, mapped {mapped}");
        }
    }

    // Merges two pieces, and checks that the merge is dense when both are.
    private static RrbList<int> MergeChecked(RrbList<int> a, RrbList<int> b, string what)
    {
        var merged = a.Merge(b);
        if (Tables(a) == 0 && Tables(b) == 0 && Tables(merged) != 0)
            throw new Exception($"{what}: two dense pieces merged with size tables\nA {a}\nB {b}\nGOT {merged}");
        return merged;
    }

    // Pieces merged left to right, and in pairs, as a parallel job joins them.
    private static void MergeBack(List<RrbList<int>> pieces, List<int> want, string what)
    {
        var dense = pieces.All(piece => Tables(piece) == 0);

        var leftToRight = RrbList<int>.Empty;
        foreach (var piece in pieces) leftToRight = MergeChecked(leftToRight, piece, what);
        Check(leftToRight, want, $"{what}, left to right", dense);

        var level = pieces;
        while (level.Count > 1)
        {
            var next = new List<RrbList<int>>();
            for (var i = 0; i < level.Count; i += 2)
                next.Add(i + 1 < level.Count ? MergeChecked(level[i], level[i + 1], what) : level[i]);
            level = next;
        }
        Check(level[0], want, $"{what}, in pairs", dense);
    }

    private static List<RrbList<int>> Cut(RrbList<int> list, IEnumerable<int> cuts)
    {
        var pieces = new List<RrbList<int>>();
        var rest = list;
        var taken = 0;
        foreach (var c in cuts)
        {
            var (piece, after) = rest.Split(c - taken);
            pieces.Add(piece.Map(x => x + 1));
            rest = after;
            taken = c;
        }
        pieces.Add(rest.Map(x => x + 1));
        return pieces;
    }

    // Cut at random points. A small piece from far into a large list has a
    // Gap many times its size, which a split gives size tables instead
    // (NormalizeGap), so the whole is dense only when every piece is.
    [Test]
    public void RandomPiecesMergedBack([Values(1, 2, 3, 4)] int seed)
    {
        var rng = new Random(seed);
        for (var round = 0; round < 30; round++)
        {
            var n = rng.Next(40, 300_000);
            var (list, expected) = Dense(rng, n);
            var cuts = Enumerable.Range(0, rng.Next(1, 12)).Select(_ => rng.Next(0, n + 1)).Distinct().Order().ToList();
            MergeBack(Cut(list, cuts), expected.Select(x => x + 1).ToList(), $"seed {seed} round {round}: {cuts.Count + 1} pieces");
        }
    }

    // Equal chunks, as a parallel job cuts a vec: up to 32 of them have a Gap
    // at most 32 times their size, and stay dense, and so does the whole.
    [Test]
    public void ChunksMergedBack([Values(1, 2, 3)] int seed)
    {
        var rng = new Random(seed);
        foreach (var k in new[] { 2, 3, 4, 7, 8, 16, 24, 32 })
        {
            var n = rng.Next(1000, 400_000);
            var (list, expected) = Dense(rng, n);
            var cuts = Enumerable.Range(1, k - 1).Select(i => (int)((long)n * i / k)).ToList();
            var pieces = Cut(list, cuts);
            if (pieces.Any(piece => Tables(piece) != 0)) throw new Exception($"seed {seed}: {k} chunks of {n}: a chunk is not dense");
            MergeBack(pieces, expected.Select(x => x + 1).ToList(), $"seed {seed}: {k} chunks of {n}");
        }
    }
}
