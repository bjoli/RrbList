using Collections;

namespace rrbtests;

[TestFixture]
public class Tests
{
    [Test]
    public void EmptyList_ShouldBeEmpty()
    {
        var list = new RrbList<int>();
        Assert.That(list.Count, Is.EqualTo(0));
        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            var x = list[0];
        });
    }

    [Test]
    public void Add_SingleItem_ShouldWork()
    {
        var list = new RrbList<int>().Add(42);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0], Is.EqualTo(42));
    }

    [Test]
    public void Immutability_Add_ShouldNotModifyOriginal()
    {
        var list1 = new RrbList<int>().Add(1);
        var list2 = list1.Add(2);

        Assert.That(list1.Count, Is.EqualTo(1));
        Assert.That(list2.Count, Is.EqualTo(2));
        Assert.That(list1[0], Is.EqualTo(1));
        Assert.That(list2[1], Is.EqualTo(2));
    }

    [Test]
    public void Immutability_SetItem_ShouldNotModifyOriginal()
    {
        // 1, 2, 3
        var list1 = new RrbList<int>(new[] { 1, 2, 3 });
        var list2 = list1.SetItem(1, 999);

        Assert.That(list1[1], Is.EqualTo(2)); // Original unchanged
        Assert.That(list2[1], Is.EqualTo(999)); // New one updated
        Assert.That(list2, Is.Not.SameAs(list1));
    }

    [Test]
    public void Builder_ShouldBeMutable()
    {
        var builder = new RrbBuilder<int>();
        builder.Add(10);
        builder.Add(20);

        Assert.That(builder.ToImmutable()[0], Is.EqualTo(10));
        Assert.That(builder.ToImmutable()[1], Is.EqualTo(20));
    }

    [Test]
    public void Builder_ToImmutable_ShouldFreeze()
    {
        var builder = new RrbBuilder<int>();
        builder.Add(1);

        var list1 = builder.ToImmutable();

        // Modifying builder after ToImmutable should NOT affect list1
        // (Because ToImmutable invalidates the token, forcing Copy-On-Write)
        builder.Add(2);

        Assert.That(list1.Count, Is.EqualTo(1));
        Assert.That(builder.ToImmutable().Count, Is.EqualTo(2));
    }

    [Test]
    public void LargeScale_Add_ShouldHandleRebalancing()
    {
        // Test crossing the 32-item boundary (Leaf -> Internal)
        // And crossing the 1024-item boundary (Depth 2 -> Depth 3)
        var count = 2000;
        var builder = new RrbBuilder<int>();

        for (var i = 0; i < count; i++) builder.Add(i);

        var list = builder.ToImmutable();
        Assert.That(list.Count, Is.EqualTo(count));

        // Verify order and integrity
        for (var i = 0; i < count; i++) Assert.That(list[i], Is.EqualTo(i));
    }

    [Test]
    public void LargeScale_SetItem_RandomAccessUpdate()
    {
        // Create a large list (force multiple tree levels)
        var count = 10000;
        var list = new RrbList<int>(Enumerable.Range(0, count));

        // Update an item deep in the tree (not just the tail)
        var list2 = list.SetItem(5000, -1);

        Assert.That(list[5000], Is.EqualTo(5000)); // Old unchanged
        Assert.That(list2[5000], Is.EqualTo(-1)); // New updated
        Assert.That(list2.Count, Is.EqualTo(count));
    }

    [Test]
    public void FromEnumerable_Optimization_ShouldWork()
    {
        int[] data = { 1, 2, 3, 4, 5 };
        var list = new RrbList<int>(data);

        Assert.That(list.Count, Is.EqualTo(5));
        Assert.That(list[2], Is.EqualTo(3));
    }

    [Test]
    public void MixedOperations_BuilderAndList()
    {
        // Scenario: Build a base, freeze, add more, branch off
        var builder = new RrbBuilder<string>();
        builder.Add("A");
        builder.Add("B");

        var listV1 = builder.ToImmutable(); // ["A", "B"]

        var listV2 = listV1.Add("C"); // ["A", "B", "C"]
        var listV3 = listV1.SetItem(0, "Z"); // ["Z", "B"]

        Assert.That(listV1[0], Is.EqualTo("A"));
        Assert.That(listV2[2], Is.EqualTo("C"));
        Assert.That(listV3[0], Is.EqualTo("Z"));
        Assert.That(listV3[1], Is.EqualTo("B"));
    }

    [Test]
    public void SetItem_IndexOutOfBounds_ShouldThrow()
    {
        var list = new RrbList<int>().Add(1);
        Assert.Throws<IndexOutOfRangeException>(() => list.SetItem(1, 2));
        Assert.Throws<IndexOutOfRangeException>(() => list.SetItem(-1, 2));
    }

    // --- Concatenation Tests ---

    [Test]
    public void Merge_SmallLists_ShouldPreserveOrder()
    {
        var list1 = new RrbList<int>(new[] { 1, 2, 3 });
        var list2 = new RrbList<int>(new[] { 4, 5, 6 });

        var merged = list1.Merge(list2);

        Assert.That(merged.Count, Is.EqualTo(6));
        // Verify content matches [1, 2, 3, 4, 5, 6]
        Assert.That(merged.ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
    }

    [Test]
    public void Merge_WithEmpty_ShouldReturnOriginal()
    {
        var list = new RrbList<int>(new[] { 1, 2, 3 });
        var empty = new RrbList<int>();

        var mergeRight = list.Merge(empty);
        var mergeLeft = empty.Merge(list);

        Assert.That(mergeRight.Count, Is.EqualTo(3));
        Assert.That(mergeLeft.Count, Is.EqualTo(3));
        // Optimization check: Should return exact same instance if one side is empty
        Assert.That(mergeRight, Is.SameAs(list));
    }

    [Test]
    public void Merge_LargeLists_ShouldRebalanceCorrectly()
    {
        // 1. Create two lists large enough to have internal nodes (Height >= 1)
        // 32 * 32 = 1024. Let's use 2000 to ensure 2-level trees.
        var size = 2000;
        var list1 = new RrbList<int>(Enumerable.Range(0, size)); // [0...1999]
        var list2 = new RrbList<int>(Enumerable.Range(size, size)); // [2000...3999]

        var merged = list1.Merge(list2);

        Assert.That(merged.Count, Is.EqualTo(size * 2));

        // 2. Verify Random Access across the seam
        // Check boundaries
        Assert.That(merged[0], Is.EqualTo(0));
        Assert.That(merged[size - 1], Is.EqualTo(size - 1));
        Assert.That(merged[size], Is.EqualTo(size));
        Assert.That(merged[size * 2 - 1], Is.EqualTo(size * 2 - 1));

        // 3. Spot check random internal items
        Assert.That(merged[100], Is.EqualTo(100));
        Assert.That(merged[3000], Is.EqualTo(3000));
    }

    [Test]
    public void Merge_Associativity_Check()
    {
        // (A + B) + C == A + (B + C)
        var a = new RrbList<int>(new[] { 1, 2 });
        var b = new RrbList<int>(new[] { 3, 4 });
        var c = new RrbList<int>(new[] { 5, 6 });

        var leftAssoc = a.Merge(b).Merge(c);
        var rightAssoc = a.Merge(b.Merge(c));

        Assert.That(leftAssoc.ToArray(), Is.EqualTo(rightAssoc.ToArray()));
    }

    // --- Slicing Tests ---

    [Test]
    public void Slice_Truncate_ShouldReduceCount()
    {
        // Tests "Slice Right" (Taking the beginning)
        var list = new RrbList<int>(Enumerable.Range(0, 100));

        // Keep first 50 items
        var sliced = list.Slice(0, 50);

        Assert.That(sliced.Count, Is.EqualTo(50));
        Assert.That(sliced[0], Is.EqualTo(0));
        Assert.That(sliced[49], Is.EqualTo(49));

        // Accessing 50 should fail
        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            var x = sliced[50];
        });
    }

    [Test]
    public void Slice_InnerRange_ShouldWork()
    {
        // Tests "Slice Left + Slice Right" (Taking the middle)
        // [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
        var list = new RrbList<int>(Enumerable.Range(0, 10));

        // Slice(start: 2, count: 5) -> [2, 3, 4, 5, 6]
        var sliced = list.Slice(2, 5);

        Assert.That(sliced.Count, Is.EqualTo(5));
        Assert.That(sliced[0], Is.EqualTo(2)); // Old index 2 is new index 0
        Assert.That(sliced[4], Is.EqualTo(6)); // Old index 6 is new index 4
    }

    [Test]
    public void Slice_Identity_ShouldReturnSelf()
    {
        var list = new RrbList<int>(new[] { 1, 2, 3 });
        var sliced = list.Slice(0, 3);

        // Optimization check
        Assert.That(sliced, Is.SameAs(list));
    }

    [Test]
    public void Slice_Empty_ShouldReturnEmpty()
    {
        var list = new RrbList<int>(new[] { 1, 2, 3 });
        var sliced = list.Slice(0, 0);

        Assert.That(sliced.Count, Is.EqualTo(0));
        Assert.That(sliced, Is.SameAs(RrbList<int>.Empty));
    }

    [Test]
    public void Slice_LargeScale_ShouldPreserveStructure()
    {
        // Create deep tree
        var size = 2000;
        var list = new RrbList<int>(Enumerable.Range(0, size));

        // Slice the middle 1000 items (500 to 1500)
        var start = 500;
        var count = 1000;
        var sliced = list.Slice(start, count);

        Assert.That(sliced.Count, Is.EqualTo(count));

        // Verify integrity
        for (var i = 0; i < count; i++)
            // New[i] == Old[start + i]
            Assert.That(sliced[i], Is.EqualTo(start + i));
    }

    [Test]
    public void Insert_Middle_ShouldSplitAndMerge()
    {
        var list = new RrbList<int>(new[] { 1, 2, 4, 5 });
        var result = list.Insert(2, 3); // Insert 3 at index 2

        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result.ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Relaxed_Operations()
    {
        var list = new RrbList<int>(Enumerable.Range(0, 1000)).Insert(4, 3).RemoveAt(30).Insert(400, 500);
        Console.WriteLine(list.ToString());
        Assert.That(list.Count, Is.EqualTo(1001));
        Assert.That(list[4], Is.EqualTo(3));
        Assert.That(list[30], Is.EqualTo(30));
    }


    [Test]
    public void Fold_Sum()
    {
        var list = new RrbList<int>(Enumerable.Range(0, 100));
        var result = list.Fold(0, (state, item) => state + item);
        Assert.That(result, Is.EqualTo(4950));
    }

    [Test]
    public void Force_RelaxedNode_MiddleSparse()
    {
        // 1. Create a balanced list of 1000 items
        var list = new RrbList<int>(Enumerable.Range(0, 1000));

        // 2. Remove a chunk from the MIDDLE to create a sparse node
        // Slice(0, 320) -> 10 full leaves
        // Slice(350, 650) -> 20 full leaves (offset)
        // We want to remove items 320 to 350.
        // Left = [0..319] (320 items, 10 full leaves)
        // Right = [350..999] (650 items)

        var left = list.Slice(0, 320);
        var right = list.Slice(350, 650); // Skips 30 items

        // 3. Merge them.
        // Left ends with full leaf. Right starts with partial leaf?
        // Right starts at 350. 350 % 32 = 30.
        // So Right's first leaf has items [350..351], size 2? 
        // Wait, Slice starts at 350.
        // 350 is index 30 in leaf 10.
        // So Right starts with a leaf of size (32-30) = 2 items.

        var merged = left.Merge(right);

        // 4. Verify Access
        // Total = 320 + 650 = 970.
        Assert.That(merged.Count, Is.EqualTo(970));

        // Access around the seam
        // Index 319 (Last of Left) -> Should work
        Assert.That(merged[319], Is.EqualTo(319));

        // Index 320 (First of Right, was 350) -> Should work
        // This accesses the sparse child we just merged.
        Assert.That(merged[320], Is.EqualTo(350));
    }

    [Test]
    public void Force_RelaxedNode_TinyHead()
    {
        // 1. Create a tiny list
        var tiny = new RrbList<int>(new[] { 1 });

        // 2. Create a large full list (at least 32 items)
        var large = new RrbList<int>(Enumerable.Range(100, 100));

        // 3. Merge Tiny + Large
        // Result: [Leaf(1), Leaf(32), Leaf(32), Leaf(36)]
        // This creates a relaxed node at the root (or near root).
        var merged = tiny.Merge(large);

        // 4. Verify
        Assert.That(merged.Count, Is.EqualTo(101));
        Assert.That(merged[0], Is.EqualTo(1));

        // Access index 1. 
        // If Balanced: 1 >> 5 = 0. Maps to Leaf(1). Bounds check fails? Or returns wrong item?
        // If Relaxed: SizeTable says Leaf(1) ends at 1. 
        // Index 1 > SizeTable[0]. Move to Leaf(32). 
        // Index becomes 1 - 1 = 0. Access Leaf(32)[0]. Correct.
        Assert.That(merged[1], Is.EqualTo(100));

        // Access index 33.
        // Balanced: 33 >> 5 = 1. Maps to Leaf(32) (the second leaf).
        // Leaf(32)[1]. Value should be 101.
        // Relaxed: 33 - 1 = 32. 
        // 32 maps to Leaf(32) [index 32]? No, Leaf(32) has indices 0..31.
        // So it maps to Leaf(32) (the THIRD leaf).
        // Value should be 132.
        Assert.That(merged[33], Is.EqualTo(132));
    }

    [Test]
    public void Force_Concat_DifferentHeights()
    {
        // 1. Deep Tree (Height 2, > 1024 + tail items)
        // 1080 items ensures we have a root at Shift 10.
        var deep = new RrbList<int>(Enumerable.Range(0, 1080));
        Console.WriteLine(deep.ToString());

        // 2. Shallow Tree (Height 1, > 32 items)
        var shallow = new RrbList<int>(Enumerable.Range(2000, 60));

        // 3. Merge Deep + Shallow
        // This forces Concat to descend Deep's spine.
        var merged = deep.Merge(shallow);

        // 4. Check boundary
        Assert.That(merged[1079], Is.EqualTo(1079)); // End of old deep
        Assert.That(merged[1080], Is.EqualTo(2000)); // Start of old shallow
    }

    [Test]
    public void Test_Unbalanced_Deep()
    {
        var list = new RrbList<int>(Enumerable.Range(0, 10000));
        list = list.RemoveAt(1024);
        Assert.That(list[1024], Is.EqualTo(1025));
    }

    [Test]
    public void Kill_Node()
    {
        var list = new RrbList<int>(Enumerable.Range(0, 2500));
        for (var i = 0; i < 32; i++) list = list.RemoveAt(0);
        Assert.That(list[0], Is.EqualTo(32));
    }
}