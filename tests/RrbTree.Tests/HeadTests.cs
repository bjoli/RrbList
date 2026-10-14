using Collections;

namespace rrbtests;

// The head: Prepend, PopFirst, and every operation on lists that have one.
[TestFixture]
public class HeadTests
{
    private static void Check(IReadOnlyList<int> expected, RrbList<int> actual, string what = "")
    {
        actual.VerifyIntegrity();
        Assert.That(actual.Count, Is.EqualTo(expected.Count), what + " count");
        for (var i = 0; i < expected.Count; i++)
            if (actual[i] != expected[i])
                Assert.Fail($"{what}: index {i} is {actual[i]}, expected {expected[i]}");
        Assert.That(actual.ToArray(), Is.EqualTo(expected.ToArray()), what + " enumerator");
        DifferentialFuzz.CheckWear(actual, what);
    }

    // A list of 0..n-1 built by prepending, so it has a head and a relaxed left spine.
    private static RrbList<int> Prepended(int n)
    {
        var l = RrbList<int>.Empty;
        for (var i = n - 1; i >= 0; i--) l = l.Prepend(i);
        return l;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(1024)]
    [TestCase(1057)]
    [TestCase(33_000)]
    [TestCase(1_100_000)]
    public void Prepend_BuildsTheList(int n)
    {
        var l = Prepended(n);
        Check(Enumerable.Range(0, n).ToList(), l, $"Prepended({n})");
    }

    [Test]
    public void Prepend_EveryStepKeepsIntegrity()
    {
        var l = RrbList<int>.Empty;
        var expected = new List<int>();
        for (var i = 0; i < 5000; i++)
        {
            l = l.Prepend(i);
            expected.Insert(0, i);
            l.VerifyIntegrity();
            Assert.That(l[0], Is.EqualTo(i));
            Assert.That(l[l.Count - 1], Is.EqualTo(0));
        }

        Check(expected, l);
    }

    [Test]
    public void Prepend_KeepsTheSearchStep()
    {
        // Onto dense, relaxed, and prepend-built trees of several heights.
        foreach (var start in new[]
                 {
                     RrbList<int>.Create(Enumerable.Range(0, 40_000).ToArray()),
                     RrbList<int>.Create(Enumerable.Range(0, 40_100).ToArray()).Slice(77, 40_000),
                     Prepended(40_000)
                 })
        {
            var l = start;
            for (var i = 0; i < 70_000; i++)
            {
                l = l.Prepend(-i);
                if (i % 997 == 0) l.VerifySearchStep();
            }

            l.VerifySearchStep();
            l.VerifyIntegrity();
            Assert.That(l.MaxSearchDistance(), Is.LessThanOrEqualTo(8));
        }
    }

    [TestCase(1)]
    [TestCase(33)]
    [TestCase(1057)]
    [TestCase(40_000)]
    public void PopFirst_EmptiesTheList(int n)
    {
        foreach (var start in new[] { Prepended(n), RrbList<int>.Create(Enumerable.Range(0, n).ToArray()) })
        {
            var l = start;
            for (var i = 0; i < n; i++)
            {
                Assert.That(l[0], Is.EqualTo(i));
                l = l.PopFirst();
                if (i % 101 == 0 || l.Count < 70) l.VerifyIntegrity();
                Assert.That(l.Count, Is.EqualTo(n - i - 1));
            }

            Assert.That(l.Count, Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() => l.PopFirst());
        }
    }

    [Test]
    public void Pop_ThroughTheHead()
    {
        var l = Prepended(100);
        for (var i = 99; i >= 0; i--)
        {
            Assert.That(l[l.Count - 1], Is.EqualTo(i));
            l = l.Pop();
            Check(Enumerable.Range(0, i).ToList(), l, $"Pop to {i}");
        }
    }

    [Test]
    public void SetItem_InHeadTreeAndTail()
    {
        var l = Prepended(3000).Add(3000).Add(3001);
        var expected = Enumerable.Range(0, 3002).ToList();
        foreach (var at in new[] { 0, 1, 5, 20, 21, 31, 32, 100, 2999, 3000, 3001 })
        {
            l = l.SetItem(at, -at - 1);
            expected[at] = -at - 1;
        }

        Check(expected, l);
    }

    [Test]
    public void Insert_And_RemoveAt_AroundTheHead()
    {
        var l = Prepended(2000);
        var expected = Enumerable.Range(0, 2000).ToList();
        var rng = new Random(5);
        for (var i = 0; i < 3000; i++)
        {
            var at = rng.Next(Math.Min(expected.Count, 70) + 1);
            if (rng.Next(2) == 0 || at == expected.Count)
            {
                l = l.Insert(at, -i);
                expected.Insert(at, -i);
            }
            else
            {
                l = l.RemoveAt(at);
                expected.RemoveAt(at);
            }

            l.VerifyIntegrity();
        }

        Check(expected, l);
    }

    [Test]
    public void Insert_IntoAFullHead()
    {
        // 32 prepends past a leaf boundary leave a full head.
        var l = RrbList<int>.Create(Enumerable.Range(1000, 100).ToArray());
        var expected = Enumerable.Range(1000, 100).ToList();
        for (var i = 0; i < 32; i++)
        {
            l = l.Prepend(i);
            expected.Insert(0, i);
        }

        for (var at = 1; at <= 32; at++)
        {
            var copy = l.Insert(at, -1);
            var exp = expected.ToList();
            exp.Insert(at, -1);
            Check(exp, copy, $"Insert({at})");
        }
    }

    [Test]
    public void Split_AtEveryIndex()
    {
        var l = Prepended(1100).Merge(RrbList<int>.Create(Enumerable.Range(1100, 50).ToArray()));
        var expected = Enumerable.Range(0, 1150).ToList();
        for (var at = 0; at <= l.Count; at++)
        {
            var (left, right) = l.Split(at);
            Check(expected.GetRange(0, at), left, $"Split({at}).Left");
            Check(expected.GetRange(at, expected.Count - at), right, $"Split({at}).Right");
        }
    }

    [Test]
    public void Slice_EveryStartAndSomeCounts()
    {
        var l = Prepended(1200);
        var expected = Enumerable.Range(0, 1200).ToList();
        for (var s = 0; s < 80; s++)
        foreach (var c in new[] { 0, 1, 31, 32, 33, 64, 65, 100, 1000, 1200 - s - 1, 1200 - s })
        {
            if (c < 0 || s + c > 1200) continue;
            Check(expected.GetRange(s, c), l.Slice(s, c), $"Slice({s}, {c})");
        }
    }

    [Test]
    public void Merge_WithHeadsOnBothSides()
    {
        foreach (var a in new[] { 0, 1, 20, 40, 100, 1057, 5000 })
        foreach (var b in new[] { 0, 1, 20, 40, 100, 1057, 5000 })
        foreach (var headB in new[] { 0, 1, 17, 32 })
        {
            // Left with a head; right with a head of headB, made by prepending.
            var left = Prepended(a);
            var right = RrbList<int>.Create(Enumerable.Range(a + headB, b).ToArray());
            for (var i = headB - 1; i >= 0; i--) right = right.Prepend(a + i);

            var merged = left.Merge(right);
            Check(Enumerable.Range(0, a + headB + b).ToList(), merged, $"Merge({a}, {headB}+{b})");
        }
    }

    [Test]
    public void ReadPaths_SeeTheHead()
    {
        var l = Prepended(1000).Add(1000);
        var expected = Enumerable.Range(0, 1001).ToList();

        Assert.That(l.Fold(0L, (x, s) => s * 31 + x), Is.EqualTo(expected.Aggregate(0L, (s, x) => s * 31 + x)));
        Assert.That(l.Map(x => x * 2).ToArray(), Is.EqualTo(expected.Select(x => x * 2).ToArray()));
        Assert.That(l.Filter(x => x % 3 == 0).ToArray(), Is.EqualTo(expected.Where(x => x % 3 == 0).ToArray()));
        Assert.That(l.Find(x => x > 5), Is.EqualTo(6));
        Assert.That(l.Contains(3), Is.True);
        Assert.That(l.IndexOf(7, 0, l.Count, null), Is.EqualTo(7));
        Assert.That(l.LastIndexOf(7, l.Count - 1, l.Count, null), Is.EqualTo(7));
        Assert.That(l.Reverse().ToArray(), Is.EqualTo(Enumerable.Reverse(expected).ToArray()));

        var order = new List<int>();
        l.Iter(x => { order.Add(x); return true; });
        Assert.That(order, Is.EqualTo(expected));

        var mapOrder = new List<int>();
        l.Map(x => { mapOrder.Add(x); return x; });
        Assert.That(mapOrder, Is.EqualTo(expected), "Map calls its mapper in order");

        foreach (var (index, count) in new[] { (0, 1001), (0, 5), (3, 10), (10, 40), (20, 900), (999, 2) })
        {
            var seen = new List<int>();
            l.ForEach(seen.Add, index, count);
            Assert.That(seen, Is.EqualTo(expected.GetRange(index, count)), $"ForEach({index}, {count})");

            var arr = new int[count];
            l.CopyTo(index, arr, 0, count);
            Assert.That(arr, Is.EqualTo(expected.GetRange(index, count).ToArray()), $"CopyTo({index}, {count})");

            var ranged = new List<int>();
            var e = l.RangeEnumerator(index, count);
            while (e.MoveNext()) ranged.Add(e.Current);
            Assert.That(ranged, Is.EqualTo(expected.GetRange(index, count)), $"RangeEnumerator({index}, {count})");

            var back = new List<int>();
            var r = l.ReverseEnumerator(index + count - 1, count);
            while (r.MoveNext()) back.Add(r.Current);
            back.Reverse();
            Assert.That(back, Is.EqualTo(expected.GetRange(index, count)), $"ReverseEnumerator({index + count - 1}, {count})");
        }
    }

    [Test]
    public void Builder_FromAListWithAHead()
    {
        var l = Prepended(1500);
        var b = l.ToBuilder();
        for (var i = 1500; i < 3000; i++) b.Add(i);
        b[3] = -3;

        var expected = Enumerable.Range(0, 3000).ToList();
        expected[3] = -3;
        Check(expected, b.ToImmutable());
        Check(Enumerable.Range(0, 1500).ToList(), l, "source list");
    }

    [Test]
    public void Equality_IgnoresTheShape()
    {
        var a = Prepended(2000);
        var b = RrbList<int>.Create(Enumerable.Range(0, 2000).ToArray());
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Deconstruct_FromTheFront()
    {
        var l = Prepended(500);
        var expected = Enumerable.Range(0, 500).ToList();
        while (l is (var x, var y, { } rest) && rest.Count > 0)
        {
            Assert.That(x, Is.EqualTo(expected[0]));
            Assert.That(y, Is.EqualTo(expected[1]));
            expected.RemoveRange(0, 2);
            Check(expected, rest);
            l = rest;
        }
    }

    [TestCase(65)]
    [TestCase(1000)]
    [TestCase(33_000)]
    [TestCase(100_000)]
    public void Create_GivesAHeadAndADenseTree(int n)
    {
        var l = RrbList<int>.Create(Enumerable.Range(0, n).ToArray());
        Check(Enumerable.Range(0, n).ToList(), l);
        Assert.That(l.ToString(), Does.Contain("[Head]"), "a list from a builder has a head");
        Assert.That(l.ToString(), Does.Not.Contain("TABLE"), "and a tree without size tables");
        Assert.That(l.MaxSearchDistance(), Is.EqualTo(0));
    }

    [Test]
    public void Builder_SetItemInTheHead_LeavesEarlierListsAlone()
    {
        var b = new RrbBuilder<int>();
        for (var i = 0; i < 200; i++) b.Add(i);
        var first = b.ToImmutable();

        b[0] = -1;
        b[31] = -31;
        b.Add(200);
        var second = b.ToImmutable();

        Check(Enumerable.Range(0, 200).ToList(), first, "first list");
        var expected = Enumerable.Range(0, 201).ToList();
        expected[0] = -1;
        expected[31] = -31;
        Check(expected, second, "second list");
        Assert.That(b[0], Is.EqualTo(-1));
        Assert.That(b[31], Is.EqualTo(-31));
        Assert.That(b[32], Is.EqualTo(32));
    }

    [Test]
    public void Builder_FromAListWithAPartHead()
    {
        // A head of 5, from popping a builder-made list.
        var l = RrbList<int>.Create(Enumerable.Range(0, 3000).ToArray());
        for (var i = 0; i < 27; i++) l = l.PopFirst();

        var b = l.ToBuilder();
        b[2] = -2;
        b[100] = -100;
        for (var i = 3000; i < 3100; i++) b.Add(i);

        var expected = Enumerable.Range(27, 3073).ToList();
        expected[2] = -2;
        expected[100] = -100;
        Check(expected, b.ToImmutable());
        Check(Enumerable.Range(27, 2973).ToList(), l, "source list");
    }

    [Test]
    public void Deconstruct_AllArities_FromHeadTreeAndTail()
    {
        foreach (var n in new[] { 1, 6, 31, 33, 64, 65, 100, 1100 })
        foreach (var start in new[] { RrbList<int>.Create(Enumerable.Range(0, n).ToArray()), Prepended(n) })
        {
            var expected = Enumerable.Range(0, n).ToList();
            var l = start;
            var step = 0;
            while (expected.Count > 0)
            {
                var k = step++ % 6 + 1;
                if (expected.Count < k) k = 1;
                RrbList<int>? rest;
                int[] got;
                switch (k)
                {
                    case 1: { var (a, r) = l; got = [a]; rest = r; break; }
                    case 2: { var (a, b, r) = l; got = [a, b]; rest = r; break; }
                    case 3: { var (a, b, c, r) = l; got = [a, b, c]; rest = r; break; }
                    case 4: { var (a, b, c, d, r) = l; got = [a, b, c, d]; rest = r; break; }
                    case 5: { var (a, b, c, d, e, r) = l; got = [a, b, c, d, e]; rest = r; break; }
                    default: { var (a, b, c, d, e, f, r) = l; got = [a, b, c, d, e, f]; rest = r; break; }
                }

                Assert.That(got, Is.EqualTo(expected.GetRange(0, k).ToArray()), $"n {n}, take {k}");
                expected.RemoveRange(0, k);
                Assert.That(rest, Is.Not.Null);
                rest!.VerifyIntegrity();
                Assert.That(rest.Count, Is.EqualTo(expected.Count));
                if (expected.Count > 0) Assert.That(rest[0], Is.EqualTo(expected[0]));
                l = rest;
            }
        }

        // Too short: null rest.
        var (x, y, none) = RrbList<int>.Create([1]);
        Assert.That(none, Is.Null);
    }

    [Test]
    public void PrependRange_PutsItemsInFront()
    {
        var l = Prepended(100).PrependRange(Enumerable.Range(-50, 50));
        Check(Enumerable.Range(-50, 150).ToList(), l);
    }
}
