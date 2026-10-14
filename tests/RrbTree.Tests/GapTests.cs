using Collections;

namespace rrbtests;

// The Gap: a dense tree whose first slots are phantoms, so that front
// operations keep the tree dense. Every list here is checked against a
// List<int>, and for its shape.
[TestFixture]
public class GapTests
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

    private static void AssertDense(RrbList<int> l, string what = "")
    {
        Assert.That(l.ToString(), Does.Not.Contain("TABLE"), what + ": the tree has size tables");
        Assert.That(l.MaxSearchDistance(), Is.EqualTo(0), what);
    }

    private static RrbList<int> Prepended(int n)
    {
        var l = RrbList<int>.Empty;
        for (var i = n - 1; i >= 0; i--) l = l.Prepend(i);
        return l;
    }

    // Lists with a Gap, and what they hold.
    private static IEnumerable<(string, RrbList<int>, List<int>)> GapLists()
    {
        foreach (var n in new[] { 3000, 40_000 })
        {
            yield return ($"Prepended({n})", Prepended(n), Enumerable.Range(0, n).ToList());

            var popped = RrbList<int>.Create(Enumerable.Range(0, n).ToArray());
            for (var i = 0; i < 100; i++) popped = popped.PopFirst();
            yield return ($"Create({n}) then 100 x PopFirst", popped, Enumerable.Range(100, n - 100).ToList());

            var dropped = RrbList<int>.Create(Enumerable.Range(0, n).ToArray()).Slice(1000, n - 1000);
            yield return ($"Create({n}).Slice(1000, ...)", dropped, Enumerable.Range(1000, n - 1000).ToList());
        }
    }

    [TestCase(1_100)]
    [TestCase(40_000)]
    [TestCase(1_100_000)]
    public void Prepend_KeepsTheTreeDense(int n)
    {
        var l = Prepended(n);
        Check(Enumerable.Range(0, n).ToList(), l);
        AssertDense(l);
        Assert.That(l.ToString(), Does.Contain("Gap"));
    }

    [TestCase(40_000)]
    [TestCase(300_000)]
    public void PopFirst_KeepsTheTreeDense(int n)
    {
        var l = RrbList<int>.Create(Enumerable.Range(0, n).ToArray());
        var expectedStart = 0;
        while (l.Count > 0)
        {
            var step = Math.Max(1, l.Count / 7);
            for (var i = 0; i < step && l.Count > 0; i++)
            {
                Assert.That(l[0], Is.EqualTo(expectedStart));
                l = l.PopFirst();
                expectedStart++;
            }

            Check(Enumerable.Range(expectedStart, n - expectedStart).ToList(), l, $"after {expectedStart}");
            AssertDense(l, $"after {expectedStart}");
        }
    }

    [Test]
    public void DropsFromTheFront_KeepTheTreeDense()
    {
        var l = RrbList<int>.Create(Enumerable.Range(0, 50_000).ToArray());
        var expected = Enumerable.Range(0, 50_000).ToList();
        var rng = new Random(3);
        while (expected.Count > 100)
        {
            var k = rng.Next(1, Math.Min(5000, expected.Count));
            l = l.Slice(k, l.Count - k);
            expected.RemoveRange(0, k);
            Check(expected, l, $"drop {k}");
            AssertDense(l, $"drop {k}");
        }
    }

    [Test]
    public void Deque_StaysDense()
    {
        var l = RrbList<int>.Empty;
        var expected = new LinkedList<int>();
        var rng = new Random(11);
        for (var i = 0; i < 100_000; i++)
        {
            switch (rng.Next(5))
            {
                case 0:
                case 1:
                    l = l.Prepend(i);
                    expected.AddFirst(i);
                    break;
                case 2:
                    l = l.Add(i);
                    expected.AddLast(i);
                    break;
                case 3:
                    if (expected.Count == 0) break;
                    l = l.PopFirst();
                    expected.RemoveFirst();
                    break;
                default:
                    if (expected.Count == 0) break;
                    l = l.Pop();
                    expected.RemoveLast();
                    break;
            }

            if (i % 5000 == 0)
            {
                Check(expected.ToList(), l, $"step {i}");
                AssertDense(l, $"step {i}");
            }
        }

        Check(expected.ToList(), l);
        AssertDense(l);
    }

    [Test]
    public void SetItem_And_Add_KeepTheGap()
    {
        foreach (var (name, start, items) in GapLists())
        {
            var l = start;
            var expected = items.ToList();
            var rng = new Random(5);
            for (var i = 0; i < 300; i++)
            {
                var at = rng.Next(expected.Count);
                l = l.SetItem(at, -i);
                expected[at] = -i;
                l = l.Add(i);
                expected.Add(i);
            }

            Check(expected, l, name);
            AssertDense(l, name);
        }
    }

    [Test]
    public void ReadPaths_OnAGap()
    {
        foreach (var (name, l, expected) in GapLists())
        {
            Assert.That(l.Fold(0L, (x, s) => s * 31 + x), Is.EqualTo(expected.Aggregate(0L, (s, x) => s * 31 + x)), name);

            var order = new List<int>();
            l.Iter(x => { order.Add(x); return true; });
            Assert.That(order, Is.EqualTo(expected), name + " Iter");

            var mapped = l.Map(x => x * 2);
            Check(expected.Select(x => x * 2).ToList(), mapped, name + " Map");

            Assert.That(l.Reverse().ToArray(), Is.EqualTo(Enumerable.Reverse(expected).ToArray()), name + " reverse");

            foreach (var (index, count) in new[] { (0, 10), (5, 900), (40, 1000), (expected.Count - 50, 50), (0, expected.Count) })
            {
                var seen = new List<int>();
                l.ForEach(seen.Add, index, count);
                Assert.That(seen, Is.EqualTo(expected.GetRange(index, count)), $"{name} ForEach({index}, {count})");

                var arr = new int[count];
                l.CopyTo(index, arr, 0, count);
                Assert.That(arr, Is.EqualTo(expected.GetRange(index, count).ToArray()), $"{name} CopyTo({index}, {count})");

                var back = new List<int>();
                var r = l.ReverseEnumerator(index + count - 1, count);
                while (r.MoveNext()) back.Add(r.Current);
                back.Reverse();
                Assert.That(back, Is.EqualTo(expected.GetRange(index, count)), $"{name} ReverseEnumerator");
            }

            for (var i = 0; i < expected.Count;)
            {
                var items = l.LeafAt(i, out var position, out var length);
                for (var p = position; p < length; p++, i++)
                    if (items[p] != expected[i])
                        Assert.Fail($"{name}: LeafAt walk at {i} is {items[p]}, expected {expected[i]}");
            }
        }
    }

    [Test]
    public void MiddleOperations_OnAGap()
    {
        foreach (var (name, l, expected) in GapLists())
        {
            var mid = expected.Count / 2;

            var ins = expected.ToList();
            ins.Insert(mid, -1);
            Check(ins, l.Insert(mid, -1), name + " Insert");

            var rem = expected.ToList();
            rem.RemoveAt(mid);
            Check(rem, l.RemoveAt(mid), name + " RemoveAt");

            var (left, right) = l.Split(mid);
            Check(expected.GetRange(0, mid), left, name + " Split.Left");
            Check(expected.GetRange(mid, expected.Count - mid), right, name + " Split.Right");

            Check(expected.GetRange(100, 700), l.Slice(100, 700), name + " Slice middle");
            Check(expected.GetRange(0, mid), l.Slice(0, mid), name + " Slice take");

            Check(expected.Concat(expected).ToList(), l.Merge(l), name + " Merge self");
            Check(expected.Concat([1, 2, 3]).ToList(), l.Merge(RrbList<int>.Create([1, 2, 3])), name + " Merge small");
            var other = Prepended(2000);
            Check(expected.Concat(Enumerable.Range(0, 2000)).ToList(), l.Merge(other), name + " Merge gap + gap");
            Check(Enumerable.Range(0, 2000).Concat(expected).ToList(), other.Merge(l), name + " Merge gap + gap, other way");

            var b = l.ToBuilder();
            b.Add(-7);
            b[3] = -3;
            var built = expected.ToList();
            built.Add(-7);
            built[3] = -3;
            Check(built, b.ToImmutable(), name + " ToBuilder");
            Check(expected, l, name + " source list");

            var popped = l;
            var exp = expected.ToList();
            for (var i = 0; i < 100; i++)
            {
                popped = popped.Pop();
                exp.RemoveAt(exp.Count - 1);
            }

            Check(exp, popped, name + " Pop x 100");
            AssertDense(popped, name + " Pop x 100");
        }
    }

    [Test]
    public void Equality_IgnoresTheGap()
    {
        foreach (var (name, l, expected) in GapLists())
        {
            var plain = RrbList<int>.Create(expected.ToArray());
            Assert.That(l, Is.EqualTo(plain), name);
            Assert.That(l.GetHashCode(), Is.EqualTo(plain.GetHashCode()), name);
        }
    }
}
