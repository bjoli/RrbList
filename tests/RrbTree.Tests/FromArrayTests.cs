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
}
