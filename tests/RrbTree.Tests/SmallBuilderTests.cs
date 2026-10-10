using Collections;

namespace rrbtests;

/// <summary>
///     A small builder grows its tail from 4 up to a leaf. What it builds must be what the
///     default builder builds, for every count and every way of adding.
/// </summary>
[TestFixture]
public class SmallBuilderTests
{
    private static List<int> Read(RrbList<int> list)
    {
        var seen = new List<int>();
        for (var i = 0; i < list.Count; i++) seen.Add(list[i]);
        return seen;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(16)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(100)]
    [TestCase(5000)]
    public void BuildsWhatTheDefaultBuilds(int n)
    {
        var small = RrbBuilder<int>.Small();
        var normal = new RrbBuilder<int>();
        for (var i = 0; i < n; i++)
        {
            small.Add(i);
            normal.Add(i);
        }

        var a = small.ToImmutable();
        var b = normal.ToImmutable();
        Assert.That(a.Count, Is.EqualTo(n));
        Assert.That(Read(a), Is.EqualTo(Read(b)));
        Assert.That(a, Is.EqualTo(Enumerable.Range(0, n)));
    }

    [TestCase(4)]
    [TestCase(8)]
    [TestCase(32)]
    public void AListItHandedItsTailIsNotChangedByLaterAdds(int n)
    {
        var small = RrbBuilder<int>.Small();
        for (var i = 0; i < n; i++) small.Add(i);
        var first = small.ToImmutable();

        for (var i = 0; i < 40; i++) small.Add(1000 + i);
        small.SetItem(0, -1);
        var second = small.ToImmutable();

        Assert.That(first, Is.EqualTo(Enumerable.Range(0, n)));
        Assert.That(second.Count, Is.EqualTo(n + 40));
        Assert.That(second[0], Is.EqualTo(-1));
        Assert.That(second[n], Is.EqualTo(1000));
    }

    [Test]
    public void SetAndReadWhileGrowing()
    {
        var small = RrbBuilder<int>.Small();
        for (var i = 0; i < 20; i++)
        {
            small.Add(i);
            small.SetItem(i, i * 10);
            Assert.That(small[i], Is.EqualTo(i * 10));
        }

        Assert.That(small.ToImmutable(), Is.EqualTo(Enumerable.Range(0, 20).Select(i => i * 10)));
    }

    [Test]
    public void AddRangeIntoAGrowingTail()
    {
        var small = RrbBuilder<int>.Small();
        small.Add(0);
        small.Add(1);
        small.AddRange(Enumerable.Range(2, 50).ToArray());
        small.Add(52);
        Assert.That(small.ToImmutable(), Is.EqualTo(Enumerable.Range(0, 53)));
    }
}
