using Collections;

namespace rrbtests;

// The builder starts with nothing and grows its tail and its leaf chunk as
// items come, and hands its tail to the list ToImmutable makes when that tail
// is exactly full. These check the sizes around every step of that growth,
// and that a list once made never changes, whatever the builder does next.
[TestFixture]
public class BuilderGrowthTests
{
    private static void AssertHolds(RrbList<int> list, IReadOnlyList<int> expected)
    {
        Assert.That(list.Count, Is.EqualTo(expected.Count));
        for (var i = 0; i < expected.Count; i++)
            Assert.That(list[i], Is.EqualTo(expected[i]), $"index {i} of {expected.Count}");
        Assert.That(list.ToArray(), Is.EqualTo(expected.ToArray()));
    }

    private static int[] Upto(int n) => Enumerable.Range(0, n).ToArray();

    private static readonly int[] Sizes =
        Enumerable.Range(0, 70)
            .Concat([127, 128, 129, 1023, 1024, 1025, 16383, 16384, 16385, 16416, 20000, 70000])
            .ToArray();

    [Test]
    public void An_empty_builder_makes_an_empty_list_and_can_go_on()
    {
        var builder = new RrbBuilder<int>();
        Assert.That(builder.ToImmutable().Count, Is.EqualTo(0));
        builder.Add(7);
        AssertHolds(builder.ToImmutable(), [7]);
    }

    [TestCaseSource(nameof(Sizes))]
    public void Added_one_at_a_time(int n)
    {
        var builder = new RrbBuilder<int>();
        for (var i = 0; i < n; i++) builder.Add(i);

        Assert.That(builder.Count, Is.EqualTo(n));
        for (var i = 0; i < n; i += Math.Max(1, n / 100))
            Assert.That(builder[i], Is.EqualTo(i));

        AssertHolds(builder.ToImmutable(), Upto(n));
    }

    [TestCaseSource(nameof(Sizes))]
    public void Added_as_one_range(int n)
    {
        var builder = new RrbBuilder<int>();
        builder.AddRange(Upto(n));
        AssertHolds(builder.ToImmutable(), Upto(n));
    }

    [TestCaseSource(nameof(Sizes))]
    public void Made_from_an_array_and_from_a_span(int n)
    {
        AssertHolds(RrbBuilder<int>.FromArray(Upto(n)), Upto(n));
        AssertHolds(RrbBuilder<int>.FromSpan(Upto(n)), Upto(n));
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(8)]
    public void A_small_tail_handed_to_a_list_is_not_changed_by_the_builder(int n)
    {
        var builder = new RrbBuilder<int>();
        builder.AddRange(Upto(n));
        var first = builder.ToImmutable();

        builder[0] = 100;
        var second = builder.ToImmutable();
        builder.Add(n);
        var third = builder.ToImmutable();

        AssertHolds(first, Upto(n));
        AssertHolds(second, [100, .. Upto(n)[1..]]);
        AssertHolds(third, [100, .. Upto(n)[1..], n]);
    }

    [Test]
    public void A_full_tail_handed_to_a_list_becomes_a_leaf_the_builder_does_not_write_to()
    {
        var builder = new RrbBuilder<int>();
        for (var i = 0; i < 32; i++) builder.Add(i);
        var first = builder.ToImmutable();

        builder.Add(32);
        builder[5] = -5;
        var second = builder.ToImmutable();

        AssertHolds(first, Upto(32));
        var expected = Upto(33);
        expected[5] = -5;
        AssertHolds(second, expected);
    }

    [Test]
    public void A_list_with_a_tree_is_not_changed_by_the_builder()
    {
        var builder = new RrbBuilder<int>();
        builder.AddRange(Upto(100));
        var first = builder.ToImmutable();

        builder.AddRange(Enumerable.Range(100, 50).ToArray());
        var second = builder.ToImmutable();

        builder[10] = -10;
        builder[99] = -99;
        builder[140] = -140;
        var third = builder.ToImmutable();

        AssertHolds(first, Upto(100));
        AssertHolds(second, Upto(150));
        var expected = Upto(150);
        expected[10] = -10;
        expected[99] = -99;
        expected[140] = -140;
        AssertHolds(third, expected);
    }

    [TestCase(5)]
    [TestCase(32)]
    [TestCase(40)]
    [TestCase(1000)]
    public void A_builder_from_a_list_does_not_change_it(int n)
    {
        var list = RrbBuilder<int>.FromArray(Upto(n));
        var builder = list.ToBuilder();
        builder[n - 1] = -1;
        builder[0] = -2;
        builder.Add(n);

        AssertHolds(list, Upto(n));
        var expected = Upto(n + 1);
        expected[n - 1] = -1;
        expected[0] = -2;
        AssertHolds(builder.ToImmutable(), expected);
    }

    // Adds, ranges, sets and snapshots in a random order, against a List<int>
    // doing the same. Every snapshot must still hold what the list held when
    // it was taken.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void Mixed_operations_against_a_model(int seed)
    {
        var random = new Random(seed);
        var builder = new RrbBuilder<int>();
        var model = new List<int>();
        var snapshots = new List<(RrbList<int> List, int[] Expected)>();
        var next = 0;

        for (var step = 0; step < 3000; step++)
        {
            switch (random.Next(10))
            {
                case < 5:
                    builder.Add(next);
                    model.Add(next++);
                    break;
                case 5:
                    var range = Enumerable.Range(next, random.Next(0, 70)).ToArray();
                    next += range.Length;
                    builder.AddRange(range);
                    model.AddRange(range);
                    break;
                case 6 or 7:
                    if (model.Count == 0) break;
                    var at = random.Next(model.Count);
                    builder[at] = -next;
                    model[at] = -next++;
                    break;
                default:
                    snapshots.Add((builder.ToImmutable(), model.ToArray()));
                    break;
            }
        }

        snapshots.Add((builder.ToImmutable(), model.ToArray()));
        foreach (var (list, expected) in snapshots)
            AssertHolds(list, expected);
    }
}
