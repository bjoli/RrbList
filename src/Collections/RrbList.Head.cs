/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Linus Björnstam
 */

using System.Runtime.CompilerServices;

namespace Collections;

// The head: up to a leaf's worth of items in front of the tree, the mirror of
// the tail. Prepend fills it, and pushes it into the tree as a leaf when it is
// full. PopFirst empties it, and takes the tree's first leaf as the new head
// when it is empty.
//
// Invariant: a list with no tree has no head (HeadLen == 0 when Root == null).
// Everything a small list holds is in its tail, so the code paths for small
// lists never have to look for a head.
public sealed partial class RrbList<T>
{
    /**
     * <summary>
     *     Returns a new list with the specified item added to the front.
     * </summary>
     * <param name="item">The item to add.</param>
     * <returns>A new list with the item first.</returns>
     */
    public RrbList<T> Prepend(T item)
    {
        if (Root == null)
        {
            // Case 1: No tree, and the tail has room.
            if (TailLen < Constants.RRB_BRANCHING)
            {
                var newTail = new T[TailLen + 1];
                newTail[0] = item;
                Array.Copy(Tail, 0, newTail, 1, TailLen);
                return new RrbList<T>(null, newTail, Count + 1, 0, TailLen + 1);
            }

            // Case 2: No tree, and the tail is full: it becomes the tree.
            var leaf = new LeafNode<T>(ExactArray(Tail, TailLen), TailLen, OwnerId.None);
            return new RrbList<T>(leaf, Array.Empty<T>(), Count + 1, 0, 0, [item], 1);
        }

        // Case 3: The head has room.
        if (HeadLen < Constants.RRB_BRANCHING)
        {
            var newHead = new T[HeadLen + 1];
            newHead[0] = item;
            if (HeadLen > 0) Array.Copy(Head!, 0, newHead, 1, HeadLen);
            return new RrbList<T>(Root, Tail, Count + 1, Shift, TailLen, newHead, HeadLen + 1);
        }

        // Case 4: The head is full, push it into the tree.
        var shift = Shift;
        var root = PushInFront(Root, TreeCount, Head!, HeadLen, ref shift);
        return new RrbList<T>(root, Tail, Count + 1, shift, TailLen, [item], 1);
    }

    /**
     * <summary>
     *     Returns a new list with the specified items added to the front.
     * </summary>
     * <param name="items">The items to add.</param>
     * <returns>A new list with the items first, in the order given.</returns>
     */
    public RrbList<T> PrependRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var other = items as RrbList<T> ?? Create(items);
        return other.Merge(this);
    }

    /**
     * <summary>
     *     Removes the first element from the list.
     * </summary>
     * <returns>A new list with the first element removed.</returns>
     */
    public RrbList<T> PopFirst()
    {
        // Fast Path: Just shrink the head. Kept small enough to inline. Count
        // is at least 2 here, as a list with a head also has a tree.
        var headLen = HeadLen;
        if (headLen > 1)
        {
            var newHead = new T[headLen - 1];
            Array.Copy(Head!, 1, newHead, 0, headLen - 1);
            return new RrbList<T>(Root, Tail, Count - 1, Shift, TailLen, newHead, headLen - 1);
        }

        if (Count == 0) throw new InvalidOperationException("List is empty");
        return DropFrontSlow(1);
    }

    /// <summary>
    ///     The list without its first <paramref name="n" /> items, for
    ///     0 &lt; n &lt; Count: what PopFirst and Deconstruct leave as the rest.
    /// </summary>
    /// <remarks>
    ///     When the items are all in the head, only the head is copied. When
    ///     they use it up, the tree's first leaf becomes the next head at once,
    ///     so the next match is on the fast path too.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RrbList<T> DropFront(int n)
    {
        var headLen = HeadLen;
        if (n < headLen)
        {
            var newLen = headLen - n;
            var newHead = new T[newLen];
            Array.Copy(Head!, n, newHead, 0, newLen);
            return new RrbList<T>(Root, Tail, Count - n, Shift, TailLen, newHead, newLen);
        }

        return DropFrontSlow(n);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private RrbList<T> DropFrontSlow(int n)
    {
        if (n >= Count) return Empty;

        // No tree: everything is in the tail.
        if (Root == null)
        {
            var restCount = Count - n;
            var newTail = new T[restCount];
            Array.Copy(Tail, n, newTail, 0, restCount);
            return new RrbList<T>(null, newTail, restCount, 0, restCount);
        }

        // The head goes, and n - HeadLen more from the tree.
        var rest = HeadLen == 0 ? this : Headless();
        var fromTree = n - HeadLen;

        // What is left of the tree's first leaf becomes the head.
        if (fromTree < RrbAlgorithm.FirstLeafLen(rest.Root!, rest.Shift))
            return rest.PromoteFirstLeaf(fromTree);

        return rest.Slice(fromTree, rest.Count - fromTree);
    }

    /// <summary>
    ///     The item at <paramref name="index" />, looked for in the head first:
    ///     the items a match takes from the front are usually there.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T FrontItem(int index) => index < HeadLen ? Head![index] : this[index];

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    // Slice of a list with a head, longer than a head can be: the rest is
    // sliced, and what is kept of the head is put back. Out of line, so that
    // Slice on a list without a head pays nothing for it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private RrbList<T> SliceWithHead(int start, int count)
    {
        var rest = Headless();
        if (start >= HeadLen) return rest.Slice(start - HeadLen, count);

        var keptHead = HeadLen - start;
        var restSlice = rest.Slice(0, count - keptHead);

        T[] newHead;
        if (start == 0)
        {
            newHead = Head!;
        }
        else
        {
            newHead = new T[keptHead];
            Array.Copy(Head!, start, newHead, 0, keptHead);
        }

        return WithHead(newHead, keptHead, restSlice);
    }

    // This list without its head: the tree and the tail, indexed from 0.
    // Operations that do not touch the head work on this and put the head
    // back with WithHead.
    internal RrbList<T> Headless()
    {
        if (HeadLen == 0) return this;
        return new RrbList<T>(Root, Tail, Count - HeadLen, Shift, TailLen);
    }

    // `rest`, which has no head, with `headLen` items of `head` in front of it.
    // Keeps the invariant: when `rest` has no tree, the head joins its tail
    // if they fit in one leaf, and otherwise becomes the tree.
    internal static RrbList<T> WithHead(T[]? head, int headLen, RrbList<T> rest)
    {
        if (headLen == 0) return rest;
        var count = rest.Count + headLen;

        if (rest.Root != null)
            return new RrbList<T>(rest.Root, rest.Tail, count, rest.Shift, rest.TailLen, head, headLen);

        if (headLen + rest.TailLen <= Constants.RRB_BRANCHING)
        {
            var newTail = new T[count];
            Array.Copy(head!, 0, newTail, 0, headLen);
            Array.Copy(rest.Tail, 0, newTail, headLen, rest.TailLen);
            return new RrbList<T>(null, newTail, count, 0, count);
        }

        var leaf = new LeafNode<T>(ExactArray(head!, headLen), headLen, OwnerId.None);
        return new RrbList<T>(leaf, rest.Tail, count, 0, rest.TailLen);
    }

    // This list without its first `drop` items, which are all in the tree's
    // first leaf: what is left of that leaf becomes the head. Requires an
    // empty head, a tree, and `drop` at most the first leaf's length.
    internal RrbList<T> PromoteFirstLeaf(int drop)
    {
        var shift = Shift;
        var root = RrbAlgorithm.RemoveFirstLeaf(Root!, Shift, out var leaf);
        var treeItems = TreeCount - leaf.Len;
        root = RrbAlgorithm.LowerRoot(root, ref shift, treeItems);

        var rest = root == null
            ? new RrbList<T>(null, Tail, TailLen, 0, TailLen)
            : new RrbList<T>(root, Tail, Count - leaf.Len, shift, TailLen);

        var headLen = leaf.Len - drop;
        if (headLen == 0) return rest;

        T[] head;
        if (drop == 0)
        {
            head = leaf.Items;
        }
        else
        {
            head = new T[headLen];
            Array.Copy(leaf.Items, drop, head, 0, headLen);
        }

        return WithHead(head, headLen, rest);
    }

    // `root`, holding `treeSize` items, with the first `len` items of `items`
    // in front of it as a leaf. A full leaf goes down the left spine. A part
    // full one, or one the left spine has no room for, goes through Concat,
    // which repacks what it has to.
    internal static Node<T> PushInFront(Node<T> root, int treeSize, T[] items, int len, ref int shift)
    {
        var leaf = new LeafNode<T>(ExactArray(items, len), len, OwnerId.None);

        if (len == Constants.RRB_BRANCHING)
        {
            var newShift = shift;
            var pushed = RrbAlgorithm.TryPrependLeaf(root, leaf, ref newShift);
            if (pushed != null)
            {
                shift = newShift;
                return pushed;
            }
        }

        var (node, _) = RrbAlgorithm.Concat<T>(leaf, len, root, treeSize, 0, shift, out var concatShift);
        shift = concatShift;
        return node;
    }

    // The first `len` items of `items`, in an array of exactly that length.
    private static T[] ExactArray(T[] items, int len)
    {
        if (items.Length == len) return items;
        var exact = new T[len];
        Array.Copy(items, exact, len);
        return exact;
    }
}
