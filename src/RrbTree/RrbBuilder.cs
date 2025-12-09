namespace Collections;

/**
 * <summary>
 *     Provides a mutable builder for creating or modifying an <see cref="RrbList{T}" /> efficiently.
 * </summary>
 * <typeparam name="T">The type of elements in the list.</typeparam>
 */
public class RrbBuilder<T>
{
    private readonly T[] _tail;

    private readonly int _tailCapacity;
    private Node<T>? _root;
    private int _shift;
    private int _tailLen;
    private OwnerToken _token;

    /**
     * <summary>
     *     Initializes a new instance of the <see cref="RrbBuilder{T}" /> class with a default tail capacity(32).
     * </summary>
     */
    public RrbBuilder() : this(Constants.RRB_BRANCHING)
    {
    }

    /**
     * <summary>
     *     Initializes a new instance of the <see cref="RrbBuilder{T}" /> class with a specified leaf capacity.
     * </summary>
     * <param name="leafCapacity">The capacity of the tail buffer, which must be a multiple of the branching factor (32).</param>
     */
    public RrbBuilder(int leafCapacity)
    {
        if (leafCapacity < Constants.RRB_BRANCHING || leafCapacity % Constants.RRB_BRANCHING != 0)
            throw new ArgumentException($"Capacity must be a multiple of {Constants.RRB_BRANCHING}.");

        _tailCapacity = leafCapacity;
        _token = new OwnerToken();
        // Allocate the fat tail immediately
        _tail = new T[_tailCapacity];
        Count = 0;
        _shift = 0;
        _tailLen = 0;
    }

    internal RrbBuilder(RrbList<T> list)
    {
        _token = new OwnerToken();
        _root = list.Root;
        Count = list.Count;
        _shift = list.Shift;
        _tailLen = list.TailLen;
        _tailCapacity = Constants.RRB_BRANCHING;
        _tail = new T[_tailCapacity];
        list.Tail.Items.CopyTo(_tail, 0);
    }

    /**
     * <summary>
     *     Gets the number of elements contained in the builder.
     * </summary>
     */
    public int Count { get; private set; }

    /**
     * <summary>
     *     Gets or sets the element at the specified index.
     * </summary>
     * <param name="index">The zero-based index of the element to get or set.</param>
     * <returns>The element at the specified index.</returns>
     */
    public T this[int index]
    {
        get // This logic is the same as for the persistent list, but with fat tail support
        {
            if (index < 0 || index >= Count) throw new IndexOutOfRangeException();
            var tailOffset = Count - _tailLen;
            if (index >= tailOffset) return _tail[index - tailOffset];

            var current = _root!;
            for (var s = _shift; s > 0; s -= Constants.RRB_BITS)
            {
                var inode = (InternalNode<T>)current;
                int childIndex;
                if (inode.SizeTable != null)
                {
                    childIndex = 0;
                    while (inode.SizeTable[childIndex] <= index) childIndex++;
                    if (childIndex > 0) index -= inode.SizeTable[childIndex - 1];
                }
                else
                {
                    childIndex = (index >> s) & Constants.RRB_MASK;
                }

                current = inode.Children[childIndex]!;
            }

            return ((LeafNode<T>)current).Items[index & Constants.RRB_MASK];
        }
        set => SetItem(index, value);
    }

    /**
     * <summary>
     *     Adds an object to the end of the list.
     * </summary>
     * <param name="item">The object to be added to the end of the list.</param>
     */
    public void Add(T item)
    {
        // Fast Path: Append to our (potentially fat) tail
        if (_tailLen < _tailCapacity)
        {
            _tail[_tailLen++] = item;
            Count++;
            return;
        }

        // Slow Path: Tail is full (e.g. 1024 items)
        PushFullTail();

        // Clear the tail. We can safely reuse it. 
        Array.Clear(_tail);
        _tail[0] = item;
        _tailLen = 1;
        Count++;
    }

    private void PushFullTail()
    {
        // We have a "Fat Tail" node (e.g. 1024 items).
        // We cannot push this directly into the tree because the tree expects size-32 leaves.
        // Thus chop chop

        var tailSpan = _tail.AsSpan();
        var chunks = _tailCapacity / Constants.RRB_BRANCHING;


        for (var i = 0; i < chunks; i++)
        {
            var chunkItems = tailSpan.Slice(i * Constants.RRB_BRANCHING,
                                                Constants.RRB_BRANCHING).ToArray();

            // We create a Transient node (owned by token) so PushDownTail can mutate it if needed?
            // Actually, once pushed to tree, it's part of the structure.
            var leaf = new LeafNode<T>(chunkItems, Constants.RRB_BRANCHING, _token);
            
            _root = RrbAlgorithm.AppendLeafToTree(_root, leaf, ref _shift, _token);
            
        }
    }

    /**
     * <summary>
     *     Creates an immutable <see cref="RrbList{T}" /> from the contents of this builder.
     * </summary>
     * <returns>An immutable list.</returns>
     */
    public RrbList<T> ToImmutable()
    {
        // I added this because the code below interacted weirdly with an old version of TryPushDownTail
        // I commented it out, and everything stil passes. Keeping it in until better times.
        // if (_tailCapacity == Constants.RRB_BRANCHING)
        // {
        //     // 3. Freeze Root
        //     var newRoot = _root;
        //     if (newRoot is InternalNode<T> intNode) newRoot = intNode.Freeze();
        //     else if (newRoot is LeafNode<T> leafNode) newRoot = leafNode.Freeze();
        //
        //     _token = new OwnerToken();
        //     var nTail = _tail.AsSpan().Slice(0, _tailLen).ToArray();
        //
        //     return new RrbList<T>(newRoot,
        //         new LeafNode<T>(nTail, _tailLen, null),
        //         Count,
        //         _shift,
        //         _tailLen);
        // }

        // Flush full chunks from current tail
        var fullChunks = _tailLen / Constants.RRB_BRANCHING;
        var remainder = _tailLen % Constants.RRB_BRANCHING;

        for (var i = 0; i < fullChunks; i++)
        {
            var chunkItems = new T[Constants.RRB_BRANCHING];
            Array.Copy(_tail, i * Constants.RRB_BRANCHING, chunkItems, 0, Constants.RRB_BRANCHING);
            var leaf = new LeafNode<T>(chunkItems, Constants.RRB_BRANCHING, null); // Immutable

            // AppendLeafToTree is robust enough to handle when _root is null.
            // We previously checked it here, but it is no longer needed.
            _root = RrbAlgorithm.AppendLeafToTree(_root, leaf, ref _shift, null); // Persistent push
        }

        // Create final tail from remainder
        LeafNode<T> finalTail;
        if (remainder > 0)
        {
            var tailItems = new T[remainder];
            Array.Copy(_tail, fullChunks * Constants.RRB_BRANCHING, tailItems, 0, remainder);
            finalTail = new LeafNode<T>(tailItems, remainder, null);
        }
        else
        {
            finalTail = LeafNode<T>.Empty;
        }

        // 3. Freeze Root
        var frozenRoot = _root;
        if (frozenRoot is InternalNode<T> inode) frozenRoot = inode.Freeze();
        else if (frozenRoot is LeafNode<T> lnode) frozenRoot = lnode.Freeze();

        _token = new OwnerToken();

        return new RrbList<T>(frozenRoot, finalTail, Count, _shift, remainder);
    }

    /**
     * <summary>
     *     Replaces the element at the specified index with the new value.
     * </summary>
     * <param name="index">The index of the element to replace.</param>
     * <param name="value">The new value for the element.</param>
     */
    public void SetItem(int index, T value)
    {
        if (index < 0 || index >= Count) throw new IndexOutOfRangeException();

        var tailOffset = Count - _tailLen;
        if (index >= tailOffset)
        {
            _tail[index - tailOffset] = value;
            return;
        }

        _root = RrbAlgorithm.Update(_root!, index, value, _shift, _token);
    }
}