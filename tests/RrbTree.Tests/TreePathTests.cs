using Collections;

namespace rrbtests;

/// <summary>
///     A walk that seeks a <see cref="TreePath{T}" /> once, steps it from leaf to
///     leaf, and uses LeafAt for the head and tail, reads each element once, in
///     order, from any start. Checked on dense trees, on relaxed trees from
///     merges, inserts and slices, and on one relaxed on two levels.
/// </summary>
[TestFixture]
public class TreePathTests
{
    private static RrbList<int> Numbers(int count) => RrbList<int>.Create(Enumerable.Range(0, count).ToArray());

    private static IEnumerable<(string, RrbList<int>)> Lists()
    {
        foreach (var n in new[] { 1, 33, 1000, 40000 })
            yield return ($"{n}", Numbers(n));

        yield return ("merged 37 and 1000", RrbFun.Merge(Numbers(37), Numbers(1000), false));

        var inserted = Numbers(2000);
        for (var i = 0; i < 50; i++) inserted = RrbFun.Insert(inserted, 17 + i * 31, -i);
        yield return ("2000 with 50 inserted", inserted);

        yield return ("slice of 5000", RrbFun.Slice(Numbers(5000), 123, 3000));

        var all = Numbers(40000);
        var joined = RrbList<int>.Empty;
        for (var k = 0; k < 40; k++) joined = RrbFun.Merge(joined, RrbFun.Slice(all, k * 997, 997), false);
        yield return ("40 pieces of 997", joined);
    }

    // The walk VecCursor makes: a path once in the tree, LeafAt elsewhere.
    private static List<int> Walk(RrbList<int> list, int from)
    {
        var read = new List<int>();
        TreePath<int>? path = null;
        var index = from;
        while (index < list.Count)
        {
            int position = 0, length = 0;
            var items = path?.Step(index, out position, out length);
            if (items == null)
            {
                if (list.InTree(index))
                {
                    path ??= new TreePath<int>(list);
                    items = path.Seek(index, out position, out length);
                }
                else
                {
                    items = list.LeafAt(index, out position, out length);
                }
            }

            for (var p = position; p < length; p++) read.Add(items[p]);
            index += length - position;
        }

        return read;
    }

    [Test]
    public void AWalkReadsEachElementOnce()
    {
        foreach (var (name, list) in Lists())
        foreach (var from in new[] { 0, 1, 31, 32, 33, 500, 1025, 20001 }.Where(f => f < list.Count))
            Assert.That(Walk(list, from), Is.EqualTo(list.Skip(from).ToList()), $"{name} from {from}");
    }

    [Test]
    public void SeekFindsTheLeafOfEachIndex()
    {
        foreach (var (name, list) in Lists())
        {
            var path = new TreePath<int>(list);
            for (var i = 0; i < list.Count; i++)
            {
                if (!list.InTree(i)) continue;
                var items = path.Seek(i, out var position, out var length);
                Assert.That(position, Is.InRange(0, length - 1), $"{name}, index {i}");
                Assert.That(items[position], Is.EqualTo(list[i]), $"{name}, index {i}");
            }
        }
    }

    [Test]
    public void StepRefusesAnIndexOffThePath()
    {
        var list = RrbFun.Merge(Numbers(37), Numbers(1000), false);
        var path = new TreePath<int>(list);
        var first = 0;
        while (!list.InTree(first)) first++;
        path.Seek(first, out var position, out var length);
        var next = first - position + length;
        Assert.That(path.Step(next + 1, out _, out _), Is.Null);
        Assert.That(path.Step(next, out _, out _), Is.Not.Null);
    }

    [Test]
    public void JoinsMakeRelaxedTrees()
    {
        Assert.That(Numbers(40000).TreeIsRelaxed, Is.False);
        var all = Numbers(40000);
        var joined = RrbList<int>.Empty;
        for (var k = 0; k < 40; k++) joined = RrbFun.Merge(joined, RrbFun.Slice(all, k * 997, 997), false);
        Assert.That(joined.TreeIsRelaxed, Is.True);
    }
}
