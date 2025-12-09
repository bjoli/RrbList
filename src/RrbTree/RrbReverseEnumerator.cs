using System.Collections;
using System.Runtime.CompilerServices;

namespace Collections;

public struct RrbReverseEnumerator<T> : IEnumerator<T>
{
    private readonly RrbList<T> _list;

    // --- Hoisted Hot Path Fields ---
    private T[]? _currentItems;
    private int _leafIndex;
    private int _totalIndex;
    // -------------------------------

    private readonly int _startIndex;
    private readonly int _count; // Limit how many items to yield

    private readonly Node<T>?[] _path;
    private readonly int[] _pathIndexes;
    private int _depth;

    public RrbReverseEnumerator<T> GetEnumerator()
    {
        return this;
    }

    // Overload 1: Reverse the WHOLE list (End -> 0)
    public RrbReverseEnumerator(RrbList<T> list)
        : this(list, list.Count - 1, list.Count)
    {
    }

    // Overload 2: Reverse from a specific index down to 0
    public RrbReverseEnumerator(RrbList<T> list, int startIndex)
        : this(list, startIndex, startIndex + 1)
    {
    }

    // Iterates backwards starting FROM 'startIndex' (inclusive).
    public RrbReverseEnumerator(RrbList<T> list, int startIndex, int count)
    {
        if (startIndex < -1 || startIndex >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));

        if (count < 0 || startIndex - count + 1 < -1) // Bounds check logic
            throw new ArgumentOutOfRangeException(nameof(count));

        _list = list;
        _startIndex = startIndex;
        _count = count;

        // Initialize state so first MoveNext() triggers MoveNextRare()
        _totalIndex = startIndex + 1; // Start "after" the item
        _currentItems = null;
        _leafIndex = -2; // Sentinel

        _path = new Node<T>?[Constants.RRB_MAX_HEIGHT + 1];
        _pathIndexes = new int[Constants.RRB_MAX_HEIGHT + 1];
        _depth = 0;
    }

    public T Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _currentItems![_leafIndex];
    }

    object IEnumerator.Current => Current!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        // Hot Path: Decrement and check bounds (down to 0)
        if (--_leafIndex >= 0)
        {
            _totalIndex--;
            return true;
        }

        return MoveNextRare();
    }

    private bool MoveNextRare()
    {
        _totalIndex--;

        // Check Limit (Range Iteration)
        // If we have yielded '_count' items, stop.
        // Stop condition: _totalIndex < (_startIndex - _count + 1)
        if (_totalIndex <= _startIndex - _count) return false;

        // 1. Check Tail
        var tailOffset = _list.Count - _list.TailLen;
        if (_totalIndex >= tailOffset)
        {
            var tail = _list.Tail;
            _currentItems = tail.Items;
            // In reverse, leafIndex is simply the offset
            _leafIndex = _totalIndex - tailOffset;
            return true;
        }

        // 2. Traverse Tree
        if (_currentItems == null)
            // First time entering tree from start (or tail)
            SetupStack(_list.Root!, _list.Shift, _totalIndex);
        else
            AdvanceStack();

        return true;
    }

    private void SetupStack(Node<T> root, int shift, int targetIndex)
    {
        // Logic identical to Forward Enumerator (drill down to specific index)
        _depth = 0;
        var current = root;
        var currentShift = shift;

        while (currentShift > 0)
        {
            var internalNode = (InternalNode<T>)current;
            var (childIndex, relativeIndex) = GetChildIndex(internalNode, targetIndex, currentShift);

            _path[_depth] = current;
            _pathIndexes[_depth] = childIndex;

            current = internalNode.Children[childIndex]!;
            _depth++;
            currentShift -= Constants.RRB_BITS;
            targetIndex = relativeIndex;
        }

        var leaf = (LeafNode<T>)current;
        _currentItems = leaf.Items;
        _leafIndex = targetIndex; // Exact index within leaf
    }

    private void AdvanceStack()
    {
        var d = _depth - 1;
        while (d >= 0)
        {
            var parent = (InternalNode<T>)_path[d]!;

            // REVERSE LOGIC: Move Left
            var prevIndex = _pathIndexes[d] - 1;

            if (prevIndex >= 0)
            {
                _pathIndexes[d] = prevIndex;
                var current = parent.Children[prevIndex]!;

                d++;
                while (d < _depth)
                {
                    _path[d] = current;
                    var internalNode = (InternalNode<T>)current;

                    // REVERSE LOGIC: Drill down LAST child
                    var lastChildIdx = internalNode.Len - 1;
                    _pathIndexes[d] = lastChildIdx;

                    current = internalNode.Children[lastChildIdx]!;
                    d++;
                }

                var leaf = (LeafNode<T>)current;
                _currentItems = leaf.Items;
                _leafIndex = leaf.Len - 1; // Start at END of new leaf
                return;
            }

            d--;
        }

        throw new InvalidOperationException("Iterator state corrupt.");
    }

    // Reuse the helper we discussed earlier
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int, int) GetChildIndex(InternalNode<T> node, int index, int shift)
    {
        if (node.SizeTable != null)
        {
            var childIndex = 0;
            while (childIndex < node.Len && node.SizeTable[childIndex] <= index) childIndex++;
            var prevCount = childIndex > 0 ? node.SizeTable[childIndex - 1] : 0;
            return (childIndex, index - prevCount);
        }
        else
        {
            var childIndex = (index >> shift) & Constants.RRB_MASK;
            var childStart = childIndex << shift;
            return (childIndex, index - childStart);
        }
    }

    public void Reset()
    {
        _totalIndex = _startIndex + 1;
        _currentItems = null;
        _leafIndex = -2;
        _depth = 0;
    }

    public void Dispose()
    {
        _currentItems = null;
    }
}