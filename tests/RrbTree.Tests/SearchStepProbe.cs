using Collections;

namespace rrbtests;

// Which operations leave a tree that breaks the search step invariant, when
// the tree they start from keeps it, and how far a lookup has to walk in what
// they leave. Prints counts rather than failing.
[TestFixture, Explicit]
public class SearchStepProbe
{
    private static bool Holds(RrbList<int> l)
    {
        try { l.VerifySearchStep(); return true; }
        catch { return false; }
    }

    [Test]
    public void Probe()
    {
        var rng = new Random(1);
        var broken = new Dictionary<string, int>();
        var tried = new Dictionary<string, int>();
        var walk = new Dictionary<string, int>();

        void Note(string op, RrbList<int> result)
        {
            tried[op] = tried.GetValueOrDefault(op) + 1;
            if (!Holds(result)) broken[op] = broken.GetValueOrDefault(op) + 1;
            walk[op] = Math.Max(walk.GetValueOrDefault(op), result.MaxSearchDistance());
        }

        RrbList<int> Fresh(int n) => rng.Next(2) == 0
            ? RrbList<int>.Create(Enumerable.Range(0, n).ToArray())
            : RrbList<int>.Create(Enumerable.Range(0, n + 50).ToArray()).Slice(rng.Next(50), n);

        for (var round = 0; round < 300; round++)
        {
            var a = Fresh(rng.Next(1, 40_000));

            var l = a;
            for (var k = 0; k < 400; k++)
            {
                l = l.Insert(rng.Next(l.Count + 1), -1);
                Note("Insert", l);
            }

            l = a;
            for (var k = 0; k < 400 && l.Count > 0; k++)
            {
                l = l.RemoveAt(rng.Next(l.Count));
                Note("RemoveAt", l);
            }

            l = Fresh(rng.Next(1, 100));
            for (var k = 0; k < 100; k++)
            {
                l = l.Merge(Fresh(rng.Next(1, 70)));
                Note("Merge chain", l);
            }

            Note("Merge", a.Merge(Fresh(rng.Next(1, 3000))));
            var (left, right) = a.Split(rng.Next(a.Count + 1));
            Note("Split", left);
            Note("Split", right);
        }

        // Everything mixed, for a long time, on one list.
        var m = Fresh(20_000);
        for (var k = 0; k < 20_000; k++)
        {
            switch (rng.Next(6))
            {
                case 0: m = m.Insert(rng.Next(m.Count + 1), k); break;
                case 1: if (m.Count > 0) m = m.RemoveAt(rng.Next(m.Count)); break;
                case 2: m = m.Merge(Fresh(rng.Next(1, 70))); break;
                case 3: m = Fresh(rng.Next(1, 70)).Merge(m); break;
                case 4:
                {
                    var (x, y) = m.Split(rng.Next(m.Count + 1));
                    m = y.Merge(x);
                    break;
                }
                default: if (m.Count > 40_000) m = m.Slice(rng.Next(1000), 30_000); break;
            }

            Note("Mixed", m);
        }

        Console.WriteLine($"{"op",-12} {"broken",8} {"of",8} {"walk",5}");
        foreach (var op in tried.Keys.OrderBy(k => k))
            Console.WriteLine($"{op,-12} {broken.GetValueOrDefault(op),8} {tried[op],8} {walk[op],5}");
    }
}
