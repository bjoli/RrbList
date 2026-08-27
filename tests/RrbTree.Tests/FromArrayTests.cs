using Collections;

namespace rrbtests;

[TestFixture]
public class FromArrayTests
{
    [Test]
    public void FromArray_ShortPath_ClonesArrayByDefault()
    {
        var original = new[] { 1, 2, 3, 4, 5 };
        var list = RrbBuilder<int>.FromArray(original, reuseArrayIfShorterThan32: false);

        Assert.That(list.Count, Is.EqualTo(5));
        Assert.That(list[0], Is.EqualTo(1));
        Assert.That(list[4], Is.EqualTo(5));

        // Modifying the original array should not affect the list
        original[0] = 99;
        Assert.That(list[0], Is.EqualTo(1));
    }

    [Test]
    public void FromArray_ShortPath_ReusesArrayWhenRequested()
    {
        var original = new[] { 1, 2, 3, 4, 5 };
        var list = RrbBuilder<int>.FromArray(original, reuseArrayIfShorterThan32: true);

        Assert.That(list.Count, Is.EqualTo(5));
        
        // Modifying the original array will affect the list because we told it to reuse the array
        original[0] = 99;
        Assert.That(list[0], Is.EqualTo(99));
    }

    [Test]
    public void FromArray_LongPath_ExactMultipleOf32()
    {
        var original = Enumerable.Range(0, 64).ToArray();
        var list = RrbBuilder<int>.FromArray(original);

        Assert.That(list.Count, Is.EqualTo(64));
        for (int i = 0; i < 64; i++)
        {
            Assert.That(list[i], Is.EqualTo(i));
        }
    }

    [Test]
    public void FromArray_LongPath_WithTail()
    {
        var original = Enumerable.Range(0, 70).ToArray();
        var list = RrbBuilder<int>.FromArray(original);

        Assert.That(list.Count, Is.EqualTo(70));
        for (int i = 0; i < 70; i++)
        {
            Assert.That(list[i], Is.EqualTo(i));
        }
    }

    [Test]
    public void FromArray_EmptyArray()
    {
        var original = Array.Empty<int>();
        var list = RrbBuilder<int>.FromArray(original);

        Assert.That(list.Count, Is.EqualTo(0));
    }

    [Test]
    public void FromArray_NullArray_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => RrbBuilder<int>.FromArray(null!));
    }

    [Test]
    public void FromSpan_WorksCorrectly()
    {
        ReadOnlySpan<int> span = stackalloc int[] { 1, 2, 3, 4, 5 };
        var list = RrbBuilder<int>.FromSpan(span);

        Assert.That(list.Count, Is.EqualTo(5));
        Assert.That(list[0], Is.EqualTo(1));
        Assert.That(list[4], Is.EqualTo(5));
    }

    [Test]
    public void Create_FromSpan_WorksCorrectly()
    {
        var arr = Enumerable.Range(0, 100).ToArray();
        ReadOnlySpan<int> span = arr.AsSpan();
        var list = RrbList<int>.Create(span);

        Assert.That(list.Count, Is.EqualTo(100));
        for (int i = 0; i < 100; i++)
        {
            Assert.That(list[i], Is.EqualTo(i));
        }
    }

    [Test]
    public void CollectionBuilder_WorksCorrectly()
    {
        RrbList<int> list = [1, 2, 3, 4, 5];
        
        Assert.That(list.Count, Is.EqualTo(5));
        Assert.That(list[0], Is.EqualTo(1));
        Assert.That(list[4], Is.EqualTo(5));
    }

    [Test]
    public void AddRange_Span_WorksCorrectly()
    {
        RrbList<int> list = [1, 2, 3];
        var newList = list.AddRange(stackalloc int[] { 4, 5, 6 });

        Assert.That(list.Count, Is.EqualTo(3));
        Assert.That(newList.Count, Is.EqualTo(6));
        Assert.That(newList[5], Is.EqualTo(6));
    }

    [Test]
    public void Builder_AddRange_Span_WorksCorrectly()
    {
        var builder = new RrbBuilder<int>();
        builder.Add(1);
        builder.AddRange(stackalloc int[] { 2, 3, 4 });
        builder.AddRange(Enumerable.Range(5, 50).ToArray().AsSpan());

        var list = builder.ToImmutable();
        Assert.That(list.Count, Is.EqualTo(54));
        Assert.That(list[0], Is.EqualTo(1));
        Assert.That(list[53], Is.EqualTo(54));
    }
}
