using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Collections;

internal static class RrbAlgorithm
{
    public static Node<T> Update<T>(Node<T> root, int index, T value, int shift, OwnerId token)
    {
        // Tree is not a tree.
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)root;

            // Direct CloneAndSet for persistent
            if (token.IsNone)
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

        if (token.IsNone)
            // Cone your way back up
            return internalNode.CloneAndSetChild(childIndex, newChild);

        internalNode = internalNode.EnsureEditable(token);
        internalNode.Children[childIndex] = newChild;
        return internalNode;
    }
    
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

    public static (Node<T> Node, int Size) Concat<T>(Node<T> leftNode, int leftSize, Node<T> rightNode, int rightSize, int leftShift, int rightShift, out int newShift)
    {
        if (leftShift > rightShift)
        {
            var left = AsInternal(leftNode);
            var lastChild = left.Children[left.Len - 1]!;
            var lastChildSize = GetChildKnownSize(left, left.Len - 1, leftSize, leftShift);
            
            var (mergedMid, mergedSize) = Concat(lastChild, lastChildSize, rightNode, rightSize, leftShift - Constants.RRB_BITS, rightShift, out var subShift);
            return Rebalance(left, leftSize - lastChildSize, mergedMid, mergedSize, null, 0, leftShift, subShift, out newShift);
        }

        if (leftShift < rightShift)
        {
            var right = AsInternal(rightNode);
            var firstChild = right.Children[0]!;
            var firstChildSize = GetChildKnownSize(right, 0, rightSize, rightShift);

            var (mergedMid, mergedSize) = Concat(leftNode, leftSize, firstChild, firstChildSize, leftShift, rightShift - Constants.RRB_BITS, out var subShift);
            return Rebalance(null, 0, mergedMid, mergedSize, right, rightSize - firstChildSize, rightShift, subShift, out newShift);
        }

        if (leftNode.Len + rightNode.Len <= Constants.RRB_BRANCHING)
        {
            newShift = leftShift;
            var newLen = leftNode.Len + rightNode.Len;
            if (leftShift == 0)
            {
                var lLeaf = AsLeaf(leftNode);
                var rLeaf = AsLeaf(rightNode);
                var newItems = new T[newLen];
                Array.Copy(lLeaf.Items, 0, newItems, 0, lLeaf.Len);
                Array.Copy(rLeaf.Items, 0, newItems, lLeaf.Len, rLeaf.Len);
                return (new LeafNode<T>(newItems, newLen, OwnerId.None), leftSize + rightSize);
            }
            
            var lInt = AsInternal(leftNode);
            var rInt = AsInternal(rightNode);
            var newChildren = new Node<T>?[newLen];
            Array.Copy(lInt.Children, 0, newChildren, 0, lInt.Len);
            Array.Copy(rInt.Children, 0, newChildren, lInt.Len, rInt.Len);

            int[]? newTable = null;
            if (lInt.IsRelaxed() || rInt.IsRelaxed() || lInt.Len < Constants.RRB_BRANCHING) 
            {
                newTable = new int[newLen];
                int sum = 0;
                for (int i = 0; i < lInt.Len; i++) { sum += GetChildKnownSize(lInt, i, leftSize, leftShift); newTable[i] = sum; }
                for (int i = 0; i < rInt.Len; i++) { sum += GetChildKnownSize(rInt, i, rightSize, rightShift); newTable[lInt.Len + i] = sum; }
            }
            return (new InternalNode<T>(newChildren, newTable, newLen, OwnerId.None), leftSize + rightSize);
        }

        if (leftShift == 0)
        {
            newShift = Constants.RRB_BITS;
            var children = new Node<T>?[2] { leftNode, rightNode };
            var sizes = new int[2] { leftSize, leftSize + rightSize };
            return (new InternalNode<T>(children, sizes, 2, OwnerId.None), leftSize + rightSize);
        }

        var leftI = AsInternal(leftNode);
        var rightI = AsInternal(rightNode);
        var midLeft = leftI.Children[leftI.Len - 1]!;
        var midLeftSize = GetChildKnownSize(leftI, leftI.Len - 1, leftSize, leftShift);
        var midRight = rightI.Children[0]!;
        var midRightSize = GetChildKnownSize(rightI, 0, rightSize, rightShift);

        var (mergedMid2, mergedSize2) = Concat(midLeft, midLeftSize, midRight, midRightSize, leftShift - Constants.RRB_BITS, rightShift - Constants.RRB_BITS, out var subShift2);
        return Rebalance(leftI, leftSize - midLeftSize, mergedMid2, mergedSize2, rightI, rightSize - midRightSize, leftShift, subShift2, out newShift);
    }

    private static (Node<T> Node, int Size) Rebalance<T>(
        InternalNode<T>? left, int leftBaseSize,
        Node<T> center, int centerSize,
        InternalNode<T>? right, int rightBaseSize,
        int shift, int centerShift, out int newShift)
    {
        int totalSize = leftBaseSize + centerSize + rightBaseSize;
        int childCount = (left != null ? left.Len - 1 : 0) + (centerShift == shift ? AsInternal(center).Len : 1) + (right != null ? right.Len - 1 : 0);

        if (childCount <= Constants.RRB_BRANCHING)
        {
            var newChildren = new Node<T>?[childCount];
            var newSizes = new int[childCount];
            int idx = 0, cumulative = 0;

            if (left != null)
            {
                for (int i = 0; i < left.Len - 1; i++)
                {
                    newChildren[idx] = left.Children[i];
                    cumulative += GetChildKnownSize(left, i, leftBaseSize + GetChildKnownSize(left, left.Len - 1, int.MaxValue, shift), shift);
                    newSizes[idx++] = cumulative;
                }
            }
            if (centerShift == shift)
            {
                var cInt = AsInternal(center);
                for (int i = 0; i < cInt.Len; i++)
                {
                    newChildren[idx] = cInt.Children[i];
                    cumulative += GetChildKnownSize(cInt, i, centerSize, shift);
                    newSizes[idx++] = cumulative;
                }
            }
            else
            {
                newChildren[idx] = center;
                cumulative += centerSize;
                newSizes[idx++] = cumulative;
            }
            if (right != null)
            {
                for (int i = 1; i < right.Len; i++)
                {
                    newChildren[idx] = right.Children[i];
                    cumulative += GetChildKnownSize(right, i, rightBaseSize + GetChildKnownSize(right, 0, int.MaxValue, shift), shift);
                    newSizes[idx++] = cumulative;
                }
            }

            newShift = shift;
            return (new InternalNode<T>(newChildren, newSizes, childCount, OwnerId.None), totalSize);
        }

        // Fallback to redistribution
        var allChildren = new Node<T>[childCount];
        var allSizes = new int[childCount];
        int count = 0;

        if (left != null)
        {
            for (var i = 0; i < left.Len - 1; i++)
            {
                allChildren[count] = left.Children[i]!;
                allSizes[count++] = GetChildKnownSize(left, i, leftBaseSize + GetChildKnownSize(left, left.Len - 1, int.MaxValue, shift), shift);
            }
        }
        if (centerShift == shift)
        {
            var cInternal = AsInternal(center);
            for (var i = 0; i < cInternal.Len; i++)
            {
                allChildren[count] = cInternal.Children[i]!;
                allSizes[count++] = GetChildKnownSize(cInternal, i, centerSize, shift);
            }
        }
        else
        {
            allChildren[count] = center;
            allSizes[count++] = centerSize;
        }
        if (right != null)
        {
            for (var i = 1; i < right.Len; i++)
            {
                allChildren[count] = right.Children[i]!;
                allSizes[count++] = GetChildKnownSize(right, i, rightBaseSize + GetChildKnownSize(right, 0, int.MaxValue, shift), shift);
            }
        }

        Span<int> plan = stackalloc int[count];
        CreateConcatPlan(new ReadOnlySpan<Node<T>>(allChildren, 0, count), plan, out var topLen);

        var newAll = ExecuteConcatPlan(new ReadOnlySpan<Node<T>>(allChildren, 0, count), new ReadOnlySpan<int>(allSizes, 0, count), plan, topLen, shift);

        if (topLen <= Constants.RRB_BRANCHING)
        {
            newShift = shift;
            return (newAll, totalSize);
        }

        var newLeft = CopyInternalAndSetSizes(newAll, 0, Constants.RRB_BRANCHING);
        var newRight = CopyInternalAndSetSizes(newAll, Constants.RRB_BRANCHING, topLen - Constants.RRB_BRANCHING);

        newShift = shift + Constants.RRB_BITS;
        var parent = new InternalNode<T>(new Node<T>?[2] { newLeft.Node, newRight.Node }, new int[2] { newLeft.Size, totalSize }, 2, OwnerId.None);
        return (parent, totalSize);
    }

    private static void CreateConcatPlan<T>(ReadOnlySpan<Node<T>> allChildren, Span<int> nodeCount, out int topLen)
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
        var iIdx = 0;

        while (optimalSlots + Constants.RRB_EXTRAS < shuffledLen)
        {
            while (iIdx < shuffledLen && nodeCount[iIdx] > Constants.RRB_BRANCHING - Constants.RRB_INVARIANT) iIdx++;
            if (iIdx == shuffledLen) break;

            var remainingNodes = nodeCount[iIdx];
            do
            {
                if (iIdx + 1 >= shuffledLen) break;
                var minSize = Math.Min(remainingNodes + nodeCount[iIdx + 1], Constants.RRB_BRANCHING);
                nodeCount[iIdx] = minSize;
                remainingNodes = remainingNodes + nodeCount[iIdx + 1] - minSize;
                iIdx++;
            } while (remainingNodes > 0);

            for (var j = iIdx; j < shuffledLen - 1; j++) nodeCount[j] = nodeCount[j + 1];
            shuffledLen--;
            iIdx--;
        }
        topLen = shuffledLen;
    }
private static InternalNode<T> ExecuteConcatPlan<T>(ReadOnlySpan<Node<T>> all, ReadOnlySpan<int> sizes, Span<int> plan, int slen, int shift)
    {
        var newChildren = new Node<T>?[slen];
        var newSizes = new int[slen];
        var idx = 0;
        var offset = 0;
        int runningTotalSize = 0;

        var shufflingLeaves = shift == Constants.RRB_BITS;

        for (var i = 0; i < slen; i++)
        {
            var newSize = plan[i];
            if (offset == 0 && idx < all.Length && all[idx].Len == newSize)
            {
                newChildren[i] = all[idx];
                runningTotalSize += sizes[idx];
                newSizes[i] = runningTotalSize;
                idx++;
                continue;
            }

            int nodeAccumulatedSize = 0;
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
                    nodeAccumulatedSize += toCopy; // This is fine because toCopy is a flat count, not cumulative.
                    if (offset == srcLeaf.Len) { idx++; offset = 0; }
                }
                newChildren[i] = new LeafNode<T>(newItems, newSize, OwnerId.None);
            }
            else
            {
                var newSubChildren = new Node<T>?[newSize];
                var newSubSizes = new int[newSize];
                var curSize = 0;
                int subTotal = 0; // Cumulative for the new node
                while (curSize < newSize)
                {
                    var srcInternal = AsInternal(all[idx]);
                    var available = srcInternal.Len - offset;
                    var toCopy = Math.Min(available, newSize - curSize);
                    
                    for(int c = 0; c < toCopy; c++) {
                        int childIdx = offset + c;
                        int childSz = GetChildKnownSize(srcInternal, childIdx, sizes[idx], shift - Constants.RRB_BITS);
                        subTotal += childSz;
                        newSubSizes[curSize + c] = subTotal;
                    }

                    Array.Copy(srcInternal.Children, offset, newSubChildren, curSize, toCopy);
                    curSize += toCopy;
                    offset += toCopy;
                    
                    // REMOVED: nodeAccumulatedSize += subTotal;
                    
                    if (offset == srcInternal.Len) { idx++; offset = 0; }
                }
                newChildren[i] = new InternalNode<T>(newSubChildren, newSubSizes, newSize, OwnerId.None);
                
                // ADDED: The total size of this newly constructed node is exactly subTotal.
                nodeAccumulatedSize = subTotal;
            }
            
            runningTotalSize += nodeAccumulatedSize;
            newSizes[i] = runningTotalSize;
        }
        return new InternalNode<T>(newChildren, newSizes, slen, OwnerId.None);
    }

    private static (InternalNode<T> Node, int Size) CopyInternalAndSetSizes<T>(InternalNode<T> orig, int start, int len)
    {
        var newArr = new Node<T>?[len];
        var newSizes = new int[len];
        int baseSub = start > 0 ? orig.SizeTable![start - 1] : 0;
        
        Array.Copy(orig.Children, start, newArr, 0, len);
        for (int i = 0; i < len; i++)
        {
            newSizes[i] = orig.SizeTable![start + i] - baseSub;
        }
        return (new InternalNode<T>(newArr, newSizes, len, OwnerId.None), newSizes[len - 1]);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetChildKnownSize<T>(InternalNode<T> node, int childIndex, int knownSize, int shift)
    {
        if (node.SizeTable != null)
        {
            int prev = childIndex > 0 ? node.SizeTable[childIndex - 1] : 0;
            return node.SizeTable[childIndex] - prev;
        }
        if (childIndex < node.Len - 1) return 1 << shift;
        return knownSize - (node.Len - 1) * (1 << shift);
    }

  private static InternalNode<T> SetSizes<T>(InternalNode<T> node, int shift)
    {
        var childShift = shift - Constants.RRB_BITS;
        var expectedBlockSize = 1 << shift;
        var isBalanced = true;
  
        // Scan Phase: Detect if we violate Dense Invariants
        // We MUST verify sizes because Rebalance() might have put a partial node in the middle.
        for (var i = 0; i < node.Len; i++)
        {
            var child = node.Children[i]!;
  
            // If child is Relaxed, we must be Relaxed.
            if (child.IsRelaxed())
            {
                isBalanced = false;
                break;
            }
  
            // B. If child is Middle (not last), it MUST be full.
            if (i < node.Len - 1)
            {
                // For Leaf Nodes (Shift 5 parent -> Shift 0 child), 
                // reading .Len is O(1). This avoids the CountTree overhead for the most common case.
                int size;
                if (childShift == 0)
                {
                    // Unsafe cast is safe here because of shift logic
                    size = AsLeaf(child).Len;
                }
                else
                {
                    // For Internal nodes, we must traverse the spine to be sure.
                    // This is O(Height), which is acceptable (max 5-6 steps).
                    size = CountTree(child, childShift);
                }
  
                if (size != expectedBlockSize)
                {
                    isBalanced = false;
                    break;
                }
            }
            // Note: We don't check the last child. 
            // A Dense node is allowed to have a partial last child.
        }
  
        // Fast Path: It's a valid Dense node. 
        if (isBalanced)
            return new InternalNode<T>(node.Children, null, node.Len, OwnerId.None);
  
  
        // 2. Build Path: We are Relaxed. Build the table.
        // We repeat the size logic here, but we can't share state easily without allocating.
        // Since we only hit this path when necessary, the double-check is worth the memory savings on the Fast Path.
        var sizes = new int[node.Len];
        var sum = 0;
  
        for (var i = 0; i < node.Len; i++)
        {
            var child = node.Children[i]!;
            int size;
  
            if (child.IsRelaxed())
            {
                // Trust the existing table (O(1))
                var internalChild = AsInternal(child);
                size = internalChild.SizeTable![internalChild.Len - 1];
            }
            else if (childShift == 0)
            {
                // Leaf Optimization (O(1))
                size = AsLeaf(child).Len;
            }
            else
            {
                // Dense Internal traversal (O(Height))
                size = CountTree(child, childShift);
            }
  
            sum += size;
            sizes[i] = sum;
        }
  
        return new InternalNode<T>(node.Children, sizes, node.Len, OwnerId.None);
    }
 
  
    internal static int CountTree<T>(Node<T> node, int shift)
    {
        var totalSize = 0;
        
        // Fast Path: Relaxed Node
        // If the node has a SizeTable, we can stop immediately. The table holds the accurate total count.
        if (node.IsRelaxed())
            return AsInternal(node).SizeTable![node.Len - 1];

        // Iterate down the rightmost edge until we hit a leaf or a relaxed node
        while (shift > 0)
        {
            var internalNode = AsInternal(node);
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
        return new InternalNode<T>(newArr, null, len, OwnerId.None);
    }
    
    /// <summary>
    /// Optimized single-pass Slice.
    /// 1. LCA Descent: Skips parent nodes that don't split the range.
    /// 2. Single Pass: Constructs the result by merging Left, Middle, and Right parts in one allocation.
    /// 3. Squash: Automatically reduces tree height if the root collapses.
    /// 4. Density Check: Avoids SizeTable allocation if the node remains dense (e.g. Take(N)).
    /// </summary>
    public static Node<T>? Slice<T>(Node<T> root, int start, int count, ref int shift, out T[] promotedTail, out int promotedTailLen)
    {
        // --- 2. LCA Descent: As long as start and end is in the same node, we go down!
        var node = root;
        var end = start + count;

        while (shift > 0)
        {
            var internalNode = AsInternal(node);
            var (startIdx, startSubIdx) = GetChildIndexAvx(internalNode, start, shift);
            var (endIdx, _) = GetChildIndexAvx(internalNode, end - 1, shift);

            if (startIdx != endIdx) break; 

            // Descend
            node = internalNode.Children[startIdx]!;
            
            var offset = start - startSubIdx;
            start = startSubIdx;
            end -= offset;
            
            shift -= Constants.RRB_BITS;
        }

        // --- 3. Execute Split ---
        var (newNode, tailArray, tailLen) = SliceNode(node, start, end, shift);
        promotedTail = tailArray;
        promotedTailLen = tailLen;

        // --- 4. Squash: despite doing DCA, the tail promotion might collapse the root. 
        if (newNode != null)
        {
            while (!newNode!.IsLeaf() && newNode.Len == 1 && shift > 0)
            {
                newNode = AsInternal(newNode).Children[0];
                shift -= Constants.RRB_BITS;
            }
        }

        return newNode;
    }

    private static (Node<T>? NewNode, T[] TailArray, int TailLen) SliceNode<T>(Node<T> node, int start, int end, int shift)
    {
        // Base Case: Leaf Node
        if (shift == 0)
        {
            var leaf = AsLeaf(node);
            var len = end - start;

            // Optimization: If preserving exact leaf
            if (len == leaf.Len)
            {
                if (len < Constants.RRB_BRANCHING) 
                    return (null, leaf.Items, leaf.Len); // Promote to tail (Pass raw array)
                
                return (leaf, Array.Empty<T>(), 0); // Keep in tree
            }

            // Standard Slice
            var newItems = new T[len];
            Array.Copy(leaf.Items, start, newItems, 0, len);

            if (len < Constants.RRB_BRANCHING) 
                return (null, newItems, len); // Promote to tail
            
            return (new LeafNode<T>(newItems, len, OwnerId.None), Array.Empty<T>(), 0);
        }

        var internalNode = AsInternal(node);
        var (startIdx, startSubIdx) = GetChildIndexAvx(internalNode, start, shift);
        var (endIdx, endSubIdx) = GetChildIndexAvx(internalNode, end - 1, shift);
        
        var limitInChild = endSubIdx + 1;

        // 1. Recurse Edges
        // If startSubIdx is 0, we are taking the whole child, so no left recursion needed.
        Node<T>? leftResult = null;
        if (startSubIdx > 0)
        {
            leftResult = SliceLeftRec(internalNode.Children[startIdx]!, startSubIdx, shift - Constants.RRB_BITS);
        }

        T[]? tailArray;
        int tailLen;
        var rightResult= SliceRightAndPromote(internalNode.Children[endIdx]!, limitInChild, shift - Constants.RRB_BITS, out tailArray, out tailLen);


        // 2. Reconstruct Node
        var hasLeft = leftResult != null;
        var hasRight = rightResult != null;
        
        // Calculate Middle
        // If startSubIdx == 0, we include startIdx as a "middle" (full) child.
        // If startSubIdx > 0, startIdx was handled by leftResult.
        var startMiddle = (startSubIdx == 0) ? startIdx : startIdx + 1;
        var middleCount = Math.Max(0, endIdx - startMiddle);
        
        var newLen = (hasLeft ? 1 : 0) + middleCount + (hasRight ? 1 : 0);
        if (newLen == 0) return (null, tailArray, tailLen);

        var newChildren = new Node<T>?[newLen];
        int writeIdx = 0;
        
        // We only allocate a SizeTable if:
        // 1. The original node was already Relaxed.
        // 2. We sliced the LEFT edge (creating a partial first child).
        // If we only sliced the RIGHT edge of a Dense node, it remains Dense (last child allowed to be partial).
        var isOriginalRelaxed = internalNode.IsRelaxed();
        var mustBeRelaxed = isOriginalRelaxed || (startSubIdx > 0);

        int[]? newTable = mustBeRelaxed ? new int[newLen] : null;
        int currentSum = 0;
        var originalTable = internalNode.SizeTable; 

        // A. Left Edge (Only exists if startSubIdx > 0)
        if (hasLeft)
        {
            newChildren[writeIdx] = leftResult;
            if (mustBeRelaxed)
            {
                int originalSize = isOriginalRelaxed
                    ? (originalTable![startIdx] - (startIdx > 0 ? originalTable[startIdx - 1] : 0))
                    : (1 << shift);

                currentSum += (originalSize - startSubIdx);
                newTable![writeIdx] = currentSum;
            }
            writeIdx++;
        }

        // B. Middle Children
        if (middleCount > 0)
        {
            // Bulk Copy
            Array.Copy(internalNode.Children, startMiddle, newChildren, writeIdx, middleCount);

            if (mustBeRelaxed)
            {
                for (int i = 0; i < middleCount; i++)
                {
                    int origIdx = startMiddle + i;
                    int size = isOriginalRelaxed
                        ? (originalTable![origIdx] - (origIdx > 0 ? originalTable[origIdx - 1] : 0))
                        : (1 << shift);

                    currentSum += size;
                    newTable![writeIdx++] = currentSum;
                }
            }
            else
            {
                writeIdx += middleCount;
            }
        }

        // C. Right Edge
        if (hasRight)
        {
            newChildren[writeIdx] = rightResult;
            if (mustBeRelaxed)
            {
                // Size = Kept - Promoted
                currentSum += (limitInChild - tailLen);
                newTable![writeIdx] = currentSum;
            }
        }

        return (new InternalNode<T>(newChildren, newTable, newLen, OwnerId.None), tailArray, tailLen);
    }
    
    
     internal static Node<T> SliceLeftRec<T>(Node<T> node, int toDrop, int shift)
    {
        // Base Case: Leaf
        if (shift == 0)
        {
            var leaf = AsLeaf(node);
            var newLen = leaf.Len - toDrop;
            var newItems = new T[newLen];
            Array.Copy(leaf.Items, toDrop, newItems, 0, newLen);
            return new LeafNode<T>(newItems, newLen, OwnerId.None);
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
        var staysBalanced = internalNode.IsDense() && dropInChild == 0;

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
                    long oldCumulative;

                    // Check if we are at the very last child of the ORIGINAL node.
                    // The index in the original internalNode.Children is (subidx + i).
                    if (subidx + i == internalNode.Len - 1)
                    {
                        // The last child of a dense node is the ONLY one that might not be full.
                        // We must calculate the actual total size of the original node.

                        // 1. Sum of all preceding full siblings
                        var fullChildrenSize = (long)(internalNode.Len - 1) * childCapacity;

                        // 2. Actual size of the last child (use CountTree to traverse down)
                        var lastChildSize = CountTree(internalNode.Children[internalNode.Len - 1]!,
                            shift - Constants.RRB_BITS);

                        oldCumulative = fullChildrenSize + lastChildSize;
                    }
                    else
                    {
                        // Any child that is NOT the last child in a dense node is guaranteed full.
                        oldCumulative = (long)(subidx + i + 1) * childCapacity;
                    }

                    newSizeTable[i] = (int)(oldCumulative - toDrop);
                }
            }
        }

        return new InternalNode<T>(newChildren, newSizeTable, remainingChildren, OwnerId.None);
    }

    /// <summary>
    ///     Slices the tree at 'limit'.
    ///     If the resulting rightmost leaf is Partial (less than 32), it is detached and returned as PromotedTail.
    ///     If the resulting rightmost leaf is Full (32), it stays in the tree (PromotedTail is Empty).
    /// </summary>
    internal static Node<T>? SliceRightAndPromote<T>(Node<T> node, int limit, int shift, out T[] promotedTail, out int tailLen)
{
    // --- Base Case: Leaf ---
    if (shift == 0)
    {
        var leaf = AsLeaf(node);

        // Optimization: If taking the exact existing leaf
        if (leaf.Len == limit)
        {
            if (limit < Constants.RRB_BRANCHING)
            {
                // It's partial -> Promote it
                promotedTail = leaf.Items;
                tailLen = leaf.Len;
                return null;
            }
            else
            {
                // It's full -> Keep it
                promotedTail = []; // Or Array.Empty<T>()
                tailLen = 0;
                return leaf;
            }
        }

        // Otherwise, Create the slice
        var newItems = new T[limit];
        Array.Copy(leaf.Items, 0, newItems, 0, limit);

        if (limit < Constants.RRB_BRANCHING)
        {
            promotedTail = newItems;
            tailLen = limit;
            return null;
        }
        else
        {
            promotedTail = [];
            tailLen = 0;
            return new LeafNode<T>(newItems, limit, OwnerId.None);
        }
    }

    // --- Recursive Step: Internal ---
    var internalNode = AsInternal(node);

    // Find which child contains the 'limit' boundary
    var (childIdx, idxInChild) = GetChildIndexAvx(internalNode, limit - 1, shift);

    // Recurse strictly on that child
    // The recursive call writes directly to our out parameters
    var limitInChild = idxInChild + 1;
    var childResult = SliceRightAndPromote(internalNode.Children[childIdx]!, limitInChild, shift - Constants.RRB_BITS, out promotedTail, out tailLen);

    // --- Reconstruct the Node ---

    // 1. Determine new length of this node
    // If childResult is null, it means the child was fully consumed (promoted).
    var newLen = childResult == null ? childIdx : childIdx + 1;

    if (newLen == 0)
    {
        // If we lost the only child, this node disappears too.
        // promotedTail and tailLen are already set by the recursive call.
        return null;
    }

    // 2. Allocate and Copy Children
    var newChildren = new Node<T>?[newLen];

    // Copy preceding children (they are untouched)
    if (childIdx > 0)
        Array.Copy(internalNode.Children, 0, newChildren, 0, Math.Min(childIdx, newLen));

    // If the child was modified (not removed), place it
    if (childResult != null)
        newChildren[childIdx] = childResult;

    // 3. Handle SizeTable (Preserve Density Optimization)
    // If the node was Dense, slicing from the right preserves density (remaining children are still full),
    // so we only need a SizeTable if the original node was Relaxed.
    int[]? newSizeTable = null;
    
    if (internalNode.SizeTable != null)
    {
        newSizeTable = new int[newLen];
        Array.Copy(internalNode.SizeTable, newSizeTable, newLen);

        // Update the last entry to reflect the exact cut
        // The size at this boundary is exactly 'limit', MINUS whatever we promoted.
        // We use the tailLen output from the recursive call here.
        newSizeTable[newLen - 1] = limit - tailLen;
    }

    return new InternalNode<T>(newChildren, newSizeTable, newLen, OwnerId.None);
}
   


// Returns the updated node if the tail could be inserted/merged.
// Returns NULL if the node is physically full and the tail could not be accepted.
    private static Node<T>? TryPushDownTail<T>(Node<T> node, LeafNode<T> tail, int shift, OwnerId token)
    {
        var internalNode = AsInternal(node);

        // 1. Leaf Parent Level (Shift 5) -> Append directly
        if (shift == Constants.RRB_BITS)
        {
            if (internalNode.Len < Constants.RRB_BRANCHING)
                return AppendChild(internalNode, tail, shift, token);
            return null;
        }

        // 2. Internal Level -> Recurse Spine
        if (internalNode.Len > 0)
        {
            var lastIdx = internalNode.Len - 1;
            var lastChild = internalNode.Children[lastIdx]!;

            var newLastChild = TryPushDownTail(lastChild, tail, shift - Constants.RRB_BITS, token);

            if (newLastChild != null)
            {
                // Update the pointer in the current node
                var editable = internalNode.EnsureEditable(token);
                editable.Children[lastIdx] = newLastChild;

                // Update metadata (SizeTable) ONLY if necessary
                if (editable.IsRelaxed())
                    editable.SizeTable![lastIdx] += tail.Len;

                return editable;
            }
        }

        // 3. Append New Path (Sibling to existing spine)
        if (internalNode.Len < Constants.RRB_BRANCHING)
        {
            var newPath = CreatePath(shift - Constants.RRB_BITS, tail, token);
            return AppendChild(internalNode, newPath, shift, token);
        }

        return null;
    }

    private static InternalNode<T> AppendChild<T>(InternalNode<T> node, Node<T> childToAdd, int shift, OwnerId token)
    {
        // Check if appending this child violates the Dense Invariant.
        // Violation happens if we are currently Dense, but the LAST child is not full.
        var requiresRelaxation = false;

        if (node.IsDense() && node.Len > 0)
        {
            // We only strictly need to check this at the leaf-parent level (Shift 5)
            // or if we trust that higher levels handle their own density.
            if (shift == Constants.RRB_BITS)
            {
                var lastChild = AsLeaf(node.Children[node.Len - 1]!);
                if (lastChild.Len < Constants.RRB_BRANCHING)
                    requiresRelaxation = true;
            }
            else
            {
                // For higher levels, if we are Dense, the last child might be a 
                // Dense node that is physically full (Len 32) but structurally partial.
                // However, checking the physical size is usually enough here:
                var lastChild = node.Children[node.Len - 1]!;
                var totalSize = CountTree(lastChild, shift - Constants.RRB_BITS);
                if (totalSize < 1 << shift)
                    requiresRelaxation = true;
            }
        }

        // 1. Get Writable Node
        // If we detected a violation, we MUST force expansion to include a SizeTable.

        InternalNode<T> editable = requiresRelaxation 
            ? CreateRelaxedNodeFromDense(node, token, shift) 
            : node.EnsureEditable(token, true);

        // 2. Insert
        editable.Children[editable.Len] = childToAdd;

        // 3. Update SizeTable (It exists if we were already relaxed OR if we just forced it)
        if (editable.IsRelaxed())
        {
            var prevTotal = editable.Len > 0 ? editable.SizeTable![editable.Len - 1] : 0;

            var addedSize = shift == Constants.RRB_BITS
                ? AsLeaf(childToAdd).Len
                : CountTree(childToAdd, shift - Constants.RRB_BITS);

            editable.SizeTable![editable.Len] = prevTotal + addedSize;
        }

        editable.Len++;
        return editable;
    }


    private static InternalNode<T> CreateRelaxedNodeFromDense<T>(InternalNode<T> node, OwnerId token, int shift)
    {
        // 1. Determine Capacity
        // If we have an Owner (Transient), we go full capacity (32).
        // If Persistent, we allocate exactly existing Len + 1 (room for the new child).
        var newCap = !token.IsNone ? Constants.RRB_BRANCHING : node.Len + 1;

        var newChildren = new Node<T>?[newCap];
        var newTable = new int[newCap];

        Array.Copy(node.Children, newChildren, node.Len);

        // 2. Build the Size Table
        // Since 'node' was Dense, we know all children 0 to Len-2 are FULL.
        // Only node.Children[Len-1] might be partial.

        var blockSize = 1 << shift;
        var currentSum = 0;
        var childShift = shift - Constants.RRB_BITS;

        for (var i = 0; i < node.Len; i++)
        {
            if (i < node.Len - 1)
                // Guaranteed full
                currentSum += blockSize;
            else
                // Last child: Calculate actual size
                // (We can assume the last child is Dense because the parent was Dense)
                currentSum += CountTree(node.Children[i]!, childShift);
            // Or use RrbAlgorithm.CountTree which handles the flags check
            newTable[i] = currentSum;
        }

        // 3. Return new node marked as Relaxed
        var newNode = new InternalNode<T>(newChildren, newTable, node.Len, token);
        // Note: InternalNode ctor sets IsRelaxed if table != null
        return newNode;
    }




    public static Node<T> AppendLeafToTree<T>(Node<T>? root, LeafNode<T> leafToPush, ref int shift, OwnerId token)
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
            newSizeTable = new int[!token.IsNone ? Constants.RRB_BRANCHING : 2];
            newSizeTable[0] = rootTotalSize;
            newSizeTable[1] = rootTotalSize + leafToPush.Len;
        }

        //Create the new Parent
        var newChildren = new Node<T>?[!token.IsNone ? Constants.RRB_BRANCHING : 2];
        newChildren[0] = root;
        newChildren[1] = CreatePath(shift, leafToPush, token);

        var newParent = new InternalNode<T>(newChildren, newSizeTable, 2, token);

        // Update shift for the caller
        shift += Constants.RRB_BITS;

        return newParent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Node<T> CreatePath<T>(int shift, LeafNode<T> tail, OwnerId token)
    {
        // Base Case: Leaf
        if (shift == 0) return tail;

        // Recursive Step
        var child = CreatePath(shift - Constants.RRB_BITS, tail, token);

        // --- Validation Logic ---
        // A node MUST be Relaxed if:
        // 1. The child itself is Relaxed (Relaxation bubbles up).
        // 2. The child is not physically full (Violates strict Dense invariant).

        var childIsRelaxed = child.IsRelaxed();

        // Calculate if strictly full (1 << shift items)
        // Since this is a single path, the total size is just the tail length.
        // (Optimization: We can check tail.Len directly against the shift capacity)
        var isFull = tail.Len == 1 << shift;

        if (childIsRelaxed || !isFull)
        {
            // Create Relaxed Parent
            var children = new Node<T>?[!token.IsNone ? Constants.RRB_BRANCHING : 1];
            children[0] = child;

            var sizeTable = new int[children.Length];
            sizeTable[0] = tail.Len; // The total size is just the tail

            return new InternalNode<T>(children, sizeTable, 1, token);
        }

        // Create Dense Parent
        // Only allowed if child is NOT relaxed AND we are strictly full.
        var node = new InternalNode<T>(1, token);
        node.Children[0] = child;
        return node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Node<T> CreateNewParent<T>(Node<T> left, Node<T> right, OwnerId token)
    {
        if (left.Len < Constants.RRB_BRANCHING)
        {
            var sizeTable = new int[!token.IsNone ? Constants.RRB_BRANCHING : 2];
            sizeTable[0] = left.Len;
            sizeTable[1] = left.Len + right.Len;

            var children = new Node<T>?[!token.IsNone ? Constants.RRB_BRANCHING : 2];
            children[0] = left;
            children[1] = right;

            return new InternalNode<T>(children, sizeTable, 2, token);
        }

        var parent = new InternalNode<T>(2, token);
        parent.Children[0] = left;
        parent.Children[1] = right;
        return parent;
    }

    // This is a method to get the child index. If the node is dense it does a regular dense search
    // if it is relaxed, it uses AVX to search 8 elements at a time.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int childIndex, int relativeIndex) GetChildIndexAvx<T>(InternalNode<T> node, int index,
        int shift)
    {
        // Dense / Balanced Path (No SizeTable)
        if (node.IsDense())
        {
            var childIndex = (index >> shift) & Constants.RRB_MASK;
            var childStart = childIndex << shift;
            return (childIndex, index - childStart);
        }

        return GetRelaxedIndexAvx(node, index, shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int childIndex, int relativeIndex) GetRelaxedIndexAvx<T>(InternalNode<T> node, int index, int shift)
    {
        int len = node.Len;
        int i = 0;
        int[] table = node.SizeTable!;

        if (Vector256.IsHardwareAccelerated && len >= 8)
        {
            var vIndex = Vector256.Create(index);

            // Process in chunks of 8 elements natively without pinning memory pointers
            for (; i <= len - 8; i += 8)
            {
                var vTable = Vector256.LoadUnsafe(ref table[i]);

                // Performs comparison directly within integer hardware pipelines
                var vResult = Vector256.GreaterThan(vTable, vIndex);

                // Directly extracts the sign bits of each element without floating-point penalty
                uint mask = Vector256.ExtractMostSignificantBits(vResult);

                if (mask != 0)
                {
                    // Find first set bit (first child that is larger than index)
                    int offset = BitOperations.TrailingZeroCount(mask);
                    int matchIndex = i + offset;
                    int prevCount = matchIndex > 0 ? table[matchIndex - 1] : 0;

                    return (matchIndex, index - prevCount);
                }
            }
        }
        while (i < len && table[i] <= index) i++;

        var prev = i > 0 ? table[i - 1] : 0;
        return (i, index - prev);
    }
    

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static InternalNode<T> AsInternal<T>(Node<T> node)
    {
#if DEBUG
    if (node is InternalNode<T> internalNode)
    {
        return internalNode;
    }
    throw new InvalidCastException($"Expected InternalNode, but found {node.GetType().Name}. Check your shift/height logic.");
#else
        return Unsafe.As<InternalNode<T>>(node);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LeafNode<T> AsLeaf<T>(Node<T> node)
    {
#if DEBUG
    if (node is LeafNode<T> leafNode)
    {
        return leafNode;
    }
    throw new InvalidCastException($"Expected LeafNode, but found {node.GetType().Name}. Check your shift/height logic.");
#else
        return Unsafe.As<LeafNode<T>>(node);
#endif
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

            return new LeafNode<T>(newItems, leaf.Len - 1, OwnerId.None);
        }

        // Internal Level: Find child and recurse
        var internalNode = (InternalNode<T>)node;
        var (childIndex, subIndex) = GetChildIndexAvx(internalNode, index, shift);

        var child = internalNode.Children[childIndex]!;
        var newChild = RemoveRecursive(child, subIndex, shift - Constants.RRB_BITS);


        // Best case: The child became empty (remove it from children array)
        if (newChild == null)
        {
            // If this was the only child, this node also becomes empty
            if (internalNode.Len == 1) return null;

            var newLen = internalNode.Len - 1;
            var newChildren = new Node<T>?[newLen];

            // Copy children before
            if (childIndex > 0)
                Array.Copy(internalNode.Children, 0, newChildren, 0, childIndex);

            // Copy children after (shifting left)
            if (childIndex < newLen)
                Array.Copy(internalNode.Children, childIndex + 1, newChildren, childIndex, newLen - childIndex);

            // Unless we update the sizetable after removing a node, we will get a lot of nagging
            var newSizeTable = new int[newLen];

            if (internalNode.SizeTable != null)
            {
                // Copy part before
                if (childIndex > 0)
                    Array.Copy(internalNode.SizeTable, newSizeTable, childIndex);

                // Copy part after, subtracting 1 from all cumulative counts
                // (We removed exactly 1 item from the tree below)
                for (var i = childIndex; i < newLen; i++) newSizeTable[i] = internalNode.SizeTable[i + 1] - 1;
            }
            else
            {
                // Convert Dense -> Relaxed
                var childShift = shift - Constants.RRB_BITS;

                // Reconstruct table. 
                // RemoveRecursive removes ONE item. If the child returns null, it means 
                // that child contained ONLY that one item.
                // So we subtract 1 from the total.

                var currentSum = 0;
                // Iterate over the new structure (skipping the removed child)
                for (var i = 0; i < newLen; i++)
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

            return new InternalNode<T>(newChildren, newSizeTable, newLen, OwnerId.None);
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
            var newSizeTable = new int[newLen];

            if (internalNode.SizeTable != null)
            {
                // Copy before
                Array.Copy(internalNode.SizeTable, newSizeTable, childIndex);

                // Adjust current and after
                newSizeTable[childIndex] = internalNode.SizeTable[childIndex] - 1;
                for (var i = childIndex + 1; i < newLen; i++) newSizeTable[i] = internalNode.SizeTable[i] - 1;
            }
            else
            {
                // Dense -> Relaxed
                // We must build the table because index arithmetic breaks.
                var childShift = shift - Constants.RRB_BITS;
                var blockSize = 1 << shift;

                var currentSum = 0;
                for (var i = 0; i < newLen; i++)
                {
                    if (i == childIndex)
                    {
                        // This is the modified child. It is 1 smaller than before.
                        // If it was the last child, we calculate exact size.
                        // If it was a middle child, it WAS full, so now it is blockSize - 1.
                        if (i == newLen - 1)
                            currentSum += CountTree(newChild, childShift);
                        else
                            currentSum += blockSize - 1;
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

            return new InternalNode<T>(newChildren, newSizeTable, newLen, OwnerId.None);
        }
    }


    public static InsertResult<T> InsertRecursive<T>(Node<T> node, int index, T item, int shift, OwnerId token)
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
                return new InsertResult<T>(new LeafNode<T>(newItems, leaf.Len + 1, OwnerId.None));
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

            return new InsertResult<T>(new LeafNode<T>(leftArr, splitPoint, OwnerId.None),
                new LeafNode<T>(rightArr, rightLen, OwnerId.None));
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
            else if (childIndex < internalNode.Len - 1 || result.NewNode.IsRelaxed())
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

                // 2. The Modified Child
                // Do not assume 'blockSize + 1'. If we are fixing a Size 1 node (childIndex == Len-1),
                // the old size was NOT necessarily blockSize.
                if (childIndex < internalNode.Len - 1)
                    // Middle child was definitely full before.
                    currentSum += blockSize + 1;
                else
                    // Last child was potentially partial. Calculate actual new size.
                    // We can trust CountTree because we are in the "no split" zone.
                    currentSum += CountTree(result.NewNode, childShift);
                newSizeTable[childIndex] = currentSum;

                // Handle Children after the modified index
                // They are still full (blockSize), except possibly the very last one.
                for (var i = childIndex + 1; i < internalNode.Len; i++)
                {
                    int size = (i == internalNode.Len - 1) 
                        ? CountTree(internalNode.Children[i]!, childShift)
                        : blockSize;

                    currentSum += size;
                    newSizeTable[i] = currentSum;
                }
            }

            return new InsertResult<T>(new InternalNode<T>(newChildren, newSizeTable, internalNode.Len, OwnerId.None));
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

            int[]? newSizeTable;

            if (internalNode.SizeTable != null)
            {
                newSizeTable = new int[newLen];
                Array.Copy(internalNode.SizeTable, newSizeTable, childIndex);

                var prevTotal = childIndex > 0 ? newSizeTable[childIndex - 1] : 0;
                var leftSize = CountTree(result.NewNode, shift - Constants.RRB_BITS);
                var rightSize = CountTree(result.Overflow, shift - Constants.RRB_BITS);

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
                currentSum += CountTree(result.NewNode, childShift);
                newSizeTable[childIndex] = currentSum;

                currentSum += CountTree(result.Overflow, childShift);
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

            return new InsertResult<T>(new InternalNode<T>(newChildren, newSizeTable, newLen, OwnerId.None));
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
        const int splitPoint = 16;
        const int rightLen = 17; // 33 - 16

        var leftChildren = new Node<T>?[splitPoint];
        var rightChildren = new Node<T>?[rightLen];

        // Helper to get from the logical sequence of 33
        Node<T> GetVirtualChild(int i)
        {
            if (i < splitChildIndex) return node.Children[i]!;
            if (i == splitChildIndex) return childLeft;
            if (i == splitChildIndex + 1) return childRight;
            return node.Children[i - 1]!;
        }

        for (var i = 0; i < splitPoint; i++) leftChildren[i] = GetVirtualChild(i);
        for (var i = 0; i < rightLen; i++) rightChildren[i] = GetVirtualChild(splitPoint + i);

        var leftTable = new int[splitPoint];
        var rightTable = new int[rightLen];
        var childShift = shift - Constants.RRB_BITS;

        // Recalculate all sizes. 
        // This is safer than trying to reuse parts of the old table because 
        // splitting Dense nodes creates complex offset shifts.

        var cumulative = 0;

        // Fill Left
        for (var i = 0; i < splitPoint; i++)
        {
            // Re-use logic: Measure new nodes, assume blocksize for others (unless table exists)
            var virtualIdx = i;
            var size = GetVirtualChildSize(node, virtualIdx, splitChildIndex, childLeft, childRight, childShift);
            cumulative += size;
            leftTable[i] = cumulative;
        }

        // Fill Right (Reset cumulative)
        cumulative = 0;
        for (var i = 0; i < rightLen; i++)
        {
            var virtualIdx = splitPoint + i;
            var size = GetVirtualChildSize(node, virtualIdx, splitChildIndex, childLeft, childRight, childShift);
            cumulative += size;
            rightTable[i] = cumulative;
        }

        var newLeft = new InternalNode<T>(leftChildren, leftTable, splitPoint, OwnerId.None);
        var newRight = new InternalNode<T>(rightChildren, rightTable, rightLen, OwnerId.None);

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
        if (virtualIndex == splitIndex) return CountTree(newLeft, childShift);
        if (virtualIndex == splitIndex + 1) return CountTree(newRight, childShift);

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

    // Helper return struct to avoid Tuple allocation
    internal readonly struct InsertResult<T>(Node<T> newNode, Node<T>? overflow = null)
    {
        public readonly Node<T> NewNode = newNode;
        public readonly Node<T>? Overflow = overflow; // If not null, the node split
    }
    
}