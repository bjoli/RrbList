using System.Runtime.CompilerServices;

namespace Collections;

/// A transient RRB-List. Most vecs a program builds are small, so nothing is
/// allocated until it is needed and everything starts small: the tail grows
/// 4, 8, 16, 32 before the first leaf is made, and the array that collects
/// full leaves grows the same way up to a chunk. A builder of three items is
/// the builder and one array of four.
public class RrbBuilder<T>
{
    private Node<T>? _root;
    private int _shift;
    private int _rootCount;
    
    // Full leaves wait here until ToImmutable puts them in the tree. Chunks of
    // a fixed size rather than one array that doubles, so that a big vec never
    // makes an array large enough for the large object heap. Every chunk in
    // `_chunks` is full and ChunkSize long; `_currentChunk` grows up to that.
    private const int ChunkSize = 512; // 512 * 8 bytes = 4KB (Well within Gen 0)
    private const int FirstChunkSize = 4;
    private const int FirstTailSize = 4;
    
    private List<LeafNode<T>[]>? _chunks;
    private LeafNode<T>[]? _currentChunk;
    // Index in the current chunk
    private int _chunkIndex;
    // Total leaves stored across all chunks
    private int _totalLeaves;
    
    // The tail is handed to the list ToImmutable makes when its length is its
    // count. It is shared from then on, and the builder copies it before
    // changing it. A shared tail is always full to its length, so Add's fast
    // path never writes to one.
    private T[] _currentTail;
    private int _currentTailLen;
    private bool _tailShared;
    
    private OwnerId _token;

    public RrbBuilder()
    {
        _token = OwnerId.Next();
        _currentTail = Array.Empty<T>();
    }

    internal RrbBuilder(RrbList<T> list)
    {
        _token = OwnerId.Next();
        _root = list.Root;
        _rootCount = list.Count - list.TailLen;
        _shift = list.Shift;
        
        if (list.TailLen == list.Tail.Length)
        {
            _currentTail = list.Tail;
            _tailShared = list.TailLen > 0;
        }
        else
        {
            _currentTail = new T[list.TailLen];
            Array.Copy(list.Tail, _currentTail, list.TailLen);
        }
        _currentTailLen = list.TailLen;
    }

    /// <summary>
    /// Creates a new RRB-List from an array directly by chunking it.
    /// </summary>
    /// <param name="items">The array of items.</param>
    /// <param name="reuseArrayIfShorterThan32">If true and the array is small enough to fit in the tail, it will be used directly instead of copied.</param>
    /// <returns>A new RRB-List.</returns>
    public static RrbList<T> FromArray(T[] items, bool reuseArrayIfShorterThan32 = false)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (items.Length == 0) return RrbList<T>.Empty;

        if (items.Length <= Constants.RRB_BRANCHING)
        {
            var tail = reuseArrayIfShorterThan32 ? items : (T[])items.Clone();
            return new RrbList<T>(null, tail, items.Length, 0, items.Length);
        }

        var builder = new RrbBuilder<T>();
        builder.AddRange(items);
        return builder.ToImmutable();
    }

    /// <summary>
    /// Creates a new RRB-List from a ReadOnlySpan directly by chunking it.
    /// </summary>
    /// <param name="items">The span of items.</param>
    /// <returns>A new RRB-List.</returns>
    public static RrbList<T> FromSpan(ReadOnlySpan<T> items)
    {
        if (items.Length == 0) return RrbList<T>.Empty;

        if (items.Length <= Constants.RRB_BRANCHING)
        {
            var tail = items.ToArray();
            return new RrbList<T>(null, tail, items.Length, 0, items.Length);
        }

        var builder = new RrbBuilder<T>();
        builder.AddRange(items);
        return builder.ToImmutable();
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
    
    private int FullChunks => _chunks?.Count ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LeafNode<T>[] ChunkAt(int chunkIdx) =>
        chunkIdx < FullChunks ? _chunks![chunkIdx] : _currentChunk!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LeafNode<T> GetLeaf(int globalIndex) =>
        ChunkAt(globalIndex / ChunkSize)[globalIndex % ChunkSize];
    
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
             
             var targetChunk = ChunkAt(globalLeafIdx / ChunkSize);
             int idxInChunk = globalLeafIdx % ChunkSize;
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

         if (_tailShared) UnshareTail(_currentTail.Length);
         _currentTail[pendingIndex - pendingTotal] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        var tail = _currentTail;
        var len = _currentTailLen;
        if ((uint)len < (uint)tail.Length)
        {
            tail[len] = item;
            _currentTailLen = len + 1;
            return;
        }

        AddSlow(item);
    }
        
    // The tail is full to its length: it is a leaf's worth and becomes one, or
    // it grows.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddSlow(T item)
    {
        if (_currentTailLen == Constants.RRB_BRANCHING)
        {
            PushTailAsLeaf();
            var fresh = new T[Constants.RRB_BRANCHING];
            fresh[0] = item;
            _currentTail = fresh;
            _currentTailLen = 1;
            return;
        }

        var capacity = _currentTailLen == 0
            ? FirstTailSize
            : Math.Min(_currentTailLen * 2, Constants.RRB_BRANCHING);
        UnshareTail(capacity);
        _currentTail[_currentTailLen++] = item;
    }

    // Replaces the tail with a copy of `capacity`, which is the builder's own.
    private void UnshareTail(int capacity)
    {
        var copy = new T[capacity];
        Array.Copy(_currentTail, copy, _currentTailLen);
        _currentTail = copy;
        _tailShared = false;
    }

    // A full tail becomes a leaf. One shared with a list is frozen, so that the
    // builder clones it before setting an item in it.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushTailAsLeaf()
    {
        var owner = _tailShared ? OwnerId.None : _token;
        AddLeaf(new LeafNode<T>(_currentTail, Constants.RRB_BRANCHING, owner));
        _tailShared = false;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        if (items.Length == 0) return;

        int offset = 0;
        int remaining = items.Length;

        // Fill the tail up to a leaf. When nothing will be left over, the tail
        // is made exactly as long as it needs to be, so that ToImmutable can
        // hand it over without copying.
        if (_currentTailLen < Constants.RRB_BRANCHING)
        {
            int toTail = Math.Min(Constants.RRB_BRANCHING - _currentTailLen, remaining);
            int needed = _currentTailLen + toTail;
            if (_tailShared || needed > _currentTail.Length)
                UnshareTail(remaining > toTail ? Constants.RRB_BRANCHING : needed);
            
            items.Slice(0, toTail).CopyTo(_currentTail.AsSpan(_currentTailLen));
            _currentTailLen = needed;
            offset = toTail;
            remaining -= toTail;
        }

        if (remaining == 0) return;

        // The tail is full, and more follows: room for it and for every whole
        // leaf after it.
        ReserveLeaves(1 + ((remaining - 1) >> Constants.RRB_BITS));
        PushTailAsLeaf();

        // The chunk, its index and the token are kept in locals, and put back
        // only around the call that finds a new chunk.
        var token = _token;
        var chunk = _currentChunk!;
        var index = _chunkIndex;
        var added = 0;
        while (remaining > Constants.RRB_BRANCHING)
        {
            var leafItems = new T[Constants.RRB_BRANCHING];
            items.Slice(offset, Constants.RRB_BRANCHING).CopyTo(leafItems);
            var leaf = new LeafNode<T>(leafItems, Constants.RRB_BRANCHING, token);
            
            if ((uint)index < (uint)chunk.Length)
            {
                chunk[index++] = leaf;
                added++;
            }
            else
            {
                _chunkIndex = index;
                _totalLeaves += added;
                added = 0;
                AddLeafToNewRoom(leaf);
                chunk = _currentChunk!;
                index = _chunkIndex;
            }
            
            offset += Constants.RRB_BRANCHING;
            remaining -= Constants.RRB_BRANCHING;
        }
        _chunkIndex = index;
        _totalLeaves += added;

        _currentTail = items.Slice(offset, remaining).ToArray();
        _currentTailLen = remaining;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddLeaf(LeafNode<T> leaf)
    {
        var chunk = _currentChunk;
        var index = _chunkIndex;
        if (chunk != null && (uint)index < (uint)chunk.Length)
        {
            chunk[index] = leaf;
            _chunkIndex = index + 1;
            _totalLeaves++;
            return;
        }
        
        AddLeafToNewRoom(leaf);
    }

    // The current chunk is full, or there is none: it grows up to a chunk, or
    // is put with the full ones and a new chunk begins.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddLeafToNewRoom(LeafNode<T> leaf)
    {
        var chunk = _currentChunk;
        if (chunk == null)
        {
            chunk = _currentChunk = new LeafNode<T>[FirstChunkSize];
        }
        else
        {
            if (chunk.Length < ChunkSize)
            {
                Array.Resize(ref _currentChunk, Math.Min(chunk.Length * 2, ChunkSize));
                chunk = _currentChunk;
            }
            else
            {
                (_chunks ??= new List<LeafNode<T>[]>()).Add(chunk);
                chunk = _currentChunk = new LeafNode<T>[ChunkSize];
                _chunkIndex = 0;
            }
        }

        chunk[_chunkIndex++] = leaf;
        _totalLeaves++;
    }

    // Room for `count` more leaves in the current chunk, as far as a chunk
    // goes, so that a range added at once is not copied as the chunk grows.
    private void ReserveLeaves(int count)
    {
        if (count <= 0) return;
        if (_currentChunk == null)
        {
            _currentChunk = new LeafNode<T>[Math.Clamp(count, FirstChunkSize, ChunkSize)];
            return;
        }

        int wanted = _chunkIndex + count;
        if (wanted > _currentChunk.Length && _currentChunk.Length < ChunkSize)
            Array.Resize(ref _currentChunk, Math.Min(Math.Max(wanted, _currentChunk.Length * 2), ChunkSize));
    }

    public RrbList<T> ToImmutable()
    {
        if (_totalLeaves > 0)
        {
            FlushLeavesToTree();
            
            // Reset state, keeping the current chunk for the leaves to come.
            _chunks?.Clear();
            Array.Clear(_currentChunk!, 0, _chunkIndex);
            _chunkIndex = 0;
            _totalLeaves = 0;
        }

        if (_root == null)
        {
            if (_currentTailLen == 0) return RrbList<T>.Empty;
            var tail = TakeTail();
            return new RrbList<T>(null, tail, _currentTailLen, 0, _currentTailLen);
        }

        var frozenRoot = _root;
        if (frozenRoot is InternalNode<T> inode) frozenRoot = inode.Freeze(_token);
        else if (frozenRoot is LeafNode<T> lnode) frozenRoot = lnode.Freeze(_token);
        
        var finalTail = TakeTail();
        var totalCount = _rootCount + _currentTailLen;
        
        _token = OwnerId.Next();
        _root = frozenRoot;
        
        return new RrbList<T>(frozenRoot, finalTail, totalCount, _shift, finalTail.Length);
    }

    // The tail for a list: the builder's own when it is exactly as long as its
    // count, which the builder then shares, and otherwise a copy.
    private T[] TakeTail()
    {
        if (_currentTailLen == 0) return Array.Empty<T>();
        if (_currentTailLen == _currentTail.Length)
        {
            _tailShared = true;
            return _currentTail;
        }

        var tail = new T[_currentTailLen];
        Array.Copy(_currentTail, tail, _currentTailLen);
        return tail;
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
            var chunk = ChunkAt(cIdx);
    
            // 3. Check if the copy spans across a chunk boundary. Only a full
            // chunk can be crossed: the current one holds every leaf left.
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
                chunk = ChunkAt(cIdx + 1);
        
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