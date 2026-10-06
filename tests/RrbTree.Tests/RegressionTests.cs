using Collections;

namespace rrbtests;

[TestFixture]
public class RegressionTests
{
    private static void AssertSame(IReadOnlyList<int> expected, RrbList<int> actual)
    {
        actual.VerifyIntegrity();
        Assert.That(actual.Count, Is.EqualTo(expected.Count));
        for (var i = 0; i < expected.Count; i++)
            Assert.That(actual[i], Is.EqualTo(expected[i]), $"index {i}");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(33)]
    [TestCase(65)]
    [TestCase(100)]
    [TestCase(1025)]
    [TestCase(40000)]
    public void PopFirst_DropsFirstElement(int size)
    {
        var expected = Enumerable.Range(0, size).ToList();
        var list = RrbList<int>.Create(expected);

        var popped = list.PopFirst();
        expected.RemoveAt(0);

        AssertSame(expected, popped);
    }

    [Test]
    public void PopFirst_Repeated_EmptiesList()
    {
        var expected = Enumerable.Range(0, 200).ToList();
        var list = RrbList<int>.Create(expected);

        while (expected.Count > 0)
        {
            list = list.PopFirst();
            expected.RemoveAt(0);
            AssertSame(expected, list);
        }
    }

    // 100 items are three leaves and a tail: one internal level.
    [TestCase(100, 1)]
    [TestCase(1000, 1)]
    [TestCase(2000, 2)]
    [TestCase(40_000, 3)]
    public void SlicePrefix_CollapsesHeight(int take, int height)
    {
        var list = RrbList<int>.Create(Enumerable.Range(0, 1_000_000).ToArray());
        var slice = list.Slice(0, take);

        AssertSame(Enumerable.Range(0, take).ToList(), slice);
        Assert.That(slice.ToString(), Does.Contain($"Height: {height})"));
    }

    [TestCase(1_000_000, 1000)]
    [TestCase(1_000_000, 1024)]
    [TestCase(1_000_000, 1056)]
    [TestCase(40_000, 70)]
    [TestCase(40_000, 96)]
    [TestCase(40_000, 33_000)]
    public void SlicePrefix_StaysUsable(int size, int take)
    {
        var list = RrbList<int>.Create(Enumerable.Range(0, size).ToArray());
        var expected = Enumerable.Range(0, take).ToList();
        var slice = list.Slice(0, take);
        AssertSame(expected, slice);

        // The collapsed tree must accept further appends and slices.
        for (var i = 0; i < 100; i++)
        {
            slice = slice.Add(-i);
            expected.Add(-i);
        }
        AssertSame(expected, slice);

        slice = slice.Slice(0, take / 2 + 1);
        AssertSame(expected.GetRange(0, take / 2 + 1), slice);
    }

    [Test]
    public void Builder_IndexesRelaxedChildUnderDenseNode()
    {
        var list = RrbList<int>.Empty;
        for (var i = 0; i < 32_972; i++) list = list.Add(i);

        var builder = list.ToBuilder();
        builder.Add(32_972);

        for (var i = 0; i < builder.Count; i++)
            Assert.That(builder[i], Is.EqualTo(i), $"index {i}");
    }

    [Test]
    public void Builder_FlushOntoPartFullLeafRoot()
    {
        var right = RrbList<int>.Create(Enumerable.Range(0, 40).ToArray()).Split(10).Right;
        var expected = Enumerable.Range(10, 30).ToList();
        AssertSame(expected, right);

        var builder = right.ToBuilder();
        var extra = Enumerable.Range(1000, 100).ToArray();
        builder.AddRange(extra);
        expected.AddRange(extra);

        for (var i = 0; i < expected.Count; i++)
            Assert.That(builder[i], Is.EqualTo(expected[i]), $"builder index {i}");

        AssertSame(expected, builder.ToImmutable());
    }

    [TestCase(10, 2000)]
    [TestCase(10, 40_000)]
    [TestCase(1000, 40_000)]
    public void Builder_FlushOntoPartFullTree(int drop, int add)
    {
        // Dropping from the front leaves a relaxed tree with a part-full first leaf.
        var list = RrbList<int>.Create(Enumerable.Range(0, 1100).ToArray()).Slice(drop, 1100 - drop);
        var expected = Enumerable.Range(drop, 1100 - drop).ToList();
        AssertSame(expected, list);

        var builder = list.ToBuilder();
        for (var i = 0; i < add; i++)
        {
            builder.Add(-i);
            expected.Add(-i);
        }

        AssertSame(expected, builder.ToImmutable());
    }
}
