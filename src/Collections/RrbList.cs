/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Linus Björnstam
 *
 * Portions of this code are based on a port of c-rrb (https://github.com/hypirion/c-rrb),
 * Copyright (c) 2013-2014 Jean Niklas L'orange, licensed under the MIT License.
 */


using System.Runtime.CompilerServices;
using System.Text;

namespace Collections;

public sealed partial class RrbList<T> where T : notnull
{
    /**
     * <summary>
     *     Gets an empty <see cref="RrbList{T}" />.
     * </summary>
     */
    public static readonly RrbList<T> Empty = new();

    internal readonly Node<T>? Root;
    internal readonly int Shift;
    internal readonly LeafNode<T> Tail;
    internal readonly int TailLen;

    internal RrbList(Node<T>? root, LeafNode<T> tail, int cnt, int shift, int tailLen)
    {
        Root = root;
        Tail = tail;
        Count = cnt;
        Shift = shift;
        TailLen = tailLen;
    }

    /**
     * <summary>
     *     Initializes a new instance of the <see cref="RrbList{T}" /> class that is empty.
     * </summary>
     */
    public RrbList()
    {
        Root = null;
        Tail = LeafNode<T>.Empty;
        Count = 0;
        Shift = 0;
        TailLen = 0;
    }

    /**
     * <summary>
     *     Initializes a new instance of the <see cref="RrbList{T}" /> class that contains elements copied from the specified
     *     collection.
     * </summary>
     * <param name="items">The collection whose elements are copied to the new list.</param>
     */
    public RrbList(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (items is RrbList<T> other)
        {
            Root = other.Root;
            Tail = other.Tail;
            Count = other.Count;
            Shift = other.Shift;
            TailLen = other.TailLen;
            return;
        }

        RrbBuilder<T> builder;

        // TODO: benchmark where it makes sense to use a fat tail.
        if (items.Count() > 4096)
            builder = new RrbBuilder<T>(1024);
        else
            builder = new RrbBuilder<T>(32);

        foreach (var item in items) builder.Add(item);

        var temp = builder.ToImmutable();
        Root = temp.Root;
        Tail = temp.Tail;
        Count = temp.Count;
        Shift = temp.Shift;
        TailLen = temp.TailLen;
    }

    /**
     * <summary>
     *     Returns the current depth (height) of the tree.
     *     A depth of 0 means the list is empty or only has a tail.
     *     A depth of 1 means one level of leaves.
     * </summary>
     */
    public int Depth => Shift / Constants.RRB_BITS;

    /**
     * <summary>
     *     Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only.
     * </summary>
     */
    public bool IsReadOnly => true;

    // --- ICollection<T> Implementation ---

    /**
     * <summary>
     *     Copies the elements of the <see cref="RrbList{T}" /> to an <see cref="T:System.Array" />, starting at a particular
     *     <see cref="T:System.Array" /> index.
     * </summary>
     * <param name="array">
     *     The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied
     *     from <see cref="RrbList{T}" />. The <see cref="T:System.Array" /> must have
     * </param>
     * <param name="arrayIndex">The zero-based index in <paramref name="array" /> at which copying begins.</param>
     */
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array is too small.");

        if (Root != null) CopyNode(Root, array, arrayIndex, Shift);

        if (TailLen > 0)
        {
            var tailDest = arrayIndex + (Count - TailLen);
            Array.Copy(Tail.Items, 0, array, tailDest, TailLen);
        }
    }

    // Explicit interface implementations for mutation methods. They return void, and is thus 
    // incompatible with this.
    void ICollection<T>.Add(T item)
    {
        throw new NotSupportedException("RrbList is immutable.");
    }

    void ICollection<T>.Clear()
    {
        throw new NotSupportedException("RrbList is immutable.");
    }

    bool ICollection<T>.Remove(T item)
    {
        throw new NotSupportedException("RrbList is immutable.");
    }

    /**
     * <summary>
     *     Determines whether the list contains a specific value.
     * </summary>
     * <param name="item">The object to locate in the list.</param>
     * <returns>true if the item is found in the list; otherwise, false.</returns>
     * <remarks>This could be made faster.</remarks>
     */
    public bool Contains(T item)
    {
        foreach (var x in this)
            if (EqualityComparer<T>.Default.Equals(x, item))
                return true;
        return false;
    }

    /**
     * <summary>
     *     The number of elements in the list
     * </summary>
     */
    public int Count { get; }


    /**
     * <summary>
     *     Gets the element at the specified index.
     * </summary>
     * <param name="index">The zero-based index of the element to get.</param>
     * <returns>The element at the specified index.</returns>
     */
    // public T this[int index]
    // {
    //     get
    //     {
    //         if (index < 0 || index >= Count) throw new IndexOutOfRangeException();
    //
    //         var tailOffset = Count - TailLen;
    //         if (index >= tailOffset) return Tail.Items[index - tailOffset];
    //
    //         var current = Root!;
    //
    //         for (var shift = Shift; shift > 0; shift -= Constants.RRB_BITS)
    //         {
    //             var internalNode = (InternalNode<T>)current;
    //             int childIndex;
    //
    //             if (internalNode.SizeTable != null)
    //             {
    //                 childIndex = 0;
    //                 while (internalNode.SizeTable[childIndex] <= index) childIndex++;
    //                 if (childIndex > 0) index -= internalNode.SizeTable[childIndex - 1];
    //             }
    //             else
    //             {
    //                 childIndex = (index >> shift) & Constants.RRB_MASK;
    //             }
    //
    //             current = internalNode.Children[childIndex]!;
    //         }
    //
    //
    //         return ((LeafNode<T>)current).Items[index & Constants.RRB_MASK];
    //     }
    // }
    // Here we have an indexer that uses AVX for indexing into relaxed nodes. It is about 1.65x faster for a relaxed
    // operation.
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new IndexOutOfRangeException();
    
            int tailOffset = Count - TailLen;
            if (index >= tailOffset)
            {
                // Maybe use Unsafe.Add while we are at it?
                return Tail.Items[index - tailOffset];
            }
    
            Node<T> current = Root!;
    
            for (int shift = Shift; shift > 0; shift -= Constants.RRB_BITS)
            {
                // Uses an unsafe cast.
                var internalNode = RrbAlgorithm.AsInternal(current);
            
                // AVX-accelerated Lookup + Relative Index Calculation
                // This handles both Dense (bit shift) and Relaxed (AVX search) paths correctly.
                var (childIndex, relativeIndex) = RrbAlgorithm.GetChildIndexAvx(internalNode, index, shift);
    
                // Set index and current node.
                index = relativeIndex;
                current = internalNode.Children[childIndex]!;
            }
        
            // Final Leaf Access
            // We use 'index' directly because GetChildIndexAvx ensures it is relative 
            // to the start of this leaf. The mask could be elided, i think.
            return RrbAlgorithm.AsLeaf(current).Items[index & Constants.RRB_MASK];
        }
    }

    /**
     * <summary>
     *     Creates a new RRB-List from an <see cref="IEnumerable{T}" />.
     * </summary>
     * <param name="items">The items to create the list from.</param>
     * <returns>A new RRB-List containing the items.</returns>
     */
    public static RrbList<T> Create(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (items is RrbList<T> rrb) return rrb;
        if (items is ICollection<T> c && c.Count == 0) return Empty;
        return new RrbList<T>(items);
    }


    /**
     * <summary>
     *     Returns a new list with the specified item added to the end.
     * </summary>
     * <param name="item">The item to add.</param>
     * <returns>A new list with the item added.</returns>
     */
    public RrbList<T> Add(T item)
    {
        var newRoot = Root;
        var newTail = Tail;
        var newCnt = Count;
        var newTailLen = TailLen;
        var newShift = Shift;

        RrbAlgorithm.Push(ref newRoot, ref newTail, item, ref newCnt, ref newTailLen, ref newShift, null);
        return new RrbList<T>(newRoot, newTail, newCnt, newShift, newTailLen);
    }

    /**
     * <summary>
     *     Returns a new list with the element at the specified index replaced with the new value.
     * </summary>
     * <param name="index">The index of the element to replace.</param>
     * <param name="value">The new value for the element.</param>
     * <returns>A new list with the replaced item.</returns>
     */
    public RrbList<T> SetItem(int index, T value)
    {
        if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

        var tailOffset = Count - TailLen;
        if (index >= tailOffset)
        {
            var newTail = Tail.CloneAndSet(index - tailOffset, value);
            return new RrbList<T>(Root, newTail, Count, Shift, TailLen);
        }

        var newRoot = RrbAlgorithm.Update(Root!, index, value, Shift, null);
        return new RrbList<T>(newRoot, Tail, Count, Shift, TailLen);
    }


    /**
     * <summary>
     *     Creates a mutable builder from the current list.
     * </summary>
     * <returns>A new <see cref="RrbBuilder{T}" />.</returns>
     */
    public RrbBuilder<T> ToBuilder()
    {
        return new RrbBuilder<T>(this);
    }

    private void CopyNode(Node<T> node, T[] array, int offset, int shift)
    {
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;
            Array.Copy(leaf.Items, 0, array, offset, leaf.Len);
            return;
        }

        var internalNode = (InternalNode<T>)node;
        var currentOffset = offset;

        if (internalNode.SizeTable != null)
        {
            for (var i = 0; i < internalNode.Len; i++)
            {
                // For SizeTable, we need absolute offsets.
                // SizeTable[i] is CUMULATIVE count from start of NODE.
                // Start of child i = offset + (i==0 ? 0 : SizeTable[i-1])
                var prevCount = i == 0 ? 0 : internalNode.SizeTable[i - 1];
                CopyNode(internalNode.Children[i]!, array, offset + prevCount, shift - Constants.RRB_BITS);
            }
        }
        else
        {
            var step = 1 << shift;
            for (var i = 0; i < internalNode.Len; i++)
            {
                CopyNode(internalNode.Children[i]!, array, currentOffset, shift - Constants.RRB_BITS);
                currentOffset += step;
            }
        }
    }

    /**
     * <summary>
     *     Ensures the list has a valid Tail. If the Tail is empty, it promotes the
     *     rightmost leaf of the tree to become the new Tail.
     * </summary>
     */
    public RrbList<T> Normalize()
    {
        // If we already have a tail, or the tree is empty, we are fine.
        if (TailLen > 0 || Root == null) return this;

        // The tree is not null, but tail is empty. 
        // We must pop the rightmost leaf from Root.
        var (newRoot, newTail) = RrbAlgorithm.PromoteTail(Root, Shift, null);

        var newShift = Shift;

        // Height Reduction:
        // If the new root has only 1 child, and it is an internal node (Shift > 0), 
        // we can collapse the root.
        // (Note: If newRoot is null, loop doesn't run, shift becomes 0 naturally)
        while (newRoot != null &&
               newShift > 0 &&
               newRoot.Len == 1 &&
               newRoot is InternalNode<T> inode)
        {
            newRoot = inode.Children[0];
            newShift -= Constants.RRB_BITS;
        }

        return new RrbList<T>(newRoot, newTail, Count, newShift, newTail.Len);
    }

    /**
     * <summary>
     *     Applies an accumulator function over a sequence.
     * </summary>
     * <typeparam name="TState">The type of the accumulator value.</typeparam>
     * <param name="seed">The initial accumulator value.</param>
     * <param name="func">An accumulator function to be invoked on each element with the arguments (state, value)</param>
     * <returns>The final accumulator value.</returns>
     */
    public TState Fold<TState>(TState seed, Func<TState, T, TState> func)
    {
        var state = seed;

        // Fold over the tree part
        if (Root != null) state = FoldNode(Root, Shift, state, func);

        // Fold over the tail part
        if (TailLen > 0)
        {
            var items = Tail.Items;
            for (var i = 0; i < TailLen; i++) state = func(state, items[i]);
        }

        return state;
    }

    private TState FoldNode<TState>(Node<T> node, int shift, TState state, Func<TState, T, TState> func)
    {
        // Base case: We are at a leaf node
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;
            var items = leaf.Items;
            var len = leaf.Len;

            // Iterate directly over the array - extremely fast
            for (var i = 0; i < len; i++) state = func(state, items[i]);
            return state;
        }

        // Recursive step: We are at an internal node
        var internalNode = (InternalNode<T>)node;
        var lenInternal = internalNode.Len;

        for (var i = 0; i < lenInternal; i++)
            // Recurse down to children
            state = FoldNode(internalNode.Children[i]!, shift - Constants.RRB_BITS, state, func);

        return state;
    }

    /**
     * <summary>
     *     Creates a slice of the list.
     * </summary>
     * <param name="start">The zero-based index at which to begin the slice.</param>
     * <param name="count">The number of elements in the slice.</param>
     * <returns>A new list that is a slice of the original list.</returns>
     */
    public RrbList<T> Slice(int start, int count)
    {
        if (start < 0 || count < 0 || start + count > Count)
            throw new ArgumentOutOfRangeException();

        if (count == 0) return Empty;
        if (start == 0 && count == Count) return this;

        // Right Slice (Tree size check)
        var treeSize = Count - TailLen;
        LeafNode<T> resultTail;
        Node<T>? resultRoot;
        var newEnd = start + count;
        var resultShift = Shift;

        if (newEnd > treeSize)
        {
            var itemsFromTail = newEnd - treeSize;
            var newTailItems = new T[itemsFromTail];
            Array.Copy(Tail.Items, 0, newTailItems, 0, itemsFromTail);
            resultTail = new LeafNode<T>(newTailItems, itemsFromTail, null);
            resultRoot = Root;
        }
        else
        {
            resultTail = LeafNode<T>.Empty;
            resultRoot = RrbAlgorithm.Slice(Root!, newEnd, Shift);
        }

        // Left Slice
        if (start > 0)
        {
            if (resultRoot != null)
            {
                if (start >= treeSize && newEnd > treeSize)
                {
                    var offsetInTail = start - treeSize;
                    var tailSlice = new T[count];
                    Array.Copy(Tail.Items, offsetInTail, tailSlice, 0, count);
                    return new RrbList<T>(null, new LeafNode<T>(tailSlice, count, null), count, 0, count);
                }

                resultRoot = RrbAlgorithm.SliceLeft(resultRoot, start, resultShift);
            }
            else
            {
                var tailSlice = new T[count];
                Array.Copy(resultTail.Items, start, tailSlice, 0, count);
                resultTail = new LeafNode<T>(tailSlice, count, null);
            }
        }

        return new RrbList<T>(resultRoot, resultTail, count, resultShift, resultTail.Len); //.Normalize();
    }

    /**
     * <summary>
     *     Merges two lists together.
     * </summary>
     * <param name="other">The list to merge with the current list.</param>
     * <returns>A new list containing elements from both lists.</returns>
     */
    public RrbList<T> Merge(RrbList<T> other)
    {
        if (other.Count == 0) return this;
        if (Count == 0) return other;


        // Handle when all that we do is merge a tree with a tail
        if (other.Root == null)
        {
            if (TailLen + other.TailLen <= Constants.RRB_BRANCHING)
            {
                var newItems = new T[TailLen + other.TailLen];
                Array.Copy(Tail.Items, 0, newItems, 0, TailLen);
                Array.Copy(other.Tail.Items, 0, newItems, TailLen, other.TailLen);

                var newTail = new LeafNode<T>(newItems, newItems.Length, null);
                return new RrbList<T>(Root, newTail, Count + other.Count, Shift, newTail.Len);
            }

            var spaceInThis = Constants.RRB_BRANCHING - TailLen;
            var overflow = other.TailLen - spaceInThis;

            var newLeafItems = new T[Constants.RRB_BRANCHING];
            Array.Copy(Tail.Items, 0, newLeafItems, 0, TailLen);
            Array.Copy(other.Tail.Items, 0, newLeafItems, TailLen, spaceInThis);
            var newLeaf = new LeafNode<T>(newLeafItems, Constants.RRB_BRANCHING, null);

            var newTailItems = new T[overflow];
            Array.Copy(other.Tail.Items, spaceInThis, newTailItems, 0, overflow);
            var newActiveTail = new LeafNode<T>(newTailItems, overflow, null);

            var treeCountAfterPush = Count - TailLen + Constants.RRB_BRANCHING;

            var newShift = Shift;
            var newRoot = RrbAlgorithm.AppendLeafToTree(Root, newLeaf, ref newShift, null);

            return new RrbList<T>(newRoot, newActiveTail, Count + other.Count, newShift, overflow);
        }

        var newLeftShift = Shift;
        var treeCount = Count - TailLen;

        var leftTree = RrbAlgorithm.FlushTail(Root, Tail, treeCount, ref newLeftShift);
        var leftTreeShift = newLeftShift;
        int combinedShift;
        var combinedTree = RrbAlgorithm.Concat(leftTree, other.Root!, leftTreeShift, other.Shift, out combinedShift);

        return new RrbList<T>(combinedTree, other.Tail, Count + other.Count, combinedShift, other.TailLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHeight(Node<T> node)
    {
        if (node is LeafNode<T>) return 0;
        return 1 + GetHeight(((InternalNode<T>)node).Children[0]!);
    }


    /**
     * <summary>
     *     Splits the list into two at the specified index.
     * </summary>
     * <param name="index">The index at which to split the list.</param>
     * <returns>A tuple containing the left and right parts of the split list.</returns>
     */
    public (RrbList<T> Left, RrbList<T> Right) Split(int index)
    {
        if (index < 0 || index > Count) throw new IndexOutOfRangeException();
        if (index == 0) return (Empty, this);
        if (index == Count) return (this, Empty);


        // Case A: Split is inside the Tail
        var treeCount = Count - TailLen;
        if (index >= treeCount)
        {
            // Tree is fully in Left. Tail is split.
            var splitInTail = index - treeCount;
            var (tailL, tailR) = SplitTail(Tail, splitInTail);

            var left = new RrbList<T>(Root, tailL, index, Shift, tailL.Len);
            var right = new RrbList<T>(null, tailR, Count - index, 0, tailR.Len);
            return (left, right);
        }

        // Case B: Split is inside the Tree
        // 1. Split the Tree
        var (treeL, treeR) = RrbAlgorithm.Split(Root!, index, Shift, null);

        // 2. Left List gets treeL + empty tail (or we could try to fetch a tail from treeL?)
        // Simplest valid state: Left has empty tail.
        var leftList = new RrbList<T>(treeL, LeafNode<T>.Empty, index, Shift, 0);
        //.Normalize();
        // TODO: should we normalize here?

        // 3. Right List gets treeR + original Tail
        var rightList = new RrbList<T>(treeR, Tail, Count - index, Shift, TailLen);

        return (leftList, rightList);
    }

    private (LeafNode<T>, LeafNode<T>) SplitTail(LeafNode<T> tail, int idx)
    {
        var lArr = new T[idx];
        Array.Copy(tail.Items, 0, lArr, 0, idx);
        var lNode = new LeafNode<T>(lArr, idx, null);

        var rLen = tail.Len - idx;
        var rArr = new T[rLen];
        Array.Copy(tail.Items, idx, rArr, 0, rLen);
        var rNode = new LeafNode<T>(rArr, rLen, null);

        return (lNode, rNode);
    }

    /**
     * <summary>
     *     Inserts item at index.
     * </summary>
     * <param name="index">The zero-based index of the element to remove.</param>
     * <param name="item">The item to insert</param>
     * <returns>A new (unbalanced) list with the item inserted at index.</returns>
     */
    public RrbList<T> Insert(int index, T item)
    {
        if (index < 0 || index > Count) throw new IndexOutOfRangeException();
        if (index == Count) return Add(item);
        if (index == 0) return new RrbList<T>().Add(item).Merge(this);

        // Single traversal split
        var (left, right) = Split(index);

        var nel = left.Add(item);
        var ner = nel.Merge(right);
        return ner;
    }

    /**
     * <summary>
     *     Removes the element at the specified index.
     * </summary>
     * <param name="index">The zero-based index of the element to remove.</param>
     * <returns>A new list with the element removed.</returns>
     */
    public RrbList<T> RemoveAt(int index)
    {
        // One could think that this approach is slower than using Split(index) and pop.
        // but it usually isn't,

        if (index < 0 || index >= Count) throw new IndexOutOfRangeException();
        var left = Slice(0, index);
        var right = Slice(index + 1, Count - (index + 1));
        return left.Merge(right);
    }

    /**
     * <summary>
     *     Returns a new, fully compacted (dense) version of this list.
     *     This operation is O(N) as it rebuilds the tree structure.
     *     Use this if the tree depth becomes excessive due to repeated relaxed operations.
     * </summary>
     */
    public RrbList<T> Compact()
    {
        // If the tree is already empty or just a tail, 
        // it is already as compact as possible.
        if (Root == null) return this;

        // We use the builder because it guarantees a "Canonical" RRB tree
        // (Dense leaves, dense nodes, no SizeTables).
        var builder = new RrbBuilder<T>();

        // We iterate manually. Since RrbEnumerator is efficient, this is fast.
        foreach (var item in this) builder.Add(item);

        return builder.ToImmutable();
    }

    /**
     * <summary>
     *     Conditionally compacts the list if its depth exceeds the specified limit.
     * </summary>
     * <param name="maxDepth">The maximum allowed depth before compaction is triggered.</param>
     * <returns>A compacted list if the depth was exceeded; otherwise, the original list.</returns>
     */
    public RrbList<T> CompactIfTooDeep(int maxDepth = 6)
    {
        if (Depth > maxDepth) return Compact();
        return this;
    }

    /**
     * <summary>
     *     Returns a string representation with debug information
     * </summary>
     * <returns>A string that represents the current object.</returns>
     */
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RrbList<{typeof(T).Name}> (Cnt: {Count}, Height: {Shift / Constants.RRB_BITS})");

        if (TailLen > 0)
        {
            sb.Append("  [Tail]: ");
            PrintItems(sb, Tail.Items, TailLen);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("  [Tail]: <empty>");
        }

        if (Root != null)
        {
            sb.AppendLine("  [Tree Root]:");
            PrintNode(sb, Root, 1, Shift);
        }
        else
        {
            sb.AppendLine("  [Tree Root]: <null>");
        }

        return sb.ToString();
    }

    private void PrintNode(StringBuilder sb, Node<T> node, int indentLevel, int shift)
    {
        var indent = new string(' ', indentLevel * 2 + 2);

        if (node is LeafNode<T> leaf)
        {
            sb.Append($"{indent}Leaf (Len: {leaf.Len}): ");
            PrintItems(sb, leaf.Items, leaf.Len);
            sb.AppendLine();
        }
        else if (node is InternalNode<T> inode)
        {
            var tableInfo = inode.SizeTable != null
                ? $" [TABLE: {string.Join(", ", inode.SizeTable)}]"
                : " [Balanced]";

            sb.AppendLine($"{indent}Node (Len: {inode.Len}, Shift: {shift}){tableInfo}");

            for (var i = 0; i < inode.Len; i++)
                PrintNode(sb, inode.Children[i]!, indentLevel + 1, shift - Constants.RRB_BITS);
        }
    }

    private void PrintItems(StringBuilder sb, T[] items, int count)
    {
        sb.Append("[");
        var limit = Math.Min(count, 10);
        for (var i = 0; i < limit; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(items[i]);
        }

        if (count > limit) sb.Append($", ... and {count - limit} more");
        sb.Append("]");
    }


    /**
     * <summary>
     *     Removes the last element from the list.
     * </summary>
     * <returns>A new list with the last element removed.</returns>
     */
    public RrbList<T> Pop()
    {
        if (Count == 0) throw new InvalidOperationException("List is empty");

        // Fast Path: Just shrink the tail count
        if (TailLen > 1)
        {
            // Currently, tail is not in the RrbList, and there are lots of taillen and things passed
            // around. Whenever I get to integrating the tail, this can be optimized so that we just decrease the 
            // TailLen for value types. 
            var newTailItems = new T[TailLen - 1];
            Array.Copy(Tail.Items, 0, newTailItems, 0, TailLen - 1);
            var newTail = new LeafNode<T>(newTailItems, TailLen - 1, null);

            return new RrbList<T>(Root, newTail, Count - 1, Shift, TailLen - 1);
        }

        // Slow Path: Tail becomes empty.
        // We rely on Slice to find the new tail from the tree.
        // Slice(0, Cnt - 1) correctly promotes the rightmost leaf.
        return Slice(0, Count - 1);
    }

    /**
     * <summary>
     *     Removes the first element from the list.
     * </summary>
     * <returns>A new list with the first element removed.</returns>
     */
    public RrbList<T> PopFirst()
    {
        if (Count == 0) throw new InvalidOperationException("List is empty");

        return Slice(1, Count);
    }

    /**
     * <summary>
     *     Verifies the internal structural integrity of the RRB-Tree. Throws an exception if an inconsistency is found.
     * </summary>
     */
    public void VerifyIntegrity()
    {
        if (Root == null) return;
        VerifyNode(Root, Shift);

        // Also verify that the size of the tree matches the Count - TailLen
        var countedSize = CountNode(Root, Shift);
        if (countedSize != Count - TailLen)
            throw new Exception(
                $"Integrity Error: Root tracks {Count - TailLen} items, but traversal found {countedSize}.");
    }

    private int CountNode(Node<T> node, int shift)
    {
        if (shift == 0) return node.Len;
        var inode = (InternalNode<T>)node;
        var sum = 0;
        for (var i = 0; i < inode.Len; i++) sum += CountNode(inode.Children[i]!, shift - Constants.RRB_BITS);
        return sum;
    }

    private void VerifyNode(Node<T> node, int shift)
    {
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;
            if (leaf.Items.Length < leaf.Len)
                throw new Exception("Integrity Error: Leaf Len > Array Size");
            return;
        }

        var inode = (InternalNode<T>)node;
        var calculatedTotal = 0;

        for (var i = 0; i < inode.Len; i++)
        {
            var child = inode.Children[i]!;
            VerifyNode(child, shift - Constants.RRB_BITS);

            var childSize = CountNode(child, shift - Constants.RRB_BITS);
            calculatedTotal += childSize;

            // Verify SizeTable consistency if it exists
            if (inode.SizeTable != null)
            {
                if (inode.SizeTable[i] != calculatedTotal)
                    throw new Exception(
                        $"Integrity Error: SizeTable mismatch at index {i}. Table says {inode.SizeTable[i]}, actual sum is {calculatedTotal}");
            }
            else
            {
                // Verify Balanced Invariant
                // All children except the last must be full
                var capacity = 1 << shift;
                if (i < inode.Len - 1)
                    if (childSize != capacity)
                        throw new Exception(
                            $"Integrity Error: Balanced node has non-full child at index {i} (Size {childSize}/{capacity})");
            }
        }
    }
}