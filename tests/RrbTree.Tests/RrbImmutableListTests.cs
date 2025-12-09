using System.Collections.Immutable;
using Collections;

namespace rrbtests;

[TestFixture]
public class RrbIImmutableListTests
{
    private IImmutableList<int> CreateList(params int[] items)
    {
        return RrbList<int>.Create(items);
    }

    [Test]
    public void Add_AppendsToEnd_AndPreservesImmutability()
    {
        var list = CreateList(1, 2, 3);
        var newList = list.Add(4);

        Assert.That(list, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        Assert.That(newList.Count, Is.EqualTo(4));
    }

    [Test]
    public void AddRange_AppendsMultiple_AndPreservesImmutability()
    {
        var list = CreateList(1, 2);
        var newList = list.AddRange(new[] { 3, 4, 5 });

        Assert.That(list, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(newList.Count, Is.EqualTo(5));
    }

    [Test]
    public void AddRange_WithEmptyCollection_ReturnsSameInstance()
    {
        var list = CreateList(1, 2);
        var newList = list.AddRange(Enumerable.Empty<int>());

        Assert.That(newList, Is.SameAs(list));
    }

    [Test]
    public void Clear_ReturnsEmptyList()
    {
        var list = CreateList(1, 2, 3);
        var empty = list.Clear();

        Assert.That(empty.Count, Is.EqualTo(0));
        Assert.That(list.Count, Is.EqualTo(3)); // Original untouched
    }

    [Test]
    public void Insert_InsertsAtCorrectIndex()
    {
        var list = CreateList(1, 3);
        var newList = list.Insert(1, 2);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Insert_AtZero_Prepends()
    {
        var list = CreateList(2, 3);
        var newList = list.Insert(0, 1);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Insert_AtCount_Appends()
    {
        var list = CreateList(1, 2);
        var newList = list.Insert(2, 3); // Index == Count

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void InsertRange_InsertsMultipleItems()
    {
        var list = CreateList(1, 5);
        var newList = list.InsertRange(1, new[] { 2, 3, 4 });

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Remove_RemovesFirstOccurrence()
    {
        var list = CreateList(1, 2, 3, 2, 4);
        var newList = list.Remove(2);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 3, 2, 4 }));
    }

    [Test]
    public void Remove_ItemNotFound_ReturnsSameInstance()
    {
        var list = CreateList(1, 2, 3);
        var newList = list.Remove(99);

        Assert.That(newList, Is.SameAs(list));
    }

    [Test]
    public void RemoveAt_RemovesCorrectIndex()
    {
        var list = CreateList(1, 2, 3);
        var newList = list.RemoveAt(1);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 3 }));
    }

    [Test]
    public void RemoveAll_RemovesMatchingPredicate()
    {
        var list = CreateList(1, 2, 3, 4, 5, 6);
        // Remove even numbers
        var newList = list.RemoveAll(x => x % 2 == 0);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 3, 5 }));
    }

    [Test]
    public void RemoveRange_IndexCount_RemovesSlice()
    {
        // Indexes:       0  1  2  3  4  5
        var list = CreateList(1, 2, 3, 4, 5, 6);

        // Remove 3, 4 (Index 2, Count 2)
        var newList = list.RemoveRange(2, 2);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 5, 6 }));
    }

    [Test]
    public void RemoveRange_Values_RemovesCorrectCounts()
    {
        // A list with duplicates
        var list = CreateList(1, 2, 2, 3, 3, 3);

        // We want to remove one '2' and two '3's
        var toRemove = new[] { 2, 3, 3 };

        var newList = list.RemoveRange(toRemove, EqualityComparer<int>.Default);

        // Expected: 1, 2, 3 (One '2' remains, One '3' remains)
        Assert.That(newList, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void RemoveRange_Values_PreservesOrder()
    {
        var list = CreateList(1, 2, 3, 4, 5);
        var toRemove = new[] { 2, 4 };

        var newList = list.RemoveRange(toRemove, null);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 3, 5 }));
    }

    [Test]
    public void Replace_ReplacesFirstOccurrence()
    {
        var list = CreateList(1, 2, 2, 3);
        var newList = list.Replace(2, 99, EqualityComparer<int>.Default);

        Assert.That(newList, Is.EquivalentTo(new[] { 1, 99, 2, 3 }));
    }

    [Test]
    public void Replace_ItemNotFound_ThrowsException()
    {
        var list = CreateList(1, 2, 3);
        Assert.Throws<ArgumentException>(() => list.Replace(99, 100, null));
    }

    [Test]
    public void SetItem_UpdatesValueAtIndex()
    {
        var list = CreateList(1, 2, 3);
        var newList = list.SetItem(1, 99);

        Assert.That(newList[1], Is.EqualTo(99));
        Assert.That(list[1], Is.EqualTo(2)); // Original preserved
    }

    [Test]
    public void IndexOf_FindsFirstMatch()
    {
        var list = CreateList(1, 2, 3, 2, 1);

        Assert.That(list.IndexOf(2, 0, list.Count, null), Is.EqualTo(1));
    }

    [Test]
    public void IndexOf_WithRange_RespectsRange()
    {
        // Indexes:       0  1  2  3  4
        var list = CreateList(1, 2, 3, 2, 1);

        // Search for '2' starting at index 2 (value 3), length 3 (3, 2, 1)
        var index = list.IndexOf(2, 2, 3, null);

        Assert.That(index, Is.EqualTo(3));
    }

    [Test]
    public void LastIndexOf_FindsLastMatch()
    {
        var list = CreateList(1, 2, 3, 2, 1);

        // Search backwards from the end
        Assert.That(list.LastIndexOf(2, list.Count - 1, list.Count, null), Is.EqualTo(3));
    }

    [Test]
    public void LastIndexOf_WithRange_RespectsRange()
    {
        // Indexes:       0  1  2  3  4
        var list = CreateList(1, 2, 3, 2, 1);

        // Search backwards starting at index 2 (value 3), look at 3 items (indices 2, 1, 0)
        // Range values: [3, 2, 1]
        var index = list.LastIndexOf(2, 2, 3, null);

        Assert.That(index, Is.EqualTo(1));
    }
}