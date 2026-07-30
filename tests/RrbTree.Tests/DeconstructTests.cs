using Collections;
using NUnit.Framework;

namespace rrbtests;

[TestFixture]
public class DeconstructTests
{
    [Test]
    public void TestDeconstruct_OneElement()
    {
        var list = new RrbList<int>(new[] { 42 });

        if (list is (var a, { } rest))
        {
            Assert.That(a, Is.EqualTo(42));
            Assert.That(rest.Count, Is.EqualTo(0));
        }
        else
        {
            Assert.Fail("Pattern match failed");
        }
    }

    [Test]
    public void TestDeconstruct_TwoElements()
    {
        var list = new RrbList<int>(new[] { 10, 20 });

        if (list is (var a, var b, { } rest))
        {
            Assert.That(a, Is.EqualTo(10));
            Assert.That(b, Is.EqualTo(20));
            Assert.That(rest.Count, Is.EqualTo(0));
        }
        else
        {
            Assert.Fail("Pattern match failed");
        }
    }

    [Test]
    public void TestDeconstruct_ThreeElements()
    {
        var list = new RrbList<int>(new[] { 1, 2, 3 });

        if (list is (var a, var b, var c, { } rest))
        {
            Assert.That(a, Is.EqualTo(1));
            Assert.That(b, Is.EqualTo(2));
            Assert.That(c, Is.EqualTo(3));
            Assert.That(rest.Count, Is.EqualTo(0));
        }
        else
        {
            Assert.Fail("Pattern match failed");
        }
    }

    [Test]
    public void TestDeconstruct_SixElementsAndRest()
    {
        var list = new RrbList<int>(new[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        if (list is (var a, var b, var c, var d, var e, var f, { } rest))
        {
            Assert.That(a, Is.EqualTo(1));
            Assert.That(b, Is.EqualTo(2));
            Assert.That(c, Is.EqualTo(3));
            Assert.That(d, Is.EqualTo(4));
            Assert.That(e, Is.EqualTo(5));
            Assert.That(f, Is.EqualTo(6));
            Assert.That(rest.Count, Is.EqualTo(2));
            Assert.That(rest[0], Is.EqualTo(7));
            Assert.That(rest[1], Is.EqualTo(8));
        }
        else
        {
            Assert.Fail("Pattern match failed");
        }
    }

    [Test]
    public void TestDeconstruct_FailureOnInsufficientElements()
    {
        var list = new RrbList<int>(new[] { 1, 2 });

        // Should fail because we demand 3 elements + non-null rest
        var matched = list is (var a, var b, var c, { } rest);
        Assert.That(matched, Is.False);
    }
    
    [Test]
    public void TestDeconstruct_ExactMatchFailsOnExtraElements()
    {
        var list = new RrbList<int>(new[] { 1, 2, 3 });

        // Demands exactly 2 elements (rest.Count == 0)
        var exactMatch = list is (var a, var b, { Count: 0 });
        Assert.That(exactMatch, Is.False);
        
        var partialMatch = list is (var c, var d, { Count: > 0 } rest);
        Assert.That(partialMatch, Is.True);
        if (list is (var e, var f, { Count: > 0 } tail))
        {
            Assert.That(tail.Count, Is.EqualTo(1));
            Assert.That(tail[0], Is.EqualTo(3));
        }
    }

    [Test]
    public void TestDeconstruct_LargeTree()
    {
        // 5000 items forces a tree structure
        var data = Enumerable.Range(0, 5000).ToArray();
        var list = new RrbList<int>(data);

        if (list is (var a, var b, var c, var d, var e, var f, { } rest))
        {
            Assert.That(a, Is.EqualTo(0));
            Assert.That(b, Is.EqualTo(1));
            Assert.That(c, Is.EqualTo(2));
            Assert.That(d, Is.EqualTo(3));
            Assert.That(e, Is.EqualTo(4));
            Assert.That(f, Is.EqualTo(5));
            
            Assert.That(rest.Count, Is.EqualTo(4994));
            Assert.That(rest[0], Is.EqualTo(6));
            Assert.That(rest[4993], Is.EqualTo(4999));
        }
        else
        {
            Assert.Fail("Pattern match failed for large tree");
        }
    }
}
