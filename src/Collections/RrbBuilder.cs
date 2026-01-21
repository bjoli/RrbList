using System.Runtime.CompilerServices;

namespace Collections;

public class RrbBuilder<T> where T : notnull
{
    private Node<T>? _root;
    private int _shift;
    private int _rootCount;
    
    // Instead of one giant array that resizes (hitting LOH), we use fixed chunks.
    // This is pretty huge, actually. 
    private const int ChunkSize = 512; // 512 * 8 bytes = 4KB (Well within Gen 0)
    
    // List of full chunks
    private readonly List<LeafNode<T>[]> _chunks;
    // Current active chunk
    private LeafNode<T>[] _currentChunk;
    // Index in the current chunk
    private int _chunkIndex;
    // Total leaves stored across all chunks
    private int _totalLeaves;
    
    // --- TAIL BUFFER ---
    private T[] _currentTail;
    private int _currentTailLen;
    
    private OwnerId _token;

    public RrbBuilder()
    {
        _token = OwnerId.Next();
        _currentTail = new T[Constants.RRB_BRANCHING];
        
        // Initialize Chunking
        _chunks = new List<LeafNode<T>[]>();
        _currentChunk = new LeafNode<T>[ChunkSize];
        _chunkIndex = 0;
        _totalLeaves = 0;
        
        _shift = 0;
        _rootCount = 0;
        _currentTailLen = 0;
    }

    internal RrbBuilder(RrbList<T> list)
    {
        _token = OwnerId.Next();
        _root = list.Root;
        _rootCount = list.Count - list.TailLen;
        _shift = list.Shift;
        
        _currentTail = new T[Constants.RRB_BRANCHING];
        if (list.TailLen > 0)
            Array.Copy(list.Tail, _currentTail, list.TailLen);
        _currentTailLen = list.TailLen;

        // Initialize Chunking
        _chunks = new List<LeafNode<T>[]>();
        _currentChunk = new LeafNode<T>[ChunkSize];
        _chunkIndex = 0;
        _totalLeaves = 0;
    }

    public int Count => _rootCount + (_totalLeaves * Constants.RRB_BRANCHING) + _currentTailLen;

    public T this[int index]
    {
        get
        {
            if (index < _rootCount)
            {
                return GetFromTree(_root!, index, _shift);
            }

            var pendingIndex = index - _rootCount;
            var pendingTotal = _totalLeaves * Constants.RRB_BRANCHING;
            
            if (pendingIndex < pendingTotal)
            {
                var globalLeafIdx = pendingIndex >> Constants.RRB_BITS; // / 32
                var itemIdx = pendingIndex & Constants.RRB_MASK;       // % 32
                
                return GetLeaf(globalLeafIdx).Items[itemIdx];
            }

            return _currentTail[pendingIndex - pendingTotal];
        }
        set => SetItem(index, value);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LeafNode<T> GetLeaf(int globalIndex)
    {
        // Determine which chunk the leaf is in
        int chunkIdx = globalIndex / ChunkSize;
        int idxInChunk = globalIndex % ChunkSize;

        if (chunkIdx < _chunks.Count)
            return _chunks[chunkIdx][idxInChunk];
        
        return _currentChunk[idxInChunk];
    }
    
    private T GetFromTree(Node<T> node, int index, int shift)
    {
        while (shift > 0)
        {
            var inode = RrbAlgorithm.AsInternal(node);
            if (inode.IsRelaxed())
            {
                var (childIdx, relIdx) = RrbAlgorithm.GetRelaxedIndexAvx(inode, index, shift);
                node = inode.Children[childIdx]!;
                index = relIdx;
            }
            else
            {
                var childIdx = (index >> shift) & Constants.RRB_MASK;
                node = inode.Children[childIdx]!;
            }
            shift -= Constants.RRB_BITS;
        }
        return RrbAlgorithm.AsLeaf(node).Items[index & Constants.RRB_MASK];
    }

    public void SetItem(int index, T value)
    {
         if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

         if (index < _rootCount)
         {
             _root = RrbAlgorithm.Update(_root!, index, value, _shift, _token);
             return;
         }

         var pendingIndex = index - _rootCount;
         var pendingTotal = _totalLeaves * Constants.RRB_BRANCHING;

         if (pendingIndex < pendingTotal)
         {
             var globalLeafIdx = pendingIndex >> Constants.RRB_BITS;
             var itemIdx = pendingIndex & Constants.RRB_MASK;
             
             // Chunk access logic inline for SetItem (cold path compared to Add)
             int chunkIdx = globalLeafIdx / ChunkSize;
             int idxInChunk = globalLeafIdx % ChunkSize;
             
             LeafNode<T>[] targetChunk = (chunkIdx < _chunks.Count) ? _chunks[chunkIdx] : _currentChunk;
             var leaf = targetChunk[idxInChunk];

             if (leaf.Owner != _token)
             {
                 leaf = leaf.CloneAndSet(itemIdx, value);
                 targetChunk[idxInChunk] = leaf;
             }
             else
             {
                 leaf.Items[itemIdx] = value;
             }
             return;
         }

         _currentTail[pendingIndex - pendingTotal] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_currentTailLen < Constants.RRB_BRANCHING)
        {
            _currentTail[_currentTailLen++] = item;
            return;
        }

        AddFullNode();
        
        _currentTail = new T[Constants.RRB_BRANCHING];
        _currentTail[0] = item;
        _currentTailLen = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddFullNode()
    {
        var newLeaf = new LeafNode<T>(_currentTail, Constants.RRB_BRANCHING, _token);
        
        // Chunk is full? Move to list and allocate new one.
        if (_chunkIndex == ChunkSize)
        {
            _chunks.Add(_currentChunk);
            _currentChunk = new LeafNode<T>[ChunkSize];
            _chunkIndex = 0;
        }
        
        _currentChunk[_chunkIndex++] = newLeaf;
        _totalLeaves++;
    }

    public RrbList<T> ToImmutable()
    {
        if (_totalLeaves > 0)
        {
            FlushLeavesToTree();
            
            // Reset state
            _chunks.Clear();
            // We can reuse the current allocated chunk to save 1 allocation
            Array.Clear(_currentChunk, 0, _chunkIndex);
            _chunkIndex = 0;
            _totalLeaves = 0;
        }

        var frozenRoot = _root;
        if (frozenRoot != null)
        {
            if (frozenRoot is InternalNode<T> inode) frozenRoot = inode.Freeze(_token);
            else if (frozenRoot is LeafNode<T> lnode) frozenRoot = lnode.Freeze(_token);
        }

        var finalTail = new T[_currentTailLen];
        Array.Copy(_currentTail, finalTail, _currentTailLen);
        
        var totalCount = _rootCount + _currentTailLen;
        
        _token = OwnerId.Next();
        _root = frozenRoot;
        
        return new RrbList<T>(frozenRoot, finalTail, totalCount, _shift, finalTail.Length);
    }
    
    private void FlushLeavesToTree()
    {
        // Tracks global progress across all chunks
        int globalLeafCursor = 0; 
        
        if (_root == null)
        {
            // Optimization: Directly grab the first leaf
            _root = GetLeaf(globalLeafCursor++);
            _shift = 0;
            _rootCount += Constants.RRB_BRANCHING;
        }
        
        if (_shift == 0)
        {
            var newRoot = new InternalNode<T>(Constants.RRB_BRANCHING, _token);
            newRoot.Children[0] = _root;
            newRoot.Len = 1; 
            _root = newRoot;
            _shift = Constants.RRB_BITS;
        }
        
        while (globalLeafCursor < _totalLeaves)
        {
            var rootInternal = RrbAlgorithm.AsInternal(_root).EnsureEditable(_token);
            _root = rootInternal;

            FillRightSpine(rootInternal, _shift, ref globalLeafCursor);

            if (globalLeafCursor < _totalLeaves)
            {
                var newRoot = new InternalNode<T>(Constants.RRB_BRANCHING, _token);
                newRoot.Children[0] = _root;
                newRoot.Len = 1;
                
                if (_root.IsRelaxed())
                {
                    var oldRootInternal = RrbAlgorithm.AsInternal(_root);
                    var oldSize = oldRootInternal.SizeTable![oldRootInternal.Len - 1];
                    var newTable = new int[Constants.RRB_BRANCHING];
                    newTable[0] = oldSize;
                    newRoot = new InternalNode<T>(newRoot.Children, newTable, 1, _token);
                }
                
                _root = newRoot;
                _shift += Constants.RRB_BITS;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void FillRightSpine(InternalNode<T> node, int shift, ref int globalCursor)
    {
        // 1. FILL EXISTING LAST CHILD
        if (node.Len > 0 && shift > Constants.RRB_BITS)
        {
            int lastIdx = node.Len - 1;
            InternalNode<T> lastChild = RrbAlgorithm.AsInternal(node.Children[lastIdx]!);
            
            //  Manual Owner Check to skip EnsureEditable call overhead
            if (lastChild.Owner != _token)
            {
                lastChild = lastChild.EnsureEditable(_token, expand: true);
                node.Children[lastIdx] = lastChild;
            }
            
            int cursorBefore = globalCursor;
            FillRightSpine(lastChild, shift - Constants.RRB_BITS, ref globalCursor);
            int leavesConsumed = globalCursor - cursorBefore;
            
            if (leavesConsumed > 0 && node.SizeTable != null)
            {
                int sizeDelta = leavesConsumed * Constants.RRB_BRANCHING;
                node.SizeTable[lastIdx] += sizeDelta;
            }
        }
        
        if (globalCursor >= _totalLeaves) return;

        // 2. FILL NEW SIBLINGS
        
        // A. BOTTOM LEVEL (Shift 5) - Loop Copy
        // A. BOTTOM LEVEL (Shift 5) - Array.Copy Optimization
        if (shift == Constants.RRB_BITS)
        {
            int spaceRemaining = Constants.RRB_BRANCHING - node.Len;
            int leavesAvailable = _totalLeaves - globalCursor;
            int countToCopy = Math.Min(spaceRemaining, leavesAvailable);

            int startIdx = node.Len;
            var children = node.Children;

            // 1. Resolve starting chunk coordinates ONCE
            int cIdx = globalCursor / ChunkSize;
            int iIdx = globalCursor % ChunkSize;
    
            // 2. Determine source chunk
            var chunk = (cIdx < _chunks.Count) ? _chunks[cIdx] : _currentChunk;
    
            // 3. Check if the copy spans across a chunk boundary
            int availableInChunk = ChunkSize - iIdx;
    
            if (countToCopy <= availableInChunk)
            {
                // FAST PATH: All items in one chunk
                Array.Copy(chunk, iIdx, children, startIdx, countToCopy);
            }
            else
            {
                // STRADDLE PATH: Items cross into the next chunk (Rare: 1 in 16 calls)
        
                // Copy Part 1 (End of current chunk)
                Array.Copy(chunk, iIdx, children, startIdx, availableInChunk);
        
                // Copy Part 2 (Start of next chunk)
                int remaining = countToCopy - availableInChunk;
                cIdx++; 
                chunk = (cIdx < _chunks.Count) ? _chunks[cIdx] : _currentChunk;
        
                Array.Copy(chunk, 0, children, startIdx + availableInChunk, remaining);
            }

            // Update SizeTable (Standard loop is fine here, simple integer add)
            if (node.IsRelaxed())
            {
                int currentTotal = node.Len > 0 ? node.SizeTable![node.Len - 1] : 0;
                var table = node.SizeTable;
                for (int i = 0; i < countToCopy; i++)
                {
                    currentTotal += Constants.RRB_BRANCHING;
                    table![startIdx + i] = currentTotal;
                }
            }

            node.Len += (byte)countToCopy;
            globalCursor += countToCopy;
            _rootCount += countToCopy * Constants.RRB_BRANCHING;
            return;
        }

        // B. INTERNAL LEVELS (Shift > 5)
        int childShift = shift - Constants.RRB_BITS;
        
        while (node.Len < Constants.RRB_BRANCHING && globalCursor < _totalLeaves)
        {
            var newSibling = new InternalNode<T>(Constants.RRB_BRANCHING, _token);
            newSibling.Len = 0;
            
            int cursorBefore = globalCursor;
            FillRightSpine(newSibling, childShift, ref globalCursor);
            int leavesConsumed = globalCursor - cursorBefore;
            
            int addedSize = leavesConsumed * Constants.RRB_BRANCHING;

            int idx = node.Len;
            node.Children[idx] = newSibling;
            
            if (node.IsRelaxed())
            {
                int prev = idx > 0 ? node.SizeTable![idx - 1] : 0;
                node.SizeTable![idx] = prev + addedSize;
            }

            node.Len++;
        }
    }
}