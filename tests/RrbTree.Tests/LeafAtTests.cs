using Collections;

namespace rrbtests;

/// <summary>
///     <see cref="RrbList{T}.LeafAt" /> answers the leaf that holds an index, and a
///     walk from leaf to leaf with it reads every element once, in order. Both
///     are checked on lists with a tail only, with full trees, and with relaxed
///     trees, which merges, inserts and slices make.
/// </summary>
[TestFixture]
public class LeafAtTests
{
    private static RrbList<int> Numbers(int count)
    {
        var list = RrbList<int>.Empty;
        for (var i = 0; i < count; i++) list = list.Add(i);
        return list;
    }

    private static IEnumerable<(string, RrbList<int>)> Lists()
    {
        foreach (var n in new[] { 1, 5, 31, 32, 33, 64, 65, 1000, 1056, 40000 })
            yield return ($"{n} added", Numbers(n));

        yield return ("merged 37 and 1000", RrbFun.Merge(Numbers(37), Numbers(1000), false));
        yield return ("merged 1000 and 37", RrbFun.Merge(Numbers(1000), Numbers(37), false));

        var inserted = Numbers(2000);
        for (var i = 0; i < 50; i++) inserted = RrbFun.Insert(inserted, 17 + i * 31, -i);
        yield return ("2000 with 50 inserted", inserted);

        yield return ("slice of 5000", RrbFun.Slice(Numbers(5000), 123, 3000));
    }

    [Test]
    public void EachIndexFindsItsElement()
    {
        foreach (var (name, list) in Lists())
        {
            for (var i = 0; i < list.Count; i++)
            {
                var items = list.LeafAt(i, out var position, out var length);
                Assert.That(position, Is.InRange(0, length - 1), $"{name}, index {i}");
                Assert.That(length, Is.LessThanOrEqualTo(items.Length), $"{name}, index {i}");
                Assert.That(items[position], Is.EqualTo(list[i]), $"{name}, index {i}");
            }
        }
    }

    [Test]
    public void LeafToLeafReadsEachElementOnce()
    {
        foreach (var (name, list) in Lists())
        {
            var read = new List<int>();
            var index = 0;
            while (index < list.Count)
            {
                var items = list.LeafAt(index, out var position, out var length);
                for (var p = position; p < length; p++) read.Add(items[p]);
                index += length - position;
            }

            Assert.That(read, Is.EqualTo(list.ToList()), name);
        }
    }

    [Test]
    public void AnIndexOutsideTheListIsRefused()
    {
        var list = Numbers(10);
        Assert.Throws<IndexOutOfRangeException>(() => list.LeafAt(10, out _, out _));
        Assert.Throws<IndexOutOfRangeException>(() => list.LeafAt(-1, out _, out _));
        Assert.Throws<IndexOutOfRangeException>(() => RrbList<int>.Empty.LeafAt(0, out _, out _));
    }
}
