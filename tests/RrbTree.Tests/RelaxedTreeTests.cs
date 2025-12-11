using Collections;

namespace rrbtests;

[TestFixture]
public class RelaxedTreeTests
{
    /// <summary>
    ///     Creates a relaxed tree by removing an element from the middle,
    ///     which forces the creation of a node with a SizeTable.
    /// </summary>
    private RrbList<int> CreateRelaxedList()
    {
        // Create a list large enough to have a few levels (e.g., > 32 * 2 items)
        var list = new RrbList<int>(Enumerable.Range(0, 100));

        // Remove an item from a node that is not the last one.
        // Removing from index 10 will make the first leaf sparse.
        // The root node will now need a SizeTable to navigate correctly.
        var relaxedList = list.RemoveAt(10);

        // Sanity check
        Assert.That(relaxedList.Count, Is.EqualTo(99));
        Assert.That(relaxedList[9], Is.EqualTo(9));
        Assert.That(relaxedList[10], Is.EqualTo(11)); // Item 10 was removed

        return relaxedList;
    }

    [Test]
    public void Builder_FromRelaxedList_ShouldHaveCorrectCount()
    {
        var relaxedList = CreateRelaxedList();
        var builder = relaxedList.ToBuilder();

        Assert.That(builder.Count, Is.EqualTo(relaxedList.Count));
    }

    [Test]
    public void Builder_FromRelaxedList_IndexedGet_ShouldWork()
    {
        var relaxedList = CreateRelaxedList();
        var builder = relaxedList.ToBuilder();

        // Access items before and after the removed element
        Assert.That(builder[9], Is.EqualTo(9));
        Assert.That(builder[10], Is.EqualTo(11));
        Assert.That(builder[98], Is.EqualTo(99));
    }

    [Test]
    public void Builder_FromRelaxedList_IndexedSet_ShouldWork()
    {
        var relaxedList = CreateRelaxedList();
        var builder = relaxedList.ToBuilder();

        // Set an item in the sparse part of the tree
        builder[10] = 999;

        Assert.That(builder[10], Is.EqualTo(999));

        var final_list = builder.ToImmutable();
        Assert.That(final_list[10], Is.EqualTo(999));
        Assert.That(final_list.Count, Is.EqualTo(99));
    }

    [Test]
    public void Builder_FromRelaxedList_Add_ShouldCorrectlyPushTail()
    {
        // This is the key test. It checks if the builder can handle
        // pushing a tail into a tree that has relaxed nodes.
        var relaxedList = CreateRelaxedList(); // Count = 99
        var builder = relaxedList.ToBuilder();

        // Add enough items to force the current tail to be pushed into the tree.
        // The initial tail has (99 % 32) = 3 items.
        // We need to add (32 - 3) = 29 items to fill it.
        // Adding 30 will force a push.
        for (var i = 0; i < 30; i++) builder.Add(1000 + i);

        Assert.That(builder.Count, Is.EqualTo(99 + 30));

        var finalList = builder.ToImmutable();
        Assert.That(finalList.Count, Is.EqualTo(129));

        // Verify content integrity
        Assert.That(finalList[10], Is.EqualTo(11)); // Check original relaxed part
        Assert.That(finalList[98], Is.EqualTo(99)); // End of original part
        Assert.That(finalList[99], Is.EqualTo(1000)); // Start of new part
        Assert.That(finalList[128], Is.EqualTo(1029)); // Last added item
    }

    [Test]
    public void Builder_FromRelaxedList_WithFatTail_Add_ShouldWork()
    {
        // Scenario: Create a relaxed list, convert to a builder with a large
        // tail capacity, and perform bulk adds.
        var relaxedList = CreateRelaxedList();

        // Use a builder with a larger tail capacity
        var builder = new RrbBuilder<int>(128);
        foreach (var item in relaxedList) builder.Add(item);

        // At this point, the builder should internally match the relaxed list
        Assert.That(builder.Count, Is.EqualTo(99));
        Assert.That(builder[10], Is.EqualTo(11));

        // Now, add more items. This should all go into the fat tail without
        // interacting with the relaxed tree structure yet.
        for (var i = 0; i < 50; i++) builder.Add(2000 + i);

        Assert.That(builder.Count, Is.EqualTo(99 + 50));

        var finalList = builder.ToImmutable();
        Assert.That(finalList.Count, Is.EqualTo(149));

        // Verify integrity
        Assert.That(finalList[10], Is.EqualTo(11));
        Assert.That(finalList[99], Is.EqualTo(2000));
        Assert.That(finalList[148], Is.EqualTo(2049));
    }


    [Test]
    public void TestTimeBomb_SparseSlice_FollowedByGrowth()
    {
        // 1. Create a full dense tree of 1024 items (32 * 32).
        //    Height 1 (Shift 5), Root has 32 children, all full.
        var list = new RrbList<int>(Enumerable.Range(0, 1025));

        // 2. Create the "Time Bomb".
        //    Slice to 993 items.
        //    993 = 31 * 32 + 1.
        //    Resulting Root (Height 1) has 32 children.
        //    Children 0-30 are full (32 items).
        //    Child 31 has 1 item.
        //    Crucially: RrbList.Slice (into tree) clears the Tail, so Root holds all 993 items.
        //    Bug condition: SliceRightRec returns a Balanced Node (no SizeTable) 
        //    because it still has 32 children, ignoring that the last one is sparse.
        list = list.Slice(0, 993);

        Assert.That(993, Is.EqualTo(list.Count));

        // Verify we have the structure we expect for the test
        // (This assertion is just to confirm the setup logic, not the fix)
        //Assert.That(0, Is.EqualTo(list.TailLen), "Slice into tree should result in empty tail");

        // 3. Trigger the Explosion.
        //    Add items to force Height Growth.
        //    Current capacity of Height 1 tree is 1024.
        //    We have 993 items.
        //    Tail can hold 32 items.
        //    We add 33 items.
        //    32 go to Tail. 33rd forces a flush.
        //    Flush triggers AppendLeafToTree.
        //    Target Index = 993 + 32 = 1025.
        //    1025 > 1024 -> Triggers Height Growth.
        //    New Root (Height 2) wraps Old Root.
        //    
        //    WITHOUT FIX: New Root is Dense. It assumes Child[0] (Old Root) is full (1024 items).
        //    WITH FIX: New Root detects Old Root is sparse (993 items) and creates SizeTable.
        for (var i = 0; i < 33; i++)
            //    This was put here for debugging reasons. You need to make TailLen public for thes to work
            //    if (list.TailLen == 32)
            //        Console.WriteLine(321);
            list = list.Add(2000 + i);

        Assert.That(1026, Is.EqualTo(list.Count));

        // 4. Access the "Phantom Zone".
        //    We added item 2000 at index 993.
        //    We added item 2007 at index 1000.
        //    
        //    If Bug exists:
        //    Root checks index 1000.
        //    1000 < 1024 -> Maps to Child[0] (The Time Bomb).
        //    Child[0] tries to find index 1000.
        //    1000 maps to SubChild 31 (which has 1 item).
        //    Index within SubChild is 8 (1000 % 32).
        //    8 > 1 -> IndexOutOfRangeException.
        //    
        //    If Fix works:
        //    Root has SizeTable. Child[0] size is 993.
        //    1000 >= 993 -> Maps to Child[1].
        //    Child[1] contains the new items.
        //    Success.

        var val = list[1000];
        Assert.That(2007, Is.EqualTo(val), "Failed to retrieve item from the 'gap' zone.");
    }
    
    [Test]
    public void IterUnbalanced()
    {
        var unbalanced = misc.MakeUnbalanced(35000);
        long sum = 0;
        long sum2 = 0;
        
        // I am just mostly trying to see if this raises an exception.
        for (int i = 0; i < unbalanced.Count; i+=1 )
            sum += unbalanced[i];

        foreach (var item in unbalanced)
        {
            sum2 += item;
        }
        Assert.That(sum2, Is.EqualTo(sum));
    }
}