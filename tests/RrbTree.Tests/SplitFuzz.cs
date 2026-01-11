namespace rrbtests;

using Collections;


[TestFixture]
public class SplitFuzz
{
    [Test]
    public void FuzzTestSplit()
{
    var random = new Random(Environment.TickCount);
    int iterations = 100_000;

    Console.WriteLine("Starting Split Fuzzer...");

    for (int i = 0; i < iterations; i++)
    {
        // 1. Generate Random Data
        int size = random.Next(0, 5000); // Test empty, small, and medium lists
        var originalList = new List<int>(size);
        var rrbBuilder = RrbList<int>.Empty.ToBuilder();

        for (int val = 0; val < size; val++)
        {
            originalList.Add(val);
            rrbBuilder.Add(val);
        }

        var rrbList = rrbBuilder.ToImmutable();

        // 2. Pick a Random Split Index
        // Include edge cases: 0, Count, and middle
        int splitIndex = random.Next(0, size + 1);

        // 3. Perform Operation
        // Reference (Trusted Oracle)
        var expectedLeft = originalList.GetRange(0, splitIndex);
        var expectedRight = originalList.GetRange(splitIndex, size - splitIndex);

        // Target (Your Implementation)
        var (actualLeft, actualRight) = rrbList.Split(splitIndex);

        // 4. Verify
        try
        {
            AssertCollectionsEqual(expectedLeft, actualLeft, "Left Side", i, splitIndex, size);
            AssertCollectionsEqual(expectedRight, actualRight, "Right Side", i, splitIndex, size);
            
            // Verify Internal Integrity (Re-scan size tables)
            ((RrbList<int>)actualLeft).VerifyIntegrity();
            ((RrbList<int>)actualRight).VerifyIntegrity();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED at Iteration {i}");
            Console.WriteLine($"Size: {size}, SplitIndex: {splitIndex}");
            Console.WriteLine(ex.Message);
            throw; // Stop on first error
        }
    }

    Console.WriteLine("Split Fuzzer Passed!");
}

private void AssertCollectionsEqual(List<int> expected, RrbList<int> actual, string side, int iter, int splitIdx, int totalSize)
{
    if (expected.Count != actual.Count)
        throw new Exception($"{side} Count mismatch. Expected {expected.Count}, Got {actual.Count}");

    // Indexer check
    for (int j = 0; j < expected.Count; j++)
    {
        if (expected[j] != actual[j])
            throw new Exception($"{side} Item mismatch at {j}. Exp {expected[j]}, Got {actual[j]}");
    }
}
}