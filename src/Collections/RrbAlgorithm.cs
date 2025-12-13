using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Collections;

internal static class RrbAlgorithm
{
    // This is the hairy part compared to clojure's balanced tries. 
    // It alculates a redistribution plan for a set of nodes to restore the RRB tree invariants.
    // This method implements the "Search and Redistribute" algorithm found in the original C implementation.
    // It iterates through the list of children and identifies nodes that are underfilled. This is defined
    // by the constants RRB_BRANCHING - RRB_INVARIANT.
    //  
    // When an underfilled node is found, it "steals" items from the subsequent node to fill the current 
    // node up to capacity. This "Sliding Window" approach ensures that the resulting nodes are densely 
    // packed, preventing the tree from becoming sparse and deep after multiple concatenations.
    //
    // Returns An array of integers representing the target size (len) for each new node. 
    // For example, if the plan is [32, 32, 5],ExecuteConcatPlan will 
    // create three nodes containing 32, 32, and 5 items respectively.

    private static void CreateConcatPlan<T>(
        ReadOnlySpan<Node<T>> allChildren,
        Span<int> nodeCount,
        out int topLen)
    {
        long totalNodes = 0;

        for (var i = 0; i < allChildren.Length; i++)
        {
            var len = allChildren[i].Len;
            nodeCount[i] = len;
            totalNodes += len;
        }

        var optimalSlots = (totalNodes - 1) / Constants.RRB_BRANCHING + 1;
        var shuffledLen = allChildren.Length;
        var i_idx = 0;

        while (optimalSlots + Constants.RRB_EXTRAS < shuffledLen)
        {
            while (i_idx < shuffledLen && nodeCount[i_idx] > Constants.RRB_BRANCHING - Constants.RRB_INVARIANT) i_idx++;

            if (i_idx == shuffledLen) break;

            var remainingNodes = nodeCount[i_idx];
            do
            {
                if (i_idx + 1 >= shuffledLen) break;
                var minSize = Math.Min(remainingNodes + nodeCount[i_idx + 1], Constants.RRB_BRANCHING);
                nodeCount[i_idx] = minSize;
                remainingNodes = remainingNodes + nodeCount[i_idx + 1] - minSize;
                i_idx++;
            } while (remainingNodes > 0);

            for (var j = i_idx; j < shuffledLen - 1; j++) nodeCount[j] = nodeCount[j + 1];

            shuffledLen--;
            i_idx--;
        }

        topLen = shuffledLen;
        //return nodeCount;
    }


    public static Node<T> Update<T>(Node<T> root, int index, T value, int shift, OwnerToken? token)
    {
        // Tree is not a tree.
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)root;

            // Direct CloneAndSet for persistent
            if (token == null)
                return leaf.CloneAndSet(index & Constants.RRB_MASK, value);

            // Transient path
            leaf = leaf.EnsureEditable(token);
            leaf.Items[index & Constants.RRB_MASK] = value;
            return leaf;
        }

        var internalNode = (InternalNode<T>)root;

        var (childIndex, subIndex) = GetChildIndexAvx(internalNode, index, shift);

        if (childIndex >= internalNode.Len) throw new IndexOutOfRangeException();

        var child = internalNode.Children[childIndex]!;
        var newChild = Update(child, subIndex, value, shift - Constants.RRB_BITS, token);

        if (token == null)
            // Cone your way back up
            return internalNode.CloneAndSetChild(childIndex, newChild);

        internalNode = internalNode.EnsureEditable(token);
        internalNode.Children[childIndex] = newChild;
        return internalNode;
    }

    public static Node<T>? Slice<T>(Node<T> root, int newCount, int shift)
    {
        if (newCount == 0) return null;
        return SliceRightRec(root, newCount, shift);
    }

    private static Node<T> SliceRightRec<T>(Node<T> node, int limit, int shift)
    {
        if (shift == 0)
        {
            var leaf = AsLeaf(node);
            if (leaf.Len == limit) return leaf;

            var newItems = new T[limit];
            Array.Copy(leaf.Items, newItems, limit);
            return new LeafNode<T>(newItems, limit, null);
        }

        var internalNode = AsInternal(node);

        var (childIdx, indexInChild) = GetChildIndexAvx(internalNode, limit - 1, shift);

        // Convert 0-based index back to 1-based count for the recursive limit
        var limitInChild = indexInChild + 1;

        var child = internalNode.Children[childIdx]!;
        var slicedChild = SliceRightRec(child, limitInChild, shift - Constants.RRB_BITS);

        var newLen = childIdx + 1;
        var newChildren = new Node<T>?[newLen];
        Array.Copy(internalNode.Children, newChildren, newLen);
        newChildren[childIdx] = slicedChild;

        int[]? newSizeTable = null;
        if (internalNode.SizeTable != null)
        {
            newSizeTable = new int[newLen];
            Array.Copy(internalNode.SizeTable, newSizeTable, newLen);
            newSizeTable[childIdx] = limit;
        }

        return new InternalNode<T>(newChildren, newSizeTable, newLen, null);
    }


    // TODO: remove this and fix Merge.
    public static Node<T> FlushTail<T>(Node<T>? root, LeafNode<T> tail, int treeCount, ref int shift)
    {
        if (tail.Len == 0) return root!; // Caller should handle null

        // Reuse the append logic
        // We pass null for token because FlushTail is usually a persistent op (Merge)
        return AppendLeafToTree(root, tail, ref shift, null);
    }

    public static Node<T> Concat<T>(Node<T> leftNode, Node<T> rightNode, int leftShift, int rightShift,
        out int newShift)
    {
        // Left node is higher than right node
        if (leftShift > rightShift)
        {
            var left = AsInternal(leftNode);
            var lastChild = left.Children[left.Len - 1]!;

            int subShift;
            var mergedMid = Concat(lastChild, rightNode, leftShift - Constants.RRB_BITS, rightShift, out subShift);

            // Pass subShift to Rebalance
            return Rebalance(left, mergedMid, null, leftShift, subShift, out newShift);
        }

        // Right node is higher than left node
        if (leftShift < rightShift)
        {
            var right = AsInternal(rightNode);
            var firstChild = right.Children[0]!;

            int subShift;
            var mergedMid = Concat(leftNode, firstChild, leftShift, rightShift - Constants.RRB_BITS, out subShift);

            // Pass subShift to Rebalance
            return Rebalance(null, mergedMid, right, rightShift, subShift, out newShift);
        }

        // Same height. RightSHift is the same here
        if (leftShift == 0)
        {
            // Both are leaves.
            // If they fit in one leaf, merge them.
            var leftLeaf = AsLeaf(leftNode);
            var rightLeaf = AsLeaf(rightNode);

            if (leftLeaf.Len + rightLeaf.Len <= Constants.RRB_BRANCHING)
            {
                var newItems = new T[leftLeaf.Len + rightLeaf.Len];
                Array.Copy(leftLeaf.Items, 0, newItems, 0, leftLeaf.Len);
                Array.Copy(rightLeaf.Items, 0, newItems, leftLeaf.Len, rightLeaf.Len);

                newShift = 0; // Still a leaf
                return new LeafNode<T>(newItems, newItems.Length, null);
            }

            // Cannot merge into one leaf. Create parent.
            newShift = Constants.RRB_BITS;
            var parent = new InternalNode<T>(2, null);
            parent.Children[0] = leftNode;
            parent.Children[1] = rightNode;
            return parent;
        }
        else // This is not required, but with the scope created, we can reuse left and right names.
        {
            // Here we know the nodes are internal, so do an unsafe cast.
            var left = AsInternal(leftNode);
            var right = AsInternal(rightNode);
            var midLeft = left.Children[left.Len - 1]!;
            var midRight = right.Children[0]!;

            int subShift;
            var mergedMid = Concat(midLeft, midRight, leftShift - Constants.RRB_BITS, rightShift - Constants.RRB_BITS,
                out subShift);

            // Pass subShift
            return Rebalance(left, mergedMid, right, leftShift, subShift, out newShift);
        }
    }

// Returns (LeftNode, RightNode) split at the given index.
    public static (Node<T>? Left, Node<T>? Right) Split<T>(Node<T> root, int splitIndex, int shift,
        OwnerToken? token)
    {
        if (shift == 0)
        {
            var leaf = AsLeaf(root);
            if (splitIndex == 0) return (null, leaf);
            if (splitIndex == leaf.Len) return (leaf, null);

            var rightLen = leaf.Len - splitIndex;

            var leftItems = new T[splitIndex];
            Array.Copy(leaf.Items, 0, leftItems, 0, splitIndex);
            var leftNode = new LeafNode<T>(leftItems, splitIndex, token);

            var rightItems = new T[rightLen];
            Array.Copy(leaf.Items, splitIndex, rightItems, 0, rightLen);
            var rightNode = new LeafNode<T>(rightItems, rightLen, token);

            return (leftNode, rightNode);
        }
        
        // Shift 0 is handled. We are above the leaves, and can do unsafe casts.
        var internalNode = AsInternal(root);
        var (childIdx, splitInChild) = GetChildIndexAvx(internalNode, splitIndex, shift);
        var (childLeft, childRight) = Split(internalNode.Children[childIdx]!, splitInChild,
            shift - Constants.RRB_BITS, token);

        Node<T>? leftParent = null;
        if (childIdx > 0 || childLeft != null)
        {
            var newLeftLen = childIdx + (childLeft != null ? 1 : 0);
            var newLeftChildren = new Node<T>?[newLeftLen];

            if (childIdx > 0) Array.Copy(internalNode.Children, 0, newLeftChildren, 0, childIdx);
            if (childLeft != null) newLeftChildren[childIdx] = childLeft;

            var tempLeft = new InternalNode<T>(newLeftChildren, null, newLeftLen, token);
            leftParent = SetSizes(tempLeft, shift);
        }

        Node<T>? rightParent = null;
        var rightSiblings = internalNode.Len - (childIdx + 1);
        if (rightSiblings > 0 || childRight != null)
        {
            var newRightLen = (childRight != null ? 1 : 0) + rightSiblings;
            var newRightChildren = new Node<T>?[newRightLen];

            var offset = 0;
            if (childRight != null)
            {
                newRightChildren[0] = childRight;
                offset = 1;
            }

            if (rightSiblings > 0)
                Array.Copy(internalNode.Children, childIdx + 1, newRightChildren, offset, rightSiblings);

            var tempRight = new InternalNode<T>(newRightChildren, null, newRightLen, token);
            rightParent = SetSizes(tempRight, shift);
        }

        return (leftParent, rightParent);
    }


    private static Node<T> Rebalance<T>(
        InternalNode<T>? left,
        Node<T> center,
        InternalNode<T>? right,
        int shift,
        int centerShift,
        out int newShift)
    {
        // Max children: 32 (left) + 32 (right) + 1 (center) = 65.
        // Sometimes this is 67, but this should suffice
        var allChildren = new Node<T>[Constants.RRB_BRANCHING * 2 + 1];
        var count = 0;

        if (left != null)
            for (var i = 0; i < left.Len - 1; i++)
                allChildren[count++] = left.Children[i]!;

        if (centerShift == shift)
        {
            var cInternal = (InternalNode<T>)center;
            for (var i = 0; i < cInternal.Len; i++)
                allChildren[count++] = cInternal.Children[i]!;
        }
        else
        {
            allChildren[count++] = center;
        }

        if (right != null)
            for (var i = 1; i < right.Len; i++)
                allChildren[count++] = right.Children[i]!;

        // Slice the span to the actual count
        var childrenSlice = new ReadOnlySpan<Node<T>>(allChildren, 0, count);

        // CreateConcatPlan changes "plan" in place.
        Span<int> plan = stackalloc int[count];
        CreateConcatPlan(childrenSlice, plan, out var topLen);

        var newAll = ExecuteConcatPlan(childrenSlice, plan, topLen, shift);

        if (topLen <= Constants.RRB_BRANCHING)
        {
            newShift = shift;
            return SetSizes(newAll, shift);
        }

        // Fixme: find a better way to preserve the sizetable
        // Split into two
        var newLeft = CopyInternal(newAll, 0, Constants.RRB_BRANCHING);
        var newRight = CopyInternal(newAll, Constants.RRB_BRANCHING, topLen - Constants.RRB_BRANCHING);

        newLeft = SetSizes(newLeft, shift);
        newRight = SetSizes(newRight, shift);

        newShift = shift + Constants.RRB_BITS;
        var parent = new InternalNode<T>(2, null);
        parent.Children[0] = newLeft;
        parent.Children[1] = newRight;
        return SetSizes(parent, newShift);
    }

    private static InternalNode<T> ExecuteConcatPlan<T>(ReadOnlySpan<Node<T>> all, Span<int> plan, int slen, int shift)
    {
        var newChildren = new Node<T>?[slen];
        var idx = 0;
        var offset = 0;

        var shufflingLeaves = shift == Constants.RRB_BITS;

        for (var i = 0; i < slen; i++)
        {
            var newSize = plan[i];
            
            if (offset == 0 && idx < all.Length && all[idx].Len == newSize)
            {
                newChildren[i] = all[idx]; // <--- Zero allocation, O(1)
                idx++;
                continue;
            }

            if (shufflingLeaves)
            {
                var newItems = new T[newSize];
                var curSize = 0;

                while (curSize < newSize)
                {
                    var srcLeaf = AsLeaf(all[idx]);
                    var available = srcLeaf.Len - offset;
                    var toCopy = Math.Min(available, newSize - curSize);

                    Array.Copy(srcLeaf.Items, offset, newItems, curSize, toCopy);

                    curSize += toCopy;
                    offset += toCopy;
                    if (offset == srcLeaf.Len)
                    {
                        idx++;
                        offset = 0;
                    }
                }

                newChildren[i] = new LeafNode<T>(newItems, newSize, null);
            }
            else
            {
                var newSubChildren = new Node<T>?[newSize];
                var curSize = 0;

                while (curSize < newSize)
                {
                    // If 'all[idx]' is a LeafNode here, something is wrong with the shift logic
                    // or we are dealing with the "center split" logic incorrectly.
                    // Assuming all[idx] is InternalNode
                    var srcInternal = (InternalNode<T>)all[idx];
                    var available = srcInternal.Len - offset;
                    var toCopy = Math.Min(available, newSize - curSize);

                    Array.Copy(srcInternal.Children, offset, newSubChildren, curSize, toCopy);

                    curSize += toCopy;
                    offset += toCopy;
                    if (offset == srcInternal.Len)
                    {
                        idx++;
                        offset = 0;
                    }
                }

                var newNode = new InternalNode<T>(newSubChildren, null, newSize, null);
                newChildren[i] = SetSizes(newNode, shift - Constants.RRB_BITS);
            }
        }

        return new InternalNode<T>(newChildren, null, slen, null);
    }


    
    private static InternalNode<T> SetSizes<T>(InternalNode<T> node, int shift)
    {
        Span<int> sizes = stackalloc int[node.Len];
        var sum = 0;
        var childShift = shift - Constants.RRB_BITS;

        // Detect if the node is balanced (Standard Vector Trie).
        // A node is balanced if all children (except possibly the last one) 
        // are fully populated (size == 1 << shift).
        var isBalanced = true;
        var expectedBlockSize = 1 << shift;

        for (var i = 0; i < node.Len; i++)
        {
            var child = node.Children[i]!;
            sum += CountTree(child, childShift);
            sizes[i] = sum;

            // If we are not at the last child, this child MUST be full for the node to be balanced.
            if (i < node.Len - 1)
                // The cumulative sum at index 'i' must be exactly (i + 1) * BlockSize
                if (sum != (i + 1) * expectedBlockSize)
                    isBalanced = false;

            // If a child is Relaxed (has a SizeTable), THIS node must also be Relaxed (have a SizeTable).
            // Because we need to subtract offsets before entering the relaxed child.
            if (child is InternalNode<T> internalChild && internalChild.SizeTable != null) isBalanced = false;
        }

        // If balanced, discard the array and pass null.
        // This enables the fast-path bit-shift indexing in RrbList.
        return new InternalNode<T>(node.Children, isBalanced ? null : sizes.ToArray(), node.Len, null);
    }

    
    private static int CountTree<T>(Node<T> node, int shift)
    {
        int totalSize = 0;

        // Iterate down the rightmost edge until we hit a leaf or a relaxed node
        while (shift > 0)
        {
            // Optimization: Use unsafe cast for speed, we know it's Internal because shift > 0
            var internalNode = AsInternal(node);

            // Fast Path: Relaxed Node
            // If the node has a SizeTable, we can stop immediately. The table holds the accurate total count.
            if (internalNode.SizeTable != null)
            {
                return totalSize + internalNode.SizeTable[internalNode.Len - 1];
            }

            // Dense Path
            // We know that in a dense node, all children except the last one are fully populated.
            // We calculate the size of the full siblings and accumulate it.
            // Math: (ChildCount - 1) * CapacityOfOneChild
            totalSize += (internalNode.Len - 1) * (1 << shift);

            // Move down to the last child to continue counting
            node = internalNode.Children[internalNode.Len - 1]!;
            shift -= Constants.RRB_BITS;
        }

        // Base Case: Leaf Node
        // We add the actual number of elements in the final leaf.
        return totalSize + node.Len;
    }

    private static InternalNode<T> CopyInternal<T>(InternalNode<T> orig, int start, int len)
    {
        var newArr = new Node<T>?[len];
        Array.Copy(orig.Children, start, newArr, 0, len);
        return new InternalNode<T>(newArr, null, len, null);
    }

    public static Node<T> SliceLeft<T>(Node<T> root, int toDrop, int shift)
    {
        if (toDrop == 0) return root;
        // Note: Caller guarantees toDrop < total count
        return SliceLeftRec(root, toDrop, shift);
    }

    private static Node<T> SliceLeftRec<T>(Node<T> node, int toDrop, int shift)
    {
        // Base Case: Leaf
        if (shift == 0)
        {
            var leaf = AsLeaf(node);
            var newLen = leaf.Len - toDrop;
            var newItems = new T[newLen];
            Array.Copy(leaf.Items, toDrop, newItems, 0, newLen);
            return new LeafNode<T>(newItems, newLen, null);
        }

        var internalNode = AsInternal(node);
        var (subidx, dropInChild) = GetChildIndexAvx(internalNode, toDrop, shift);


        // Reconstruct Children Array
        // We discard children [0...subidx-1]
        // We slice child [subidx]
        // We keep children [subidx+1...end]

        var remainingChildren = internalNode.Len - subidx;
        var newChildren = new Node<T>?[remainingChildren];

        // Handle the split child
        // if dropInChild == 0, we keep the whole child.
        if (dropInChild > 0)
            newChildren[0] = SliceLeftRec(internalNode.Children[subidx]!, dropInChild, shift - Constants.RRB_BITS);
        else
            newChildren[0] = internalNode.Children[subidx];

        // Copy remaining siblings
        if (remainingChildren > 1) Array.Copy(internalNode.Children, subidx + 1, newChildren, 1, remainingChildren - 1);

        // Rebuild Size Table
        // If we slice from the left, indices shift, so we almost always need a SizeTable.
        // Exception: If we dropped exact whole subtrees from a balanced node, it stays balanced!
        var staysBalanced = internalNode.SizeTable == null && dropInChild == 0;

        int[]? newSizeTable = null;
        if (!staysBalanced)
        {
            newSizeTable = new int[remainingChildren];

            if (internalNode.SizeTable != null)
            {
                // Adjust existing table
                for (var i = 0; i < remainingChildren; i++)
                    newSizeTable[i] = internalNode.SizeTable[subidx + i] - toDrop;
            }
            else
            {
                // Create table from balanced assumptions
                var childCapacity = 1 << shift;
                for (var i = 0; i < remainingChildren; i++)
                {
                    // Original cumulative size was (subidx + i + 1) * capacity
                    var oldCumulative = (long)(subidx + i + 1) * childCapacity;
                    newSizeTable[i] = (int)(oldCumulative - toDrop);
                }
            }
        }

        return new InternalNode<T>(newChildren, newSizeTable, remainingChildren, null);
    }


    public static void Push<T>(
        ref Node<T>? root,
        ref LeafNode<T> tail,
        T element,
        ref int cnt,
        ref int tailLen,
        ref int shift,
        OwnerToken? token)
    {
        //  Try to fit into the active tail buffer
        if (tailLen < Constants.RRB_BRANCHING)
        {
            tail = tail.EnsureEditable(token);

            // If persistent and full-width (from previous transient owner?), ensure capacity
            // (This happens if a transient node was frozen but kept its 32-size array)
            if (token == null && tail.Items.Length == tailLen)
            {
                var newItems = new T[tailLen + 1];
                Array.Copy(tail.Items, newItems, tailLen);
                tail = new LeafNode<T>(newItems, tailLen, null);
            }

            tail.Items[tailLen] = element;
            tail.Len++;
            tailLen++;
            cnt++;
            return;
        }

        // Tail is full. 
        // We must promote the 'oldTail' into the tree and start a new tail.
        var oldTailToPush = tail;

        // Create the new active tail with the new element
        var newTail = new LeafNode<T>(1, token);
        newTail.Items[0] = element;

        // Update references
        tail = newTail;
        tailLen = 1;
        cnt++; // Total count increases by 1 (the new element)

        // Delegate Tree Insertion
        // AppendLeafToTree handles:
        // - Null Root
        // - Root Growth (Leaf -> Internal)
        // - Tree Height Growth (Overflow)
        root = AppendLeafToTree(root, oldTailToPush, ref shift, token);
    }


// Returns the updated node if the tail could be inserted/merged.
// Returns NULL if the node is physically full and the tail could not be accepted.
    private static Node<T>? TryPushDownTail<T>(Node<T> node, LeafNode<T> tailToInsert, int shift, OwnerToken? token)
    {
        var internalNode = AsInternal(node);

        // A. Base Case: Parent of Leaves (Shift == 5)
        if (shift == Constants.RRB_BITS)
        {
            // 1. Try to MERGE into the last child
            if (internalNode.Len > 0)
            {
                var lastChild = AsLeaf(internalNode.Children[internalNode.Len - 1]!);

                // Check if there is room in the last leaf
                if (lastChild.Len < Constants.RRB_BRANCHING)
                    // Check if we can fit the whole tail
                    if (lastChild.Len + tailToInsert.Len <= Constants.RRB_BRANCHING)
                    {
                        var mergedLeaf = MergeLeaves(lastChild, tailToInsert, token);

                        // Update Parent (Replace existing child)
                        // If Persistent: EnsureEditable(null) returns exact-fit copy -> Safe to replace [Len-1]
                        var editable = internalNode.EnsureEditable(token);
                        editable.Children[editable.Len - 1] = mergedLeaf;

                        if (editable.SizeTable != null) editable.SizeTable[editable.Len - 1] += tailToInsert.Len;
                        return editable;
                    }
            }

            // Try to APPEND a new child
            if (internalNode.Len < Constants.RRB_BRANCHING)
                return AppendChild(internalNode, tailToInsert, shift, token);

            // Full. Let caller handle height growth.
            return null;
        }


        // Recursive Step: Internal Nodes (Shift > 5)
        // Try to push into the last child (Recursion)
        if (internalNode.Len > 0)
        {
            var lastChild = internalNode.Children[internalNode.Len - 1]!;
            var newLastChild = TryPushDownTail(lastChild, tailToInsert, shift - Constants.RRB_BITS, token);

            if (newLastChild != null)
            {
                // Success down below. Replace existing child.
                // If Persistent: EnsureEditable(null) returns exact-fit copy -> Safe to replace [Len-1]
                var editable = internalNode.EnsureEditable(token);
                editable.Children[editable.Len - 1] = newLastChild;

                if (editable.SizeTable != null) editable.SizeTable[editable.Len - 1] += tailToInsert.Len;
                return editable;
            }
        }

        // Child was full. Try to APPEND a new Path.
        if (internalNode.Len < Constants.RRB_BRANCHING)
        {
            var newPath = CreatePath(shift - Constants.RRB_BITS, tailToInsert, token);
            return AppendChild(internalNode, newPath, shift, token);
        }

        // Completely full. Let the caller grow the tree.
        return null;
    }

    private static InternalNode<T> AppendChild<T>(InternalNode<T> node, Node<T> childToAdd, int shift,
        OwnerToken? token)
    {
        //  CHECK FOR DENSITY VIOLATION
        // if pushing a tail into a dense tree where the last leaf is not completely full
        // This fixes that. 
        var forceRelaxed = false;

        if (node.SizeTable == null && node.Len > 0)
        {
            var lastChild = node.Children[node.Len - 1]!;
            if (shift == Constants.RRB_BITS)
            {
                if (lastChild.Len < Constants.RRB_BRANCHING)
                    forceRelaxed = true;
            }
            else if (lastChild is InternalNode<T> inode && inode.SizeTable != null)
            {
                forceRelaxed = true;
            }
        }

        // TRANSIENT PATH
        if (token != null)
        {
            var editable = node;

            if (forceRelaxed && node.SizeTable == null)
                editable = CreateNodeWithSizeTable(node, token, shift);
            else
                editable = node.EnsureEditable(token);

            editable.Children[editable.Len] = childToAdd;

            if (editable.SizeTable != null)
            {
                var prevTotal = editable.Len > 0 ? editable.SizeTable[editable.Len - 1] : 0;
                var addedSize = GetTotalSize(childToAdd, shift - Constants.RRB_BITS);
                editable.SizeTable[editable.Len] = prevTotal + addedSize;
            }

            editable.Len++;
            return editable;
        }

        // PERSISTENT PATH

        var newLen = node.Len + 1;
        var newChildren = new Node<T>?[newLen];
        Array.Copy(node.Children, newChildren, node.Len);
        newChildren[node.Len] = childToAdd;

        int[]? newSizeTable = null;

        if (node.SizeTable != null || forceRelaxed)
        {
            newSizeTable = new int[newLen];

            if (node.SizeTable != null)
            {
                Array.Copy(node.SizeTable, newSizeTable, node.Len);
            }
            else
            {
                // At shift 5, a full child (leaf) has size 32 (1<<5).
                var blockSize = 1 << shift;
                var childShift = shift - Constants.RRB_BITS;

                for (var i = 0; i < node.Len; i++)
                {
                    var prevSum = i == 0 ? 0 : newSizeTable[i - 1];

                    // We only need to calculate the specific size if it's the last child 
                    // (which caused the relaxation). Otherwise, we know it's a full block.
                    var childSize = i == node.Len - 1
                        ? GetTotalSize(node.Children[i]!, childShift)
                        : blockSize;

                    newSizeTable[i] = prevSum + childSize;
                }
            }

            var prevTotal = node.Len > 0 ? newSizeTable[node.Len - 1] : 0;
            var addedSize = GetTotalSize(childToAdd, shift - Constants.RRB_BITS);
            newSizeTable[node.Len] = prevTotal + addedSize;
        }

        return new InternalNode<T>(newChildren, newSizeTable, newLen, null);
    }

// Helper to "Upgrade" a mutable dense node to a mutable relaxed node
    private static InternalNode<T> CreateNodeWithSizeTable<T>(InternalNode<T> node, OwnerToken? token, int shift)
    {
        // Calculate sizes for existing children
        var childShift = shift - Constants.RRB_BITS;
        var blockSize = 1 << shift;
        var newTable = new int[Constants.RRB_BRANCHING]; // Full capacity for transient

        var sum = 0;
        for (var i = 0; i < node.Len; i++)
        {
            // If we are here, the last child is likely the sparse one.
            if (i == node.Len - 1)
                sum += GetTotalSize(node.Children[i]!, childShift);
            else
                sum += blockSize;

            newTable[i] = sum;
        }

        var newChildren = new Node<T>?[Constants.RRB_BRANCHING];
        Array.Copy(node.Children, newChildren, node.Len);

        return new InternalNode<T>(newChildren, newTable, node.Len, token);
    }

// Helper to get size without crashing. That was a thin
    internal static int GetTotalSize<T>(Node<T> node, int shift)
    {
        if (shift == 0) return node.Len;
        if (node is InternalNode<T> inode && inode.SizeTable != null)
            return inode.SizeTable[inode.Len - 1];

        // It's a dense node.
        // However, if we are calling this on a child that forced relaxation, 
        // it might be a Dense node with 32 children where the last one is sparse
        var denseNode = AsInternal(node);
        var fullParams = (denseNode.Len - 1) * (1 << shift);
        var lastChildSize = GetTotalSize(denseNode.Children[denseNode.Len - 1]!, shift - Constants.RRB_BITS);
        return fullParams + lastChildSize;
    }


    private static LeafNode<T> MergeLeaves<T>(LeafNode<T> left, LeafNode<T> right, OwnerToken? token)
    {
        if (token != null && left.Owner == token)
        {
            // Transient: Mutate Left
            // Ensure array capacity
            if (left.Items.Length < left.Len + right.Len)
            {
                var newArr = new T[Constants.RRB_BRANCHING];
                Array.Copy(left.Items, newArr, left.Len);
                left.Items = newArr;
            }

            Array.Copy(right.Items, 0, left.Items, left.Len, right.Len);
            left.Len += right.Len;
            return left;
        }

        // Persistent
        var newItems = new T[left.Len + right.Len];
        Array.Copy(left.Items, 0, newItems, 0, left.Len);
        Array.Copy(right.Items, 0, newItems, left.Len, right.Len);
        return new LeafNode<T>(newItems, newItems.Length, null);
    }

    public static Node<T> AppendLeafToTree<T>(Node<T>? root, LeafNode<T> leafToPush, ref int shift, OwnerToken? token)
    {
        if (root == null)
        {
            shift = 0;
            return leafToPush;
        }

        // Special Case: Root is a leaf (Shift 0) -> Turn into tree (Shift 5)
        if (shift == 0)
        {
            shift = Constants.RRB_BITS;
            return CreateNewParent(root, leafToPush, token);
        }

        var newRoot = TryPushDownTail(root, leafToPush, shift, token);

        if (newRoot != null) return newRoot;

        // Everything from here is Root overflow.
        // at this point, we know that root is an InternalNode due to TryPushDownTail failing.
        var oldRootInode = AsInternal(root);


        // We get the size of the old root.
        var rootTotalSize = oldRootInode.SizeTable != null
            ? oldRootInode.SizeTable[oldRootInode.Len - 1]
            : CountTree(oldRootInode, shift); // Use optimized count if dense

        // Calculate theoretical capacity of a full node at this level
        //    (e.g., if shift is 5, a full node holds 32 * 32 = 1024 items)
        var rootCapacity = 1 << (shift + Constants.RRB_BITS);

        // Now the stinky stuff:
        // Determine if we need a SizeTable for the new parent
        int[]? newSizeTable = null;

        // We need a table if the old root was explicitly relaxed OR 
        // if it simply wasn't fully populated (like if we pushed a tail when
        // the rightmost node wasn't full. 
        if (oldRootInode.SizeTable != null || rootTotalSize != rootCapacity)
        {
            newSizeTable = new int[token != null ? Constants.RRB_BRANCHING : 2];
            newSizeTable[0] = rootTotalSize;
            newSizeTable[1] = rootTotalSize + leafToPush.Len;
        }

        //Create the new Parent
        var newChildren = new Node<T>?[token != null ? Constants.RRB_BRANCHING : 2];
        newChildren[0] = root;
        newChildren[1] = CreatePath(shift, leafToPush, token);

        var newParent = new InternalNode<T>(newChildren, newSizeTable, 2, token);

        // Update shift for the caller
        shift += Constants.RRB_BITS;

        return newParent;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Node<T> CreatePath<T>(int shift, LeafNode<T> tail, OwnerToken? token)
    {
        if (shift == 0) return tail;
        var node = new InternalNode<T>(1, token);
        node.Children[0] = CreatePath(shift - Constants.RRB_BITS, tail, token);
        return node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Node<T> CreateNewParent<T>(Node<T> left, Node<T> right, OwnerToken? token)
    {
        var parent = new InternalNode<T>(2, token);
        parent.Children[0] = left;
        parent.Children[1] = right;
        return parent;
    }
    
    // This is a method to get the child index. If the node is dense it does a regular dense search
    // if it is relaxed, it uses AVX to search 8 elements at a time.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe (int childIndex, int relativeIndex) GetChildIndexAvx<T>(InternalNode<T> node, int index,
        int shift)
    {
        // Dense / Balanced Path (No SizeTable)
        if (node.SizeTable == null)
        {
            int childIndex = (index >> shift) & Constants.RRB_MASK;
            int childStart = childIndex << shift;
            return (childIndex, index - childStart);
        }

        // Relaxed Path (SizeTable Search)
        int len = node.Len;
        int i = 0;

        // Use AVX2 if supported and profitable (at least one vector worth of data)
        // This is at no cost to the old kind of indexing.
        if (Avx2.IsSupported && len >= 8)
        {
            // Broadcast the target index to a vector of 8 ints
            var vIndex = Vector256.Create(index);

            fixed (int* tablePtr = node.SizeTable)
            {
                // Iterate in chunks of 8. 
                // Since RRB_BRANCHING is 32, this runs max 4 times (0, 8, 16, 24).
                for (; i <= len - 8; i += 8)
                {
                    var vTable = Avx.LoadVector256(tablePtr + i);

                    // Compare: SizeTable[k] > index
                    // Result is -1 (all 1s) for true, 0 for false.
                    var vResult = Avx2.CompareGreaterThan(vTable, vIndex);

                    // Extract sign bits to a generic integer mask (8 bits, one per element)
                    int mask = Avx.MoveMask(vResult.AsSingle());

                    if (mask != 0)
                    {
                        // Found a match in this chunk.
                        // The first set bit corresponds to the first element > index.
                        int offset = BitOperations.TrailingZeroCount(mask);
                        int matchIndex = i + offset;

                        int prevCount = matchIndex > 0 ? tablePtr[matchIndex - 1] : 0;
                        return (matchIndex, index - prevCount);
                    }
                }
            }
        }
        
        // Handles remaining elements (if len % 8 != 0) or systems without AVX2.
        // Also handles the case where the index is beyond the total size (i hits len).
        var table = node.SizeTable;
        while (i < len && table[i] <= index)
        {
            i++;
        }

        int prev = i > 0 ? table[i - 1] : 0;
        return (i, index - prev);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static InternalNode<T> AsInternal<T>(Node<T> node) => Unsafe.As<InternalNode<T>>(node);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LeafNode<T> AsLeaf<T>(Node<T> node) => Unsafe.As<LeafNode<T>>(node);


    public static (Node<T>? NewNode, LeafNode<T> PromotedTail) PromoteTail<T>(Node<T> node, int shift,
        OwnerToken? token)
    {
        // Base Case: We are at the leaf level. 
        // This entire node becomes the promoted tail.
        if (shift == 0) return (null, AsLeaf(node));
        
        // From here we know the node is internal, since leaf nodes are handled above.
        var internalNode = AsInternal(node);
        var lastIdx = internalNode.Len - 1;
        var lastChild = internalNode.Children[lastIdx]!;

        // Recurse down the right edge
        var (newLastChild, promotedTail) = PromoteTail(lastChild, shift - Constants.RRB_BITS, token);

        // If the child was fully consumed (it became the tail), we shrink this node
        // If the child remains (it gave up a descendant to be the tail), we keep size but update child
        var newLen = newLastChild == null ? lastIdx : internalNode.Len;

        // If this node becomes empty, return null so the parent knows to remove it too
        if (newLen == 0) return (null, promotedTail);


        // Reconstruct this Node

        // Copy Children
        var newChildren = new Node<T>?[newLen];
        Array.Copy(internalNode.Children, newChildren, newLen);

        if (newLastChild != null) newChildren[lastIdx] = newLastChild;

        // Handle SizeTable
        // If we didn't have one, we don't need one (removing from the right preserves dense prefix).
        // If we DID have one, we must update it.
        int[]? newSizeTable = null;

        if (internalNode.SizeTable != null)
        {
            newSizeTable = new int[newLen];
            // Copy the table up to the new length
            Array.Copy(internalNode.SizeTable, newSizeTable, newLen);

            if (newLastChild != null)
                // If the last child still exists, it is smaller now. 
                // We reduce the cumulative total at this index by the size of the removed tail.
                newSizeTable[lastIdx] -= promotedTail.Len;
            // If newLastChild is null, we just chopped off the last entry of the table, 
            // which correctly represents the new cumulative total of the previous sibling.
        }

        var newNode = new InternalNode<T>(newChildren, newSizeTable, newLen, token);
        return (newNode, promotedTail);
    }


    public static Node<T>? RemoveRecursive<T>(Node<T> node, int index, int shift)
    {
        // Base case: Leaf Level - Remove the item from the array
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;

            // If this is the last item, the node becomes empty.
            if (leaf.Len == 1) return null;

            var newItems = new T[leaf.Len - 1];

            // Copy before index
            if (index > 0)
                Array.Copy(leaf.Items, 0, newItems, 0, index);

            // Copy after index
            if (index < leaf.Len - 1)
                Array.Copy(leaf.Items, index + 1, newItems, index, leaf.Len - index - 1);

            return new LeafNode<T>(newItems, leaf.Len - 1, null);
        }

        // Internal Level: Find child and recurse
        var internalNode = (InternalNode<T>)node;
        var (childIndex, subIndex) = GetChildIndexAvx(internalNode, index, shift);

        Node<T> child = internalNode.Children[childIndex]!;
        Node<T>? newChild = RemoveRecursive(child, subIndex, shift - Constants.RRB_BITS);


        // Best case: The child became empty (remove it from children array)
        if (newChild == null)
        {
            // If this was the only child, this node also becomes empty
            if (internalNode.Len == 1) return null;

            int newLen = internalNode.Len - 1;
            var newChildren = new Node<T>?[newLen];

            // Copy children before
            if (childIndex > 0)
                Array.Copy(internalNode.Children, 0, newChildren, 0, childIndex);

            // Copy children after (shifting left)
            if (childIndex < newLen)
                Array.Copy(internalNode.Children, childIndex + 1, newChildren, childIndex, newLen - childIndex);

            // Unless we update the sizetable after removing a node, we will get a lot of nagging
            int[] newSizeTable = new int[newLen];

            if (internalNode.SizeTable != null)
            {
                // Copy part before
                if (childIndex > 0)
                    Array.Copy(internalNode.SizeTable, newSizeTable, childIndex);

                // Copy part after, subtracting 1 from all cumulative counts
                // (We removed exactly 1 item from the tree below)
                for (int i = childIndex; i < newLen; i++)
                {
                    newSizeTable[i] = internalNode.SizeTable[i + 1] - 1;
                }
            }
            else
            {
                // Convert Dense -> Relaxed
                int childShift = shift - Constants.RRB_BITS;

                // Reconstruct table. 
                // RemoveRecursive removes ONE item. If the child returns null, it means 
                // that child contained ONLY that one item.
                // So we subtract 1 from the total.

                int currentSum = 0;
                // Iterate over the new structure (skipping the removed child)
                for (int i = 0; i < newLen; i++)
                {

                    // If the old child was Dense, its size was blockSize.
                    // But we know internalNode was Dense, so all children (except last) were full.
                    // Actually, simply: We iterate the new children and ask for their size.
                    // Since this is max 32, it's fine.
                    // Optimally:
                    // Pre-childIndex: sum += blockSize (mostly)
                    // Post-childIndex: sum += blockSize
                    // But let us use Countree.

                    currentSum += CountTree(newChildren[i]!, childShift);
                    newSizeTable[i] = currentSum;
                }
            }

            return new InternalNode<T>(newChildren, newSizeTable, newLen, null);
        }
        // Second best case: The child exists (just modified)
        else
        {
            int newLen = internalNode.Len;
            var newChildren = new Node<T>?[newLen];
            Array.Copy(internalNode.Children, newChildren, newLen);
            newChildren[childIndex] = newChild;

            // Update SizeTable
            // We removed exactly 1 item.
            int[] newSizeTable = new int[newLen];

            if (internalNode.SizeTable != null)
            {
                // Copy before
                Array.Copy(internalNode.SizeTable, newSizeTable, childIndex);

                // Adjust current and after
                newSizeTable[childIndex] = internalNode.SizeTable[childIndex] - 1;
                for (int i = childIndex + 1; i < newLen; i++)
                {
                    newSizeTable[i] = internalNode.SizeTable[i] - 1;
                }
            }
            else
            {
                // Dense -> Relaxed
                // We must build the table because index arithmetic breaks.
                int childShift = shift - Constants.RRB_BITS;
                int blockSize = 1 << shift;

                int currentSum = 0;
                for (int i = 0; i < newLen; i++)
                {
                    if (i == childIndex)
                    {
                        // This is the modified child. It is 1 smaller than before.
                        // If it was the last child, we calculate exact size.
                        // If it was a middle child, it WAS full, so now it is blockSize - 1.
                        if (i == newLen - 1)
                            currentSum += CountTree(newChild, childShift);
                        else
                            currentSum += (blockSize - 1);
                    }
                    else if (i == newLen - 1)
                    {
                        // Last child of dense node (might be partial)
                        currentSum += CountTree(internalNode.Children[i]!, childShift);
                    }
                    else
                    {
                        // Middle child of dense node (Always full)
                        currentSum += blockSize;
                    }

                    newSizeTable[i] = currentSum;
                }
            }

            return new InternalNode<T>(newChildren, newSizeTable, newLen, null);
        }
    }

    // Helper return struct to avoid Tuple allocation
    internal readonly struct InsertResult<T>
    {
        public readonly Node<T> NewNode;
        public readonly Node<T>? Overflow; // If not null, the node split

        public InsertResult(Node<T> newNode, Node<T>? overflow = null)
        {
            NewNode = newNode;
            Overflow = overflow;
        }
    }


    public static InsertResult<T> InsertRecursive<T>(Node<T> node, int index, T item, int shift, OwnerToken? token)
    {
        // Leaf level
        if (shift == 0)
        {
            var leaf = AsLeaf(node);

            // Simple Insert (If it fits within standard limits)
            if (leaf.Len < Constants.RRB_BRANCHING)
            {
                var newItems = new T[leaf.Len + 1];
                if (index > 0) Array.Copy(leaf.Items, 0, newItems, 0, index);
                newItems[index] = item;
                if (index < leaf.Len) Array.Copy(leaf.Items, index, newItems, index + 1, leaf.Len - index);
                return new InsertResult<T>(new LeafNode<T>(newItems, leaf.Len + 1, null));
            }

            // Leaf Split
            // Robustness: Allocate based on actual length + 1. I don't even remember why I did this. 
            var totalCount = leaf.Len + 1;
            var totalItems = new T[totalCount];

            Array.Copy(leaf.Items, 0, totalItems, 0, index);
            totalItems[index] = item;
            Array.Copy(leaf.Items, index, totalItems, index + 1, leaf.Len - index);

            // Default Split Strategy: Balanced (roughly 16/17)
            // This keeps the tree healthier for random insertions.
            var splitPoint = (Constants.RRB_BRANCHING + 1) / 2;
            var rightLen = totalCount - splitPoint;

            var leftArr = new T[splitPoint];
            var rightArr = new T[rightLen];

            Array.Copy(totalItems, 0, leftArr, 0, splitPoint);
            Array.Copy(totalItems, splitPoint, rightArr, 0, rightLen);

            return new InsertResult<T>(new LeafNode<T>(leftArr, splitPoint, null),
                new LeafNode<T>(rightArr, rightLen, null));
        }

        // In the tree
        var internalNode = AsInternal(node);
        var (childIndex, subIndex) = GetChildIndexAvx(internalNode, index, shift);

        var child = internalNode.Children[childIndex]!;
        var result = InsertRecursive(child, subIndex, item, shift - Constants.RRB_BITS, token);

        // Pretty case: Child Update (No Split)
        if (result.Overflow == null)
        {
            var newChildren = new Node<T>?[internalNode.Len];
            Array.Copy(internalNode.Children, newChildren, internalNode.Len);
            newChildren[childIndex] = result.NewNode;

            int[]? newSizeTable = null;
            if (internalNode.SizeTable != null)
            {
                // Already Relaxed: Just update and increment subsequent
                newSizeTable = new int[internalNode.Len];
                Array.Copy(internalNode.SizeTable, newSizeTable, internalNode.Len);
                for (var i = childIndex; i < internalNode.Len; i++) newSizeTable[i]++;
            }
            else if (childIndex < internalNode.Len - 1)
            {
                // DENSE -> RELAXED (Math Optimization)
                // I just assume any insertion becomes relaxed. For my sanity
                newSizeTable = new int[internalNode.Len];

                var blockSize = 1 << shift; // e.g., 32 items
                var childShift = shift - Constants.RRB_BITS;
                var currentSum = 0;

                // Children before the modified index are guaranteed full.
                // Math: Index * BlockSize
                for (var i = 0; i < childIndex; i++)
                {
                    currentSum += blockSize;
                    newSizeTable[i] = currentSum;
                }

                // The Modified Child
                // If it was full (blockSize), it is now blockSize + 1.
                // If it was the last child (and partial), it is now partial + 1.
                // Let us not forget that we are in the no split zone

                // If we modified childIndex, we need its new seize
                // Old size was 'blockSize' (because it's a middle child of a Dense node).
                // New size is blockSize + 1.

                currentSum += blockSize + 1;
                newSizeTable[childIndex] = currentSum;

                // Handle Children after the modified index
                // They are still full (blockSize), except possibly the very last one.
                for (var i = childIndex + 1; i < internalNode.Len; i++)
                {
                    int size;
                    if (i == internalNode.Len - 1)
                        size = CountTree(internalNode.Children[i]!, childShift);
                    else
                        size = blockSize;

                    currentSum += size;
                    newSizeTable[i] = currentSum;
                }
            }
            
            return new InsertResult<T>(new InternalNode<T>(newChildren, newSizeTable, internalNode.Len, null));
        }

        // Sad case: Child owerflow

        // Check if overflow
        if (internalNode.Len < Constants.RRB_BRANCHING)
        {
            var newLen = internalNode.Len + 1;
            var newChildren = new Node<T>?[newLen];

            if (childIndex > 0) Array.Copy(internalNode.Children, 0, newChildren, 0, childIndex);
            newChildren[childIndex] = result.NewNode;
            newChildren[childIndex + 1] = result.Overflow;
            if (childIndex + 1 < internalNode.Len)
                Array.Copy(internalNode.Children, childIndex + 1, newChildren, childIndex + 2,
                    internalNode.Len - (childIndex + 1));

            int[]? newSizeTable = null;

            if (internalNode.SizeTable != null)
            {
                newSizeTable = new int[newLen];
                Array.Copy(internalNode.SizeTable, newSizeTable, childIndex);

                var prevTotal = childIndex > 0 ? newSizeTable[childIndex - 1] : 0;
                var leftSize = GetTotalSize(result.NewNode, shift - Constants.RRB_BITS);
                var rightSize = GetTotalSize(result.Overflow, shift - Constants.RRB_BITS);

                newSizeTable[childIndex] = prevTotal + leftSize;
                newSizeTable[childIndex + 1] = prevTotal + leftSize + rightSize;

                for (var i = childIndex + 1; i < internalNode.Len; i++)
                    newSizeTable[i + 1] = internalNode.SizeTable[i] + 1;
            }
            else
            {
                // Dense -> Relaxed (Split Logic)
                // Let's just assume an insert makes a relaxed child. 
                newSizeTable = new int[newLen];
                var childShift = shift - Constants.RRB_BITS;
                var blockSize = 1 << shift;
                var currentSum = 0;

                // Children before split (Guaranteed Full)
                for (var i = 0; i < childIndex; i++)
                {
                    currentSum += blockSize;
                    newSizeTable[i] = currentSum;
                }

                // Measure the split children
                currentSum += GetTotalSize(result.NewNode, childShift);
                newSizeTable[childIndex] = currentSum;

                currentSum += GetTotalSize(result.Overflow, childShift);
                newSizeTable[childIndex + 1] = currentSum;

                // Children after split (Shifted, Last one might be partial)
                for (var i = childIndex + 1; i < internalNode.Len; i++)
                {
                    var size = i == internalNode.Len - 1
                        ? CountTree(internalNode.Children[i]!, childShift)
                        : blockSize;
                    currentSum += size;
                    newSizeTable[i + 1] = currentSum;
                }
            }

            return new InsertResult<T>(new InternalNode<T>(newChildren, newSizeTable, newLen, null));
        }

        return SplitInternalNode(internalNode, childIndex, result.NewNode, result.Overflow, shift);
    }

    private static InsertResult<T> SplitInternalNode<T>(
        InternalNode<T> node,
        int splitChildIndex,
        Node<T> childLeft,
        Node<T> childRight,
        int shift)
    {
        // Total virtual children = 32 (existing) - 1 (replaced) + 2 (new) = 33.
        const int SplitPoint = 16;
        const int RightLen = 17; // 33 - 16

        var leftChildren = new Node<T>?[SplitPoint];
        var rightChildren = new Node<T>?[RightLen];

        // Helper to get from the logical sequence of 33
        Node<T> GetVirtualChild(int i)
        {
            if (i < splitChildIndex) return node.Children[i]!;
            if (i == splitChildIndex) return childLeft;
            if (i == splitChildIndex + 1) return childRight;
            return node.Children[i - 1]!;
        }

        for (var i = 0; i < SplitPoint; i++) leftChildren[i] = GetVirtualChild(i);
        for (var i = 0; i < RightLen; i++) rightChildren[i] = GetVirtualChild(SplitPoint + i);

        var leftTable = new int[SplitPoint];
        var rightTable = new int[RightLen];
        var childShift = shift - Constants.RRB_BITS;

        // Recalculate all sizes. 
        // This is safer than trying to reuse parts of the old table because 
        // splitting Dense nodes creates complex offset shifts.

        var cumulative = 0;

        // Fill Left
        for (var i = 0; i < SplitPoint; i++)
        {
            // Re-use logic: Measure new nodes, assume blocksize for others (unless table exists)
            var virtualIdx = i;
            var size = GetVirtualChildSize(node, virtualIdx, splitChildIndex, childLeft, childRight, childShift);
            cumulative += size;
            leftTable[i] = cumulative;
        }

        // Fill Right (Reset cumulative)
        cumulative = 0;
        for (var i = 0; i < RightLen; i++)
        {
            var virtualIdx = SplitPoint + i;
            var size = GetVirtualChildSize(node, virtualIdx, splitChildIndex, childLeft, childRight, childShift);
            cumulative += size;
            rightTable[i] = cumulative;
        }

        var newLeft = new InternalNode<T>(leftChildren, leftTable, SplitPoint, null);
        var newRight = new InternalNode<T>(rightChildren, rightTable, RightLen, null);

        return new InsertResult<T>(newLeft, newRight);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetVirtualChildSize<T>(
        InternalNode<T> originalNode,
        int virtualIndex,
        int splitIndex,
        Node<T> newLeft,
        Node<T> newRight,
        int childShift)
    {
        if (virtualIndex == splitIndex) return GetTotalSize(newLeft, childShift);
        if (virtualIndex == splitIndex + 1) return GetTotalSize(newRight, childShift);

        var originalIndex = virtualIndex < splitIndex ? virtualIndex : virtualIndex - 1;

        if (originalNode.SizeTable != null)
        {
            var prev = originalIndex > 0 ? originalNode.SizeTable[originalIndex - 1] : 0;
            return originalNode.SizeTable[originalIndex] - prev;
        }

        // Dense assumption
        if (originalIndex == originalNode.Len - 1)
            return CountTree(originalNode.Children[originalIndex]!, childShift);

        return 1 << (childShift + Constants.RRB_BITS);
    }

}