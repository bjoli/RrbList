using System;
using System.Runtime.CompilerServices;

namespace Collections;

public sealed partial class RrbList<T>
{
    /// <summary>
    /// Deconstructs the list into 1 head element and a rest list.
    /// Supports C# pattern matching like: <c>list is (var a, { } rest)</c>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T item1, out RrbList<T>? rest)
    {
        if (Count < 1)
        {
            item1 = default!;
            rest = null;
            return;
        }

        item1 = this[0];

        if (Count == 1)
        {
            rest = Empty;
            return;
        }

        if (Root == null)
        {
            var restCount = Count - 1;
            var newTail = new T[restCount];
            Array.Copy(Tail, 1, newTail, 0, restCount);
            rest = new RrbList<T>(null, newTail, restCount, 0, restCount);
            return;
        }

        rest = Slice(1, Count - 1);
    }

    /// <summary>
    /// Deconstructs the list into 2 head elements and a rest list.
    /// Supports C# pattern matching like: <c>list is (var a, var b, { } rest)</c>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T item1, out T item2, out RrbList<T>? rest)
    {
        if (Count < 2)
        {
            item1 = default!;
            item2 = default!;
            rest = null;
            return;
        }

        item1 = this[0];
        item2 = this[1];

        if (Count == 2)
        {
            rest = Empty;
            return;
        }

        if (Root == null)
        {
            var restCount = Count - 2;
            var newTail = new T[restCount];
            Array.Copy(Tail, 2, newTail, 0, restCount);
            rest = new RrbList<T>(null, newTail, restCount, 0, restCount);
            return;
        }

        rest = Slice(2, Count - 2);
    }

    /// <summary>
    /// Deconstructs the list into 3 head elements and a rest list.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T item1, out T item2, out T item3, out RrbList<T>? rest)
    {
        if (Count < 3)
        {
            item1 = default!;
            item2 = default!;
            item3 = default!;
            rest = null;
            return;
        }

        item1 = this[0];
        item2 = this[1];
        item3 = this[2];

        if (Count == 3)
        {
            rest = Empty;
            return;
        }

        if (Root == null)
        {
            var restCount = Count - 3;
            var newTail = new T[restCount];
            Array.Copy(Tail, 3, newTail, 0, restCount);
            rest = new RrbList<T>(null, newTail, restCount, 0, restCount);
            return;
        }

        rest = Slice(3, Count - 3);
    }

    /// <summary>
    /// Deconstructs the list into 4 head elements and a rest list.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T item1, out T item2, out T item3, out T item4, out RrbList<T>? rest)
    {
        if (Count < 4)
        {
            item1 = default!;
            item2 = default!;
            item3 = default!;
            item4 = default!;
            rest = null;
            return;
        }

        item1 = this[0];
        item2 = this[1];
        item3 = this[2];
        item4 = this[3];

        if (Count == 4)
        {
            rest = Empty;
            return;
        }

        if (Root == null)
        {
            var restCount = Count - 4;
            var newTail = new T[restCount];
            Array.Copy(Tail, 4, newTail, 0, restCount);
            rest = new RrbList<T>(null, newTail, restCount, 0, restCount);
            return;
        }

        rest = Slice(4, Count - 4);
    }

    /// <summary>
    /// Deconstructs the list into 5 head elements and a rest list.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T item1, out T item2, out T item3, out T item4, out T item5, out RrbList<T>? rest)
    {
        if (Count < 5)
        {
            item1 = default!;
            item2 = default!;
            item3 = default!;
            item4 = default!;
            item5 = default!;
            rest = null;
            return;
        }

        item1 = this[0];
        item2 = this[1];
        item3 = this[2];
        item4 = this[3];
        item5 = this[4];

        if (Count == 5)
        {
            rest = Empty;
            return;
        }

        if (Root == null)
        {
            var restCount = Count - 5;
            var newTail = new T[restCount];
            Array.Copy(Tail, 5, newTail, 0, restCount);
            rest = new RrbList<T>(null, newTail, restCount, 0, restCount);
            return;
        }

        rest = Slice(5, Count - 5);
    }

    /// <summary>
    /// Deconstructs the list into 6 head elements and a rest list.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T item1, out T item2, out T item3, out T item4, out T item5, out T item6, out RrbList<T>? rest)
    {
        if (Count < 6)
        {
            item1 = default!;
            item2 = default!;
            item3 = default!;
            item4 = default!;
            item5 = default!;
            item6 = default!;
            rest = null;
            return;
        }

        item1 = this[0];
        item2 = this[1];
        item3 = this[2];
        item4 = this[3];
        item5 = this[4];
        item6 = this[5];

        if (Count == 6)
        {
            rest = Empty;
            return;
        }

        if (Root == null)
        {
            var restCount = Count - 6;
            var newTail = new T[restCount];
            Array.Copy(Tail, 6, newTail, 0, restCount);
            rest = new RrbList<T>(null, newTail, restCount, 0, restCount);
            return;
        }

        rest = Slice(6, Count - 6);
    }
}
