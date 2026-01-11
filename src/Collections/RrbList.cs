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
    public void CopyTo(int index, T[] array, int arrayIndex, int count)
{
    // 1. Validation
    if (array == null) throw new ArgumentNullException(nameof(array));
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
    if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
    if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
    if (array.Length - arrayIndex < count) throw new ArgumentException("Destination array is too small.");
    if (index > Count - count) throw new ArgumentException("Source range is invalid.");

    if (count == 0) return;

    int rootSize = Count - TailLen;
    int itemsCopied = 0;

    // 2. Copy from Root (Tree)
    if (Root != null && index < rootSize)
    {
        int countFromTree = Math.Min(count, rootSize - index);
        
        // Call the helper that mimics your CopyNode structure
        CopyRangeNode(Root, array, index, arrayIndex, countFromTree, Shift);
        
        itemsCopied = countFromTree;
    }

    // 3. Copy from Tail
    if (itemsCopied < count)
    {
        // Calculate start position relative to the Tail
        int tailStart = Math.Max(0, index - rootSize);
        int tailCount = count - itemsCopied;
        
        Array.Copy(Tail.Items, tailStart, array, arrayIndex + itemsCopied, tailCount);
    }
}

// The Helper: Structured exactly like your CopyNode, but handles ranges
private void CopyRangeNode(Node<T> node, T[] array, int srcIdx, int destIdx, int count, int shift)
{
    // Base Case: Leaf
    if (shift == 0)
    {
        var leaf = (LeafNode<T>)node;
        Array.Copy(leaf.Items, srcIdx, array, destIdx, count);
        return;
    }

    var internalNode = (InternalNode<T>)node;
    int currentDest = destIdx;
    int remainingCount = count;

    // RELAXED PATH (Matches your CopyNode structure)
    if (internalNode.SizeTable != null)
    {
        for (var i = 0; i < internalNode.Len; i++)
        {
            if (remainingCount <= 0) break;

            // Calculate child bounds
            int prevTotal = (i == 0) ? 0 : internalNode.SizeTable[i - 1];
            int childSize = internalNode.SizeTable[i] - prevTotal;

            // Does the requested range overlap this child?
            // We want data starting at 'srcIdx' (relative to this node)
            if (srcIdx < prevTotal + childSize)
            {
                // Calculate offset inside the child
                int offsetInChild = Math.Max(0, srcIdx - prevTotal);
                int copyAmount = Math.Min(remainingCount, childSize - offsetInChild);

                // REUSE: If taking the WHOLE child (from 0 to end), use your fast CopyNode
                if (offsetInChild == 0 && copyAmount == childSize)
                {
                    CopyNode(internalNode.Children[i]!, array, currentDest, shift - Constants.RRB_BITS);
                }
                else
                {
                    CopyRangeNode(internalNode.Children[i]!, array, offsetInChild, currentDest, copyAmount, shift - Constants.RRB_BITS);
                }

                currentDest += copyAmount;
                remainingCount -= copyAmount;
            }
        }
    }
    // DENSE PATH (Matches your CopyNode structure)
    else
    {
        var childStep = 1 << (shift - Constants.RRB_BITS); // Size of one child

        // Optimization: Jump directly to the first child involved
        int startChild = srcIdx / childStep;
        int offsetInChild = srcIdx % childStep;

        for (var i = startChild; i < internalNode.Len; i++)
        {
            if (remainingCount <= 0) break;

            // For dense nodes, children are size 'childStep' (unless it's the very last partial one)
            int copyAmount = Math.Min(remainingCount, childStep - offsetInChild);

            // REUSE: If taking the WHOLE child, use your fast CopyNode
            // (We check copyAmount >= childStep to handle the partial last child correctly)
            if (offsetInChild == 0 && copyAmount >= childStep)
            {
                CopyNode(internalNode.Children[i]!, array, currentDest, shift - Constants.RRB_BITS);
            }
            else
            {
                CopyRangeNode(internalNode.Children[i]!, array, offsetInChild, currentDest, copyAmount, shift - Constants.RRB_BITS);
            }

            currentDest += copyAmount;
            remainingCount -= copyAmount;
            
            // After the first child, subsequent children start at 0
            offsetInChild = 0; 
        }
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
    // Here we have an indexer that uses AVX for indexing into relaxed nodes. It is about 1.65x faster for a relaxed
    // operation.
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new IndexOutOfRangeException();

            // 1. Check Tail
            int tailOffset = Count - TailLen;
            if (index >= tailOffset) return Tail.Items[index - tailOffset];

            Node<T> node = Root!;
            int shift = Shift;

            if (shift == 0) 
                 return RrbAlgorithm.AsLeaf(node).Items[index];

            while ((node.Flags & NodeFlags.IsRelaxed) != 0)
            {
                var internalNode = Unsafe.As<InternalNode<T>>(node);
                var (childIndex, relativeIndex) = RrbAlgorithm.GetRelaxedIndexAvx(internalNode, index, shift);
            
                node = internalNode.Children[childIndex]!;
                index = relativeIndex;
                shift -= Constants.RRB_BITS;
            }

            while (shift > 0)
            {
                int childIndex = (index >> shift) & Constants.RRB_MASK;
                // Unsafe cast is safe here because shift > 0
                node = Unsafe.As<InternalNode<T>>(node).Children[childIndex]!;
                shift -= Constants.RRB_BITS;
            }

            // 4. Leaf Access
            return Unsafe.As<LeafNode<T>>(node).Items[index & Constants.RRB_MASK];
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

        RrbAlgorithm.Push(ref newRoot, ref newTail, item, ref newCnt, ref newTailLen, ref newShift, OwnerId.None);
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

        var newRoot = RrbAlgorithm.Update(Root!, index, value, Shift, OwnerId.None);
        return new RrbList<T>(newRoot, Tail, Count, Shift, TailLen);
    }


    /**
     * <summary>
     *     Creates a mutable builder from the current list.
     * </summary>
     * <returns>A new <see cref="RrbBuilder{T}" />.</returns>
     */
    public RrbBuilder<T> ToBuilder(int leafCapacify = Constants.RRB_BRANCHING)
    {
        return new RrbBuilder<T>(this, leafCapacify);
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
     *     Creates a slice of the list.
     * </summary>
     * <param name="start">The zero-based index at which to begin the slice.</param>
     * <param name="length">The number of elements in the slice.</param>
     * <returns>A new list that is a slice of the original list.</returns>
     */
    public RrbList<T> Slice(int start, int length)
{
    // 0. Validation
    if (length <= 0) return Empty;
    if (start < 0) start = 0;
    if (start + length > this.Count) length = this.Count - start;
    if (length == this.Count && start == 0) return this;

    // 1. SCALA-STYLE OPTIMIZATION (Tiny Slice)
    // If the result fits in a single leaf, just copy it.
    // This beats ALL tree logic (40-50ns range).
    if (length <= Constants.RRB_BRANCHING)
    {
        var items = new T[length];
        // Ensure you have a helper/iterator to copy range to array
        this.CopyTo(start, items, 0, length); 
        var smallTail = new LeafNode<T>(items, length, OwnerId.None);
        
        // Constructor: Root, Tail, Count, Shift, TailLength
        return new RrbList<T>(null, smallTail, length, 0, length);
    }

    // -----------------------------------------------------------
    // STEP 1: CUT RIGHT (Establish End & Promote Tail)
    // -----------------------------------------------------------
    // We keep the range [0 ... start+length].
    // buildLeft: true (We need the prefix)
    // buildRight: false (Discard the excess)
    var limit = start + length;
    var (tempRoot, tempTail, _) = RrbAlgorithm.MasterSplit(
         Root!, 
         limit, 
         Shift, 
         OwnerId.None, 
         buildLeft: true, 
         buildRight: false);

    // -----------------------------------------------------------
    // STEP 2: CUT LEFT (Drop Start)
    // -----------------------------------------------------------
    // We now drop the first 'start' items from the result of Step 1.
    
    Node<T>? finalRoot = null;
    LeafNode<T>? finalTail = tempTail; // Default to keeping the tail from Step 1
    int finalShift = Shift;

    // Calculate how many items are in the Tree portion vs the Tail portion
    // Note: tempRoot might be null if Step 1 resulted in only a Tail
    int rootSize = tempRoot != null ? RrbAlgorithm.GetTotalSize(tempRoot, Shift) : 0; 

    // CASE A: The cut is inside the Tree (Root survives)
    if (start < rootSize)
    {
        if (tempRoot != null)
        {
            // We use MasterSplit to "Drop" the prefix.
            // buildLeft: false (Trash the start items)
            // buildRight: true (Keep the rest of the tree)
            // Note: splitIndex is 'start' because we are slicing the result of Step 1
            var (_, _, rightResult) = RrbAlgorithm.MasterSplit(
                 tempRoot, 
                 start, 
                 Shift, 
                 OwnerId.None, 
                 buildLeft: false, 
                 buildRight: true);
                 
            finalRoot = rightResult;

            // Squash Logic:
            // If the tree became too tall for its contents (e.g. Root with 1 child), peel it.
            // This prevents "tall narrow" trees after heavy slicing.
            while (finalShift > 0 && finalRoot is InternalNode<T> internalNode && finalRoot.Len == 1)
            {
                finalRoot = internalNode.Children[0];
                finalShift -= Constants.RRB_BITS;
            }
        }
        // tempTail survives exactly as is.
    }
    // CASE B: The cut eats the entire Root and bites into the Tail
    // (This case is technically unreachable if length > 32 due to logic, but handled for safety)
    else
    {
        // The Root is gone.
        finalRoot = null;
        finalShift = 0; 
        
        int offsetInTail = start - rootSize;
        
        // Slice the tail if necessary
        if (offsetInTail > 0 && tempTail != null)
        {
            var newItems = new T[length];
            Array.Copy(tempTail.Items, offsetInTail, newItems, 0, length);
            finalTail = new LeafNode<T>(newItems, length, OwnerId.None);
        }
    }

    // Safety fallback
    if (finalRoot == null && finalTail == null) return Empty;
    if (finalRoot == null) finalShift = 0;
    if (finalTail == null) finalTail = LeafNode<T>.Empty;


    // Constructor: Root, Tail, Count, Shift, TailLength
    return new RrbList<T>(finalRoot, finalTail!, length, finalShift, finalTail!.Len);
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

                var newTail = new LeafNode<T>(newItems, newItems.Length, OwnerId.None);
                return new RrbList<T>(Root, newTail, Count + other.Count, Shift, newTail.Len);
            }

            var spaceInThis = Constants.RRB_BRANCHING - TailLen;
            var overflow = other.TailLen - spaceInThis;

            var newLeafItems = new T[Constants.RRB_BRANCHING];
            Array.Copy(Tail.Items, 0, newLeafItems, 0, TailLen);
            Array.Copy(other.Tail.Items, 0, newLeafItems, TailLen, spaceInThis);
            var newLeaf = new LeafNode<T>(newLeafItems, Constants.RRB_BRANCHING, OwnerId.None);

            var newTailItems = new T[overflow];
            Array.Copy(other.Tail.Items, spaceInThis, newTailItems, 0, overflow);
            var newActiveTail = new LeafNode<T>(newTailItems, overflow, OwnerId.None);

            var newShift = Shift;
            var newRoot = RrbAlgorithm.AppendLeafToTree(Root, newLeaf, ref newShift, OwnerId.None);

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




    /**
     * <summary>
     *     Splits the list into two at the specified index.
     * </summary>
     * <param name="index">The index at which to split the list.</param>
     * <returns>A tuple containing the left and right parts of the split list.</returns>
     */
    public (RrbList<T> Left, RrbList<T> Right) Split(int index)
{
    if (index <= 0) return (Empty, this);
    if (index >= Count) return (this, Empty);

    int rootSize = Count - TailLen;

    // ---------------------------------------------------------
    // CASE A: Split happens inside the Tail
    // ---------------------------------------------------------
    if (index >= rootSize)
    {
        // where in the tail to split
        int tailSplitIdx = index - rootSize;

        // Left Tail
        var leftTailItems = new T[tailSplitIdx];
        Array.Copy(Tail.Items, 0, leftTailItems, 0, tailSplitIdx);
        var leftTail1 = new LeafNode<T>(leftTailItems, tailSplitIdx, OwnerId.None);

        // Right Tail 
        int rightLen = TailLen - tailSplitIdx;
        var rightTailItems = new T[rightLen];
        Array.Copy(Tail.Items, tailSplitIdx, rightTailItems, 0, rightLen);
        var rightTail = new LeafNode<T>(rightTailItems, rightLen, OwnerId.None);

        // 2. Construct Lists
        // Left: Keeps the original Root and Shift, gets the new Left Tail.
        var left = new RrbList<T>(Root, leftTail1, index, Shift, leftTail1.Len);

        // Right: Has no root, contains only the Right Tail.
        var right = new RrbList<T>(null, rightTail, rightLen, 0, rightTail.Len);

        return (left, right);
    }

    // ---------------------------------------------------------
    // CASE B: Split happens inside the Tree
    // ---------------------------------------------------------
    // We split the Root. The 'Tail' of the left side will be promoted from the split point.
    // The 'Tail' of the right side will be the original Tail of this list.

    var (leftRoot, leftTail, rightRoot) = RrbAlgorithm.MasterSplit(
        Root!, 
        index, 
        Shift, 
        OwnerId.None, 
        buildLeft: true, 
        buildRight: true);

    // 1. Construct Left List
    // We must squash the Left Root to find the correct shift. At this point we could have 4 levels of 1 Child.
    // Squash fixes that
    var (finalLeftRoot, leftShift) = Squash(leftRoot, Shift);
  
    var leftList = new RrbList<T>(
        finalLeftRoot, 
        leftTail,      // Promoted from inside the tree
        index, 
        leftShift, 
        leftTail.Len);

    // 2. Construct Right List
    // The Right Root forms the tree part. The Original Tail is appended.
    var (finalRightRoot, rightShift) = Squash(rightRoot, Shift);
    var rightCount = Count - index;

    var rightList = new RrbList<T>(
        finalRightRoot, 
        Tail,           // The original tail survives
        rightCount, 
        rightShift, 
        TailLen);       // The original tail length

    return (leftList, rightList);
}
    // Reduces the height if the root has only 1 child.
    private static (Node<T>? Node, int Shift) Squash(Node<T>? node, int shift)
    {
        if (node == null) return (null, 0);

        // While we are an InternalNode with exactly 1 child, peel the layer.
        while (shift > 0 && node is InternalNode<T> internalNode && internalNode.Len == 1)
        {
            node = internalNode.Children[0];
            shift -= Constants.RRB_BITS;
        }
    
        // If we peeled all the way down to a Leaf but shift is still > 0 
        // (technically shouldn't happen if logic is correct, but safe guard):
        if (node is LeafNode<T>) shift = 0;

        return (node, shift);
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

        // Index is at the very end: delegate to Add
        if (index == Count) return Add(item);

        int tailOffset = Count - TailLen;

        // Insert into Tail
        if (index >= tailOffset)
        {
            // Tail has room (Simple Insert)
            if (TailLen < Constants.RRB_BRANCHING)
            {
                var newTailItems = new T[TailLen + 1];
                int idxInTail = index - tailOffset;

                if (idxInTail > 0)
                    Array.Copy(Tail.Items, 0, newTailItems, 0, idxInTail);

                newTailItems[idxInTail] = item;

                if (idxInTail < TailLen)
                    Array.Copy(Tail.Items, idxInTail, newTailItems, idxInTail + 1, TailLen - idxInTail);

                return new RrbList<T>(Root, new LeafNode<T>(newTailItems, TailLen + 1, OwnerId.None), Count + 1, Shift,
                    TailLen + 1);
            }
            // Or: Tail is Full (Split & Promote)
            else
            {
                // We have 32 items + 1 new item = 33 items.
                // We split them: [0..31] -> Promoted to Tree, [32] -> New Tail.

                var tempItems = new T[Constants.RRB_BRANCHING + 1];
                int idxInTail = index - tailOffset;

                // Construct the virtual 33-item array
                Array.Copy(Tail.Items, 0, tempItems, 0, idxInTail);
                tempItems[idxInTail] = item;
                Array.Copy(Tail.Items, idxInTail, tempItems, idxInTail + 1, TailLen - idxInTail);

                // Create the Full Node to push into tree
                var promotedItems = new T[Constants.RRB_BRANCHING];
                Array.Copy(tempItems, 0, promotedItems, 0, Constants.RRB_BRANCHING);
                var promotedLeaf = new LeafNode<T>(promotedItems, Constants.RRB_BRANCHING, OwnerId.None);

                // Create the New Tail (with the 1 remaining item)
                var newTailItems = new T[1];
                newTailItems[0] = tempItems[Constants.RRB_BRANCHING];
                var newTail = new LeafNode<T>(newTailItems, 1, OwnerId.None);

                // Update Tree
                int newShift = Shift;
                // Reuse the logic from Add/Push to grow the tree
                Node<T> newRoot = RrbAlgorithm.AppendLeafToTree(Root, promotedLeaf, ref newShift, OwnerId.None);

                return new RrbList<T>(newRoot, newTail, Count + 1, newShift, 1);
            }
        }

        // Insert into Tree (Recursive)
        // InsertRecursive only worries about the tree epart. 
        var result = RrbAlgorithm.InsertRecursive(Root!, index, item, Shift, OwnerId.None);

        Node<T> treeRoot = result.NewNode;
        int treeShift = Shift;

        // Handle Root Overflow
        if (result.Overflow != null)
        {
            treeShift += Constants.RRB_BITS;
            var children = new Node<T>[] { result.NewNode, result.Overflow };

            // Calculate sizes for the new root
            var sizes = new int[2];
            sizes[0] = RrbAlgorithm.GetTotalSize(result.NewNode, Shift);
            sizes[1] = sizes[0] + RrbAlgorithm.GetTotalSize(result.Overflow, Shift);

            treeRoot = new InternalNode<T>(children, sizes, 2, OwnerId.None);
        }

        return new RrbList<T>(treeRoot, Tail, Count + 1, treeShift, TailLen);
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
        if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

        // Simple case: Index is in the Tail
        int tailOffset = Count - TailLen;
        if (index >= tailOffset)
        {
            int indexInTail = index - tailOffset;
        
            // Create new tail array
            var newTailItems = new T[TailLen - 1];
            if (indexInTail > 0)
                Array.Copy(Tail.Items, 0, newTailItems, 0, indexInTail);
            if (indexInTail < TailLen - 1)
                Array.Copy(Tail.Items, indexInTail + 1, newTailItems, indexInTail, TailLen - indexInTail - 1);
            
            var newTail = new LeafNode<T>(newTailItems, newTailItems.Length, OwnerId.None);
            return new RrbList<T>(Root, newTail, Count - 1, Shift, newTail.Len);
        }

        // Sad case: Index is in the Tree
        // We execute a zip removal, which is always going to be faster than 
        // removing using slice, but may end up with a more unbalanced tree
    
        Node<T>? newRoot = RrbAlgorithm.RemoveRecursive(Root!, index, Shift);
        int newShift = Shift;

        // Handle Root Collapse (if root became a single child)
        // Only if we still have a tree (newRoot != null)
        while (newRoot != null && 
               newShift > 0 && 
               newRoot is InternalNode<T> inode && 
               inode.Len == 1)
        {
            // If the root has only 1 child, that child becomes the new root
            newRoot = inode.Children[0];
            newShift -= Constants.RRB_BITS;
        }
    
        // If newRoot became null (tree empty), we just have the tail remaining.
        if (newRoot == null)
        {
            // Shift should reset to 0 effectively, but we just pass 0.
            return new RrbList<T>(null, Tail, TailLen, 0, TailLen);
        }

        return new RrbList<T>(newRoot, Tail, Count - 1, newShift, TailLen);
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
            var newTail = new LeafNode<T>(newTailItems, TailLen - 1, OwnerId.None);

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
        {
            Console.WriteLine("BreakPoint");
            
            throw new Exception(
                $"Integrity Error: Root tracks {Count - TailLen} items, but traversal found {countedSize}.");
        }
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
                    {
                        Console.WriteLine("BreakPoint!");
                        throw new Exception(
                            $"Integrity Error: Balanced node has non-full child at index {i} (Size {childSize}/{capacity})");
                    }
            }
        }
    }
}