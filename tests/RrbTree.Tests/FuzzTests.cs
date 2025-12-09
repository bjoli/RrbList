using Collections;

namespace rrbtests;

[TestFixture]
public class FuzzTest
{
    private bool CheckAll(RrbList<int> a, List<int> b)
    {
        for (var i = 0; i < b.Count; i++)
            if (a[i] != b[i])
                return false;

        return true;
    }

    [Test]
    public void FuzzTest_Relaxed_RrbList()
    {
        // 1. SETUP
        // printCode = true means that it outputs code you can paste into another file so
        // you can recreate the test (with as easier way to set breakpoints). Otherwise you can just 
        // reuse the seed printed at the end of a failed test.
        var printCode = true;
        var seed = Environment.TickCount;
        //int seed = 27306740; // Uncomment to reproduce a specific crash


        var random = new Random(seed);

        // this hinges on List<> being correct, which it is :)
        var expected = new List<int>(Enumerable.Range(0, 32500));
        var actual = new RrbList<int>(Enumerable.Range(0, 32500));

        // 50_000 operations takes a moderate amount of time. If you really want to stress it, 
        // numbers up to one million is pretty fast. 
        var iterations = 50000;

        Console.WriteLine($"[FuzzTest] Started with Seed: {seed}");

        try
        {
            for (var i = 0; i < iterations; i++)
            {
                // This values are used to insert into the tree, and to choose the operation (p).
                var p = random.NextDouble();
                var val = random.Next(1000, 9999); // distinct looking numbers

                // A. ADD (Append) - 40%
                // Pushing a tail in a large unbalanced tree. 
                if (p < 0.40)
                {
                    if (printCode) Console.WriteLine($"actual.Add({val});\n expected.Add({val});\n");
                    expected.Add(val);
                    actual = actual.Add(val);
                }
                // B. REMOVE AT - 25%
                // This forces the tree to calculate SizeTables and shift indices
                else if (p < 0.65)
                {
                    if (expected.Count > 0)
                    {
                        var index = random.Next(0, expected.Count);

                        if (printCode) Console.WriteLine($"actual.RemoveAt({index});\n expected.Add({index});\n");

                        expected.RemoveAt(index);
                        actual = actual.RemoveAt(index);
                    }
                }
                // C. SET ITEM 25%
                // The failure mode here is a failure to find the correct node path.
                else if (p < 0.90)
                {
                    if (expected.Count > 0)
                    {
                        var index = random.Next(0, expected.Count);

                        if (printCode)
                            Console.WriteLine($"actual.SetItem({index}, {val});\n expected[{index}] = {val});\n");

                        expected[index] = val;
                        actual = actual.SetItem(index, val);
                    }
                }
                // D. INSERT - 10%
                // Insert does a split and a merge
                else
                {
                    var index = random.Next(0, expected.Count + 1);


                    if (printCode)
                        Console.WriteLine($"actual.Insert({index}, {val});\n expected.Insert({index}, {val});\n");

                    expected.Insert(index, val);
                    actual = actual.Insert(index, val); // Assuming you have Insert
                }

                // . just a fast verification on every iteration.
                if (expected.Count != actual.Count)
                    throw new Exception($"Count Mismatch at iter {i}. " +
                                        $"Expected {expected.Count}, Actual {actual.Count}");


                // This is a great place to test the enumerator!
                var foreachList = 0;
                var foreachRrb = 0;
                foreach (var io in actual) foreachRrb += io;

                foreach (var oi in expected) foreachList += oi;

                if (foreachRrb != foreachList) throw new Exception("foreach mismatch");
                //
                // if (!CheckAll(actual, expected))
                //  {
                //      Console.WriteLine("BreakPoint!");
                // }
                // On every tenth iteration we choose 3 random indices to check. 
                if (expected.Count > 0 && i % 10 == 0)
                    for (var k = 0; k < 3; k++)
                    {
                        var idx = random.Next(0, expected.Count);
                        if (expected[idx] != actual[idx])
                        {
                            Console.WriteLine("bp");
                            throw new Exception($"Value Mismatch at index {idx} (iter {i}). " +
                                                $"Ex: {expected[idx]}, Ac: {actual[idx]}");
                        }
                    }
            }

            // just to be sure.
            actual.VerifyIntegrity();


            // At the end, check EVERYTHING.
            Console.WriteLine("Final Full Scan...");
            for (var i = 0; i < expected.Count; i++)
                if (expected[i] != actual[i])
                    throw new Exception($"Final Scan Mismatch at {i}");

            Console.WriteLine("SUCCESS!");
        }
        catch (Exception ex)
        {
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Console.WriteLine("TEST FAILED AT ITERATION");
            Console.WriteLine($"SEED: {seed}");
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            throw; // Rethrow to test handler
        }
    }
}