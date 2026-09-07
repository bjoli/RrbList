using Collections;

namespace rrbtests;

/// <summary>
/// Walking backwards, and backwards over part of a list.
///
/// Two things went wrong here while nothing used the range overloads. The hot
/// path stopped at the leaf's start rather than at the range's, so a walk that
/// ended inside a leaf ran on and yielded elements outside the range; and a
/// walk that left the tail for the tree advanced a stack that had never been
/// built, because "am I in the tree" was asked as "are there items", and the
/// tail is items.
///
/// Both need a list long enough to have a tree *and* a tail, which is why the
/// sizes below are what they are: 100 is three full leaves and a tail of four.
/// </summary>
[TestFixture]
public class ReverseEnumeratorTests
{
    private static RrbList<int> Numbers(int count)
    {
        var list = RrbList<int>.Empty;
        for (var i = 0; i < count; i++) list = list.Add(i);
        return list;
    }

    private static List<int> Drain<T>(RrbReverseEnumerator<int> e)
    {
        var got = new List<int>();
        while (e.MoveNext()) got.Add(e.Current);
        return got;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(100)]
    [TestCase(1000)]
    public void WholeList(int count)
    {
        var expected = Enumerable.Range(0, count).Reverse().ToList();
        Assert.That(Drain<int>(Numbers(count).ReverseEnumerator()), Is.EqualTo(expected));
    }

    [Test]
    public void RangeInsideOneLeaf()
    {
        // Ends inside a leaf, which is what the hot path used to run past.
        var e = new RrbReverseEnumerator<int>(Numbers(5), 2, 2);
        Assert.That(Drain<int>(e), Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public void RangeOutInTheTree()
    {
        var e = new RrbReverseEnumerator<int>(Numbers(100), 44, 5);
        Assert.That(Drain<int>(e), Is.EqualTo(new[] { 44, 43, 42, 41, 40 }));
    }

    [Test]
    public void RangeCrossingFromTailIntoTree()
    {
        // 100 items: the tail holds 96..99, so this starts in the tail and
        // continues in the tree.
        var e = new RrbReverseEnumerator<int>(Numbers(100), 97, 4);
        Assert.That(Drain<int>(e), Is.EqualTo(new[] { 97, 96, 95, 94 }));
    }

    [Test]
    public void RangeCrossingALeafBoundary()
    {
        var e = new RrbReverseEnumerator<int>(Numbers(100), 33, 4);
        Assert.That(Drain<int>(e), Is.EqualTo(new[] { 33, 32, 31, 30 }));
    }

    [Test]
    public void EmptyRange()
    {
        Assert.That(Drain<int>(new RrbReverseEnumerator<int>(Numbers(10), 4, 0)), Is.Empty);
        Assert.That(Drain<int>(new RrbReverseEnumerator<int>(Numbers(0), -1, 0)), Is.Empty);
    }

    [Test]
    public void EveryRangeOfALongList()
    {
        // Every start and every length, against what the list says by index.
        var list = Numbers(100);

        for (var last = 0; last < 100; last++)
        for (var count = 0; count <= last + 1; count++)
        {
            var expected = new List<int>();
            for (var i = last; i > last - count; i--) expected.Add(list[i]);

            Assert.That(Drain<int>(new RrbReverseEnumerator<int>(list, last, count)),
                Is.EqualTo(expected),
                $"last={last} count={count}");
        }
    }

    [Test]
    public void ResetWalksItAgain()
    {
        var e = new RrbReverseEnumerator<int>(Numbers(100), 44, 5);
        var first = Drain<int>(e);
        e.Reset();
        Assert.That(Drain<int>(e), Is.EqualTo(first));
    }
}
