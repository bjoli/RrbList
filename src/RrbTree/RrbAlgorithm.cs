using System.Runtime.CompilerServices;

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

        var (childIndex, subIndex) = GetChildIndex(internalNode, index, shift);

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
            var leaf = (LeafNode<T>)node;
            if (leaf.Len == limit) return leaf;

            var newItems = new T[limit];
            Array.Copy(leaf.Items, newItems, limit);
            return new LeafNode<T>(newItems, limit, null);
        }

        var internalNode = (InternalNode<T>)node;

        var (childIdx, indexInChild) = GetChildIndex(internalNode, limit - 1, shift);

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
            var left = (InternalNode<T>)leftNode;
            var lastChild = left.Children[left.Len - 1]!;

            int subShift;
            var mergedMid = Concat(lastChild, rightNode, leftShift - Constants.RRB_BITS, rightShift, out subShift);

            // Pass subShift to Rebalance
            return Rebalance(left, mergedMid, null, leftShift, subShift, out newShift);
        }

        // Right node is higher than left node
        if (leftShift < rightShift)
        {
            var right = (InternalNode<T>)rightNode;
            var firstChild = right.Children[0]!;

            int subShift;
            var mergedMid = Concat(leftNode, firstChild, leftShift, rightShift - Constants.RRB_BITS, out subShift);

            // Pass subShift to Rebalance
            return Rebalance(null, mergedMid, right, rightShift, subShift, out newShift);
        }

        // Same height
        if (leftShift == 0)
        {
            // Both are leaves.
            // If they fit in one leaf, merge them.
            var leftLeaf = (LeafNode<T>)leftNode;
            var rightLeaf = (LeafNode<T>)rightNode;

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

        {
            var left = (InternalNode<T>)leftNode;
            var right = (InternalNode<T>)rightNode;
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
            var leaf = (LeafNode<T>)root;
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

        var internalNode = (InternalNode<T>)root;
        var (childIdx, splitInChild) = GetChildIndex(internalNode, splitIndex, shift);
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

            if (shufflingLeaves)
            {
                var newItems = new T[newSize];
                var curSize = 0;

                while (curSize < newSize)
                {
                    var srcLeaf = (LeafNode<T>)all[idx];
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
        var sizes = new int[node.Len];
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
        return new InternalNode<T>(node.Children, isBalanced ? null : sizes, node.Len, null);
    }

    private static int CountTree<T>(Node<T> node, int shift)
    {
        if (shift == 0) return node.Len;
        // relaxed, just use the size table
        if (node is InternalNode<T> inode && inode.SizeTable != null)
            return inode.SizeTable[inode.Len - 1];

        // Balanced calculation
        return (node.Len - 1) * (1 << shift) + CountTree(((InternalNode<T>)node).Children[node.Len - 1]!,
            shift - Constants.RRB_BITS);
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
            var leaf = (LeafNode<T>)node;
            var newLen = leaf.Len - toDrop;
            var newItems = new T[newLen];
            Array.Copy(leaf.Items, toDrop, newItems, 0, newLen);
            return new LeafNode<T>(newItems, newLen, null);
        }

        var internalNode = (InternalNode<T>)node;
        var (subidx, dropInChild) = GetChildIndex(internalNode, toDrop, shift);


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
        var internalNode = (InternalNode<T>)node;

        // A. Base Case: Parent of Leaves (Shift == 5)
        if (shift == Constants.RRB_BITS)
        {
            // 1. Try to MERGE into the last child
            if (internalNode.Len > 0)
            {
                var lastChild = (LeafNode<T>)internalNode.Children[internalNode.Len - 1]!;

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

// Helper to get size without crashing. That was a thing.
    private static int GetTotalSize<T>(Node<T> node, int shift)
    {
        if (shift == 0) return node.Len;
        if (node is InternalNode<T> inode && inode.SizeTable != null)
            return inode.SizeTable[inode.Len - 1];

        // It's a dense node.
        // However, if we are calling this on a child that forced relaxation, 
        // it might be a Dense node with 32 children where the last one is sparse
        var denseNode = (InternalNode<T>)node;
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
        var oldRootInode = (InternalNode<T>)root;


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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int childIndex, int relativeIndex) GetChildIndex<T>(InternalNode<T> node, int index, int shift)
    {
        if (node.SizeTable != null)
        {
            var childIndex = 0;
            // Search for the slot where size > index
            while (childIndex < node.Len && node.SizeTable[childIndex] <= index) childIndex++;

            // Calculate relative index for the child
            var prevCount = childIndex > 0 ? node.SizeTable[childIndex - 1] : 0;
            return (childIndex, index - prevCount);
        }
        else
        {
            // Dense/Balanced logic
            var childIndex = (index >> shift) & Constants.RRB_MASK;

            // IMPORTANT: For dense nodes, the relative index is just masking 
            // IF we assume the child is also dense. But if the child is Relaxed, 
            // it expects a 0-based index.
            // It is safer to always subtract the base.
            var childStart = childIndex << shift;
            return (childIndex, index - childStart);
        }
    }


    public static (Node<T>? NewNode, LeafNode<T> PromotedTail) PromoteTail<T>(Node<T> node, int shift,
        OwnerToken? token)
    {
        // Base Case: We are at the leaf level. 
        // This entire node becomes the promoted tail.
        if (shift == 0) return (null, (LeafNode<T>)node);

        var internalNode = (InternalNode<T>)node;
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
}