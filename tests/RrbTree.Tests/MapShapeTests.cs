using System.Text.RegularExpressions;
using Collections;

namespace rrbtests;

// MapShape makes Map's list from items made elsewhere: the same items as a
// Map would give, the same shape, and a split's halves still merge dense.
[TestFixture]
public class MapShapeTests
{
    private static int Tables(RrbList<int> list) => Regex.Matches(list.ToString(), @"\[TABLE").Count;

    [Test]
    public void SameAsMap([Values(0, 1, 31, 32, 33, 100, 1025, 40_000)] int n)
    {
        var rng = new Random(n);
        foreach (var drop in new[] { 0, 7, 1000 })
        {
            var list = RrbList<int>.Create(Enumerable.Range(0, n + drop).ToArray());
            for (var i = 0; i < drop; i++) list = list.PopFirst();
            var at = n == 0 ? 0 : rng.Next(n);
            var (l, r) = list.Split(at);
            foreach (var half in new[] { list, l, r, l.Merge(RrbList<int>.Create(new[] { 1, 2, 3 })) })
            {
                var mapped = half.Map(x => x * 3);
                var shaped = half.MapShape(half.Select(x => x * 3).ToArray());
                shaped.VerifyIntegrity();
                Assert.That(shaped.ToString(), Is.EqualTo(mapped.ToString()));
                Assert.That(shaped.ToArray(), Is.EqualTo(mapped.ToArray()));
            }

            var joined = l.MapShape(l.Select(x => x + 1).ToArray()).Merge(r.MapShape(r.Select(x => x + 1).ToArray()));
            joined.VerifyIntegrity();
            Assert.That(joined.ToArray(), Is.EqualTo(list.Select(x => x + 1).ToArray()));
            Assert.That(Tables(joined), Is.EqualTo(0));
        }
    }

    [Test]
    public void WrongCountThrows()
    {
        var list = RrbList<int>.Create(new[] { 1, 2, 3 });
        Assert.Throws<ArgumentException>(() => list.MapShape(new[] { 1, 2 }));
    }
}
