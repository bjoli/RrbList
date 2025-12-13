using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Collections; // Your namespace

namespace rrbtests;

[TestFixture]
public class RrbFuzzTest
{
    // Configuration
    private const int Iterations = 100_000;
    private const int MaxPoolSize = 20;
    private const int MaxInitialSize = 1000;
    private const int Seed = 42; // Fixed seed for reproducibility

    private readonly Random _rng = new Random(Seed);
    
    // The "Pool" of active lists to play with.
    // We store the RrbList and a standard List<int> as the "Truth" reference.
    private List<(RrbList<int> Actual, List<int> Expected)> _pool = new();
    [Test]
    public void Run()
    {
        Console.WriteLine($"Starting Fuzz Test with Seed {Seed}...");
        
        // Seed the pool with a few lists
        for (int i = 0; i < 5; i++) AddNewListToPool();

        for (int i = 0; i < Iterations; i++)
        {
            if (i % 1000 == 0) Console.Write(".");

            // Ensure pool isn't empty or too massive
            if (_pool.Count == 0) AddNewListToPool();
            if (_pool.Count > MaxPoolSize) PrunePool();

            // Pick a random operation weighted by probability
            double roll = _rng.NextDouble();

            try
            {
                if (roll < 0.3) // 30% Split
                {
                    DoSplit();
                }
                else if (roll < 0.6 && _pool.Count >= 2) // 30% Merge (needs 2 items)
                {
                    DoMerge();
                }
                else // 40% Random Mutation (Insert/Remove/Set/Add) to dirty the trees
                {
                    DoMutation();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nFAILED at Iteration {i}");
                Console.WriteLine(ex);
                throw;
            }
        }

        Console.WriteLine("\nPassed!");
    }

    private void DoMerge()
    {
        // Pick two random lists
        int idx1 = _rng.Next(_pool.Count);
        int idx2 = _rng.Next(_pool.Count);
        if (idx1 == idx2) idx2 = (idx1 + 1) % _pool.Count;

        var (rrb1, exp1) = _pool[idx1];
        var (rrb2, exp2) = _pool[idx2];

        // 1. Perform RRB Merge
        var rrbResult = rrb1.Merge(rrb2);

        // 2. Perform Reference Merge
        var expResult = new List<int>(exp1);
        expResult.AddRange(exp2);

        // 3. Verify
        Verify(rrbResult, expResult, "Merge");

        // 4. Update Pool: Remove input lists, add merged result
        // (Remove higher index first to avoid shifting issues)
        RemoveTwoFromPool(idx1, idx2);
        _pool.Add((rrbResult, expResult));
    }

    private void DoSplit()
    {
        int idx = _rng.Next(_pool.Count);
        var (rrb, exp) = _pool[idx];

        if (rrb.Count == 0) return; // Can't split empty

        int splitIndex = _rng.Next(rrb.Count + 1); // 0 to Count inclusive

        // 1. Perform RRB Split
        var (leftRrb, rightRrb) = rrb.Split(splitIndex);

        // 2. Perform Reference Split
        var leftExp = exp.GetRange(0, splitIndex);
        var rightExp = exp.GetRange(splitIndex, exp.Count - splitIndex);

        // 3. Verify
        Verify(leftRrb, leftExp, "Split Left");
        Verify(rightRrb, rightExp, "Split Right");

        // 4. Update Pool
        _pool.RemoveAt(idx);
        _pool.Add((leftRrb, leftExp));
        _pool.Add((rightRrb, rightExp));
    }

    private void DoMutation()
    {
        int idx = _rng.Next(_pool.Count);
        var (rrb, exp) = _pool[idx];
        
        // Choose mutation type
        int op = _rng.Next(4); // 0=Add, 1=RemoveAt, 2=Insert, 3=Set

        // Helpers
        int val = _rng.Next(10000);
        
        switch (op)
        {
            case 0: // Add
                rrb = rrb.Add(val);
                exp.Add(val);
                break;

            case 1: // RemoveAt
                if (rrb.Count > 0)
                {
                    int rmIdx = _rng.Next(rrb.Count);
                    rrb = rrb.RemoveAt(rmIdx);
                    exp.RemoveAt(rmIdx);
                }
                break;

            case 2: // Insert
                int insIdx = _rng.Next(rrb.Count + 1);
                rrb = rrb.Insert(insIdx, val);
                exp.Insert(insIdx, val);
                break;

            case 3: // SetItem
                if (rrb.Count > 0)
                {
                    int setIdx = _rng.Next(rrb.Count);
                    rrb = rrb.SetItem(setIdx, val);
                    exp[setIdx] = val;
                }
                break;
        }

        Verify(rrb, exp, $"Mutation-{op}");
        
        // Update pool with mutated version
        _pool[idx] = (rrb, exp);
    }

    private void Verify(RrbList<int> rrb, List<int> expected, string operation)
    {
        // 1. Count check
        if (rrb.Count != expected.Count)
            throw new Exception($"[{operation}] Count Mismatch! RRB: {rrb.Count}, Exp: {expected.Count}");

        // 2. Internal Integrity Check (if visible)
        // You might need to make VerifyIntegrity internal visible or use reflection
        rrb.VerifyIntegrity();

        // 3. Element check (Sampled or Exhaustive)
        // For smaller lists, exhaustive check. For massive ones, sample.
        if (rrb.Count < 5000)
        {
            int i = 0;
            foreach (var item in rrb)
            {
                if (item != expected[i])
                    throw new Exception($"[{operation}] Item mismatch at index {i}. RRB: {item}, Exp: {expected[i]}");
                i++;
            }
        }
        else
        {
            // Random sampling for speed
            for (int k = 0; k < 50; k++)
            {
                int sampleIdx = _rng.Next(rrb.Count);
                if (rrb[sampleIdx] != expected[sampleIdx])
                     throw new Exception($"[{operation}] Item mismatch at {sampleIdx}");
            }
        }
    }

    private void AddNewListToPool()
    {
        int size = _rng.Next(MaxInitialSize);
        var list = new List<int>(size);
        for (int i = 0; i < size; i++) list.Add(_rng.Next(10000));
        
        _pool.Add((new RrbList<int>(list), list));
    }

    private void PrunePool()
    {
        // Remove a random item to keep pool size manageable
        _pool.RemoveAt(_rng.Next(_pool.Count));
    }

    private void RemoveTwoFromPool(int idx1, int idx2)
    {
        if (idx1 > idx2)
        {
            _pool.RemoveAt(idx1);
            _pool.RemoveAt(idx2);
        }
        else
        {
            _pool.RemoveAt(idx2);
            _pool.RemoveAt(idx1);
        }
    }
}