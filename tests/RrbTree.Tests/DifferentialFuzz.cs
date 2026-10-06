using Collections;

namespace rrbtests;

// Random operations on an RrbList and a List<int> side by side. After every
// step the list is checked for integrity and compared at every index.
[TestFixture]
public class DifferentialFuzz
{
    private const int Steps = 600;
    private const int MaxSize = 50_000;

    private Random _rng = null!;
    private int _next;
    private string _op = "";

    private int Fresh() => _next++;

    private static void Check(List<int> expected, RrbList<int> actual, string what)
    {
        actual.VerifyIntegrity();
        if (actual.Count != expected.Count)
            throw new Exception($"{what}: count {actual.Count}, expected {expected.Count}");
        for (var i = 0; i < expected.Count; i++)
            if (actual[i] != expected[i])
                throw new Exception($"{what}: index {i} is {actual[i]}, expected {expected[i]}");
        var j = 0;
        foreach (var x in actual)
        {
            if (x != expected[j]) throw new Exception($"{what}: enumerator at {j} is {x}, expected {expected[j]}");
            j++;
        }
        if (j != expected.Count) throw new Exception($"{what}: enumerator yielded {j} items");
    }

    // A list built one of several ways, so that merges see differently shaped trees.
    private (RrbList<int>, List<int>) MakeList(int size)
    {
        var items = new List<int>(size);
        for (var i = 0; i < size; i++) items.Add(Fresh());

        RrbList<int> list;
        switch (_rng.Next(4))
        {
            case 0:
                list = RrbList<int>.Create(items.ToArray());
                break;
            case 1:
                list = RrbList<int>.Empty;
                foreach (var x in items) list = list.Add(x);
                break;
            case 2:
            {
                // Built bigger, then sliced from the front, so the tree is relaxed.
                var drop = _rng.Next(1, 100);
                var pad = Enumerable.Range(0, drop).Select(_ => Fresh()).ToArray();
                list = RrbList<int>.Create(pad.Concat(items).ToArray()).Slice(drop, size);
                break;
            }
            default:
            {
                var b = RrbList<int>.Empty.ToBuilder();
                foreach (var x in items) b.Add(x);
                list = b.ToImmutable();
                break;
            }
        }

        Check(items, list, "MakeList");
        return (list, items);
    }

    private int SizeForMerge()
    {
        return _rng.Next(10) switch
        {
            < 4 => _rng.Next(1, 70),
            < 8 => _rng.Next(70, 2100),
            _ => _rng.Next(32_000, 34_000)
        };
    }

    private void Step(ref RrbList<int> actual, List<int> expected)
    {
        var n = expected.Count;
        var op = _rng.Next(13);
        // Over the cap, only shrink.
        if (n > MaxSize) op = 3 + _rng.Next(3);

        switch (op)
        {
            case 0:
            {
                var k = _rng.Next(1, 80);
                _op = $"Add x{k}";
                for (var i = 0; i < k; i++)
                {
                    var v = Fresh();
                    actual = actual.Add(v);
                    expected.Add(v);
                }
                break;
            }
            case 1:
            {
                var k = Math.Min(n, _rng.Next(1, 50));
                _op = $"Pop x{k}";
                for (var i = 0; i < k; i++)
                {
                    actual = actual.Pop();
                    expected.RemoveAt(expected.Count - 1);
                }
                break;
            }
            case 2:
            {
                var k = Math.Min(n, _rng.Next(1, 50));
                _op = $"PopFirst x{k}";
                for (var i = 0; i < k; i++)
                {
                    actual = actual.PopFirst();
                    expected.RemoveAt(0);
                }
                break;
            }
            case 3:
            {
                var c = _rng.Next(n + 1);
                _op = $"Slice(0, {c})";
                actual = actual.Slice(0, c);
                expected.RemoveRange(c, n - c);
                break;
            }
            case 4:
            {
                var s = _rng.Next(n + 1);
                _op = $"Slice({s}, {n - s})";
                actual = actual.Slice(s, n - s);
                expected.RemoveRange(0, s);
                break;
            }
            case 5:
            {
                var s = _rng.Next(n + 1);
                var c = _rng.Next(n - s + 1);
                _op = $"Slice({s}, {c})";
                actual = actual.Slice(s, c);
                var kept = expected.GetRange(s, c);
                expected.Clear();
                expected.AddRange(kept);
                break;
            }
            case 6:
            {
                var at = _rng.Next(n + 1);
                var keepLeft = _rng.Next(2) == 0;
                _op = $"Split({at}).{(keepLeft ? "Left" : "Right")}";
                var (left, right) = actual.Split(at);
                var expLeft = expected.GetRange(0, at);
                var expRight = expected.GetRange(at, n - at);
                Check(keepLeft ? expRight : expLeft, keepLeft ? right : left, _op + " other half");
                actual = keepLeft ? left : right;
                expected.Clear();
                expected.AddRange(keepLeft ? expLeft : expRight);
                break;
            }
            case 7:
            {
                _op = "MakeList";
                var (other, otherItems) = MakeList(SizeForMerge());
                var pure = _rng.Next(2) == 0;
                if (_rng.Next(2) == 0)
                {
                    _op = $"Merge({other.Count}, pure: {pure})";
                    actual = actual.Merge(other, pure);
                    expected.AddRange(otherItems);
                }
                else
                {
                    _op = $"{other.Count}.Merge(this, pure: {pure})";
                    actual = other.Merge(actual, pure);
                    expected.InsertRange(0, otherItems);
                }
                break;
            }
            case 8:
            {
                _op = "Merge(self)";
                actual = actual.Merge(actual);
                expected.AddRange(expected.ToArray());
                break;
            }
            case 9:
            {
                var k = _rng.Next(1, 20);
                _op = $"Insert x{k}";
                for (var i = 0; i < k; i++)
                {
                    var at = _rng.Next(expected.Count + 1);
                    var v = Fresh();
                    actual = actual.Insert(at, v);
                    expected.Insert(at, v);
                }
                break;
            }
            case 10:
            {
                var k = Math.Min(n, _rng.Next(1, 20));
                _op = $"RemoveAt x{k}";
                for (var i = 0; i < k; i++)
                {
                    var at = _rng.Next(expected.Count);
                    actual = actual.RemoveAt(at);
                    expected.RemoveAt(at);
                }
                break;
            }
            case 11:
            {
                var k = n == 0 ? 0 : _rng.Next(1, 20);
                _op = $"SetItem x{k}";
                for (var i = 0; i < k; i++)
                {
                    var at = _rng.Next(n);
                    var v = Fresh();
                    actual = actual.SetItem(at, v);
                    expected[at] = v;
                }
                break;
            }
            default:
                BuilderStep(ref actual, expected);
                break;
        }
    }

    private void BuilderStep(ref RrbList<int> actual, List<int> expected)
    {
        var before = actual;
        var beforeItems = expected.ToList();
        var builder = actual.ToBuilder();
        _op = "Builder";

        var rounds = _rng.Next(1, 4);
        for (var r = 0; r < rounds; r++)
        {
            switch (_rng.Next(3))
            {
                case 0:
                {
                    var k = _rng.Next(1, 100);
                    _op += $" Add x{k}";
                    for (var i = 0; i < k; i++)
                    {
                        var v = Fresh();
                        builder.Add(v);
                        expected.Add(v);
                    }
                    break;
                }
                case 1:
                {
                    var k = _rng.Next(10) == 0 ? _rng.Next(1000, 34_000) : _rng.Next(1, 300);
                    _op += $" AddRange({k})";
                    var items = Enumerable.Range(0, k).Select(_ => Fresh()).ToArray();
                    builder.AddRange(items);
                    expected.AddRange(items);
                    break;
                }
                default:
                {
                    var k = expected.Count == 0 ? 0 : _rng.Next(1, 20);
                    _op += $" Set x{k}";
                    for (var i = 0; i < k; i++)
                    {
                        var at = _rng.Next(expected.Count);
                        var v = Fresh();
                        builder[at] = v;
                        expected[at] = v;
                    }
                    break;
                }
            }

            if (builder.Count != expected.Count)
                throw new Exception($"{_op}: builder count {builder.Count}, expected {expected.Count}");
            for (var i = 0; i < expected.Count; i++)
                if (builder[i] != expected[i])
                    throw new Exception($"{_op}: builder index {i} is {builder[i]}, expected {expected[i]}");

            if (_rng.Next(3) == 0)
            {
                _op += " ToImmutable";
                Check(expected, builder.ToImmutable(), _op + " (intermediate)");
            }
        }

        _op += " ToImmutable";
        actual = builder.ToImmutable();

        // The builder must not have written into the list it came from.
        Check(beforeItems, before, _op + " (source list)");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public void RandomOperations_MatchList(int seed)
    {
        _rng = new Random(seed);
        _next = 0;

        int[] startSizes = [0, 1, 31, 33, 64, 1000, 1030, 32_760, 32_800, 40_000];
        var startSize = startSizes[_rng.Next(startSizes.Length)];
        var (actual, expected) = MakeList(startSize);

        for (var step = 0; step < Steps; step++)
        {
            var sizeBefore = expected.Count;
            try
            {
                Step(ref actual, expected);
                Check(expected, actual, _op);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"seed {seed}, step {step}, size {sizeBefore} -> {expected.Count}, op {_op}: {ex.Message}\n{actual}",
                    ex);
            }
        }
    }
}
