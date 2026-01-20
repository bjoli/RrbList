namespace Collections;

/**
 * <summary>
 *     Provides a mutable builder for creating or modifying an <see cref="RrbList{T}" /> efficiently.
 * </summary>
 * <typeparam name="T">The type of elements in the list.</typeparam>
 */
public class RrbBuilder<T> where T : notnull
{
    private readonly T[] _tail;

    private readonly int _tailCapacity;
    private Node<T>? _root;
    private int _shift;
    private int _tailLen;
    private OwnerId _token;

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
        _token = OwnerId.Next();
        // Allocate the fat tail immediately
        _tail = new T[_tailCapacity];
        Count = 0;
        _shift = 0;
        _tailLen = 0;
    }

    internal RrbBuilder(RrbList<T> list, int tailCapacity = Constants.RRB_BRANCHING)
    {
        _token = OwnerId.Next();
        _root = list.Root;
        Count = list.Count;
        _shift = list.Shift;
        _tailLen = list.TailLen;
        _tailCapacity = tailCapacity;
        _tail = new T[_tailCapacity];
        list.Tail.CopyTo(_tail, 0);
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
        PushFatTail();

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
        // Flush full chunks from current tail
        var fullChunks = _tailLen / Constants.RRB_BRANCHING;
        var remainder = _tailLen % Constants.RRB_BRANCHING;

        for (var i = 0; i < fullChunks; i++)
        {
            var chunkItems = new T[Constants.RRB_BRANCHING];
            Array.Copy(_tail, i * Constants.RRB_BRANCHING, chunkItems, 0, Constants.RRB_BRANCHING);
            var leaf = new LeafNode<T>(chunkItems, Constants.RRB_BRANCHING, OwnerId.None); // Immutable

            // AppendLeafToTree is robust enough to handle when _root is null.
            // We previously checked it here, but it is no longer needed.
            _root = RrbAlgorithm.AppendLeafToTree(_root, leaf, ref _shift, OwnerId.None); // Persistent push
        }

        // Create final tail from remainder
        T[] finalTail;
        if (remainder > 0)
        {
            var tailItems = new T[remainder];
            Array.Copy(_tail, fullChunks * Constants.RRB_BRANCHING, tailItems, 0, remainder);
            finalTail = tailItems;
        }
        else
        {
            finalTail = [];
        }

        // 3. Freeze Root
        var frozenRoot = _root;
        if (frozenRoot is InternalNode<T> inode) frozenRoot = inode.Freeze(_token);
        else if (frozenRoot is LeafNode<T> lnode) frozenRoot = lnode.Freeze(_token);

        _token = OwnerId.Next();

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

    public void PushFatTail()
    {
        if (_shift == 0 || _root.IsRelaxed())
        {
         PushFullTail();
         return;
        }
        
        
        int a = 0;
        ref int pos = ref a;
        Span<T> tail = _tail.AsSpan();

        var inode = RrbAlgorithm.AsInternal(_root).EnsureEditable(_token);
        if (!PushFatTailRec(inode, tail, _shift, ref pos))
        {
            Node<T>[] newchildren = new Node<T>[2];
            newchildren[0] = _root;
            newchildren[1] = new InternalNode<T>(0, _token);
            _root = new InternalNode<T>(newchildren, null, 2, _token);
            _shift += Constants.RRB_BITS;
            PushFatTailRec(_root, tail, _shift, ref pos);
        }

        Count += _tailCapacity;

    }

    internal bool PushFatTailRec(Node<T> node, Span<T> tail, int shift, ref int pos)
    {
        if (shift == 0)
            throw new IndexOutOfRangeException();
        
        var inode = RrbAlgorithm.AsInternal(node);

        
        // ACTION! We make sure the current node is editable above the current shift. It is already expanded and editable
        // here.
        if (shift == Constants.RRB_BITS)
        {
            // Everything is full to the brim
            if (inode.Len == Constants.RRB_BRANCHING &&
                inode.Children[Constants.RRB_BRANCHING - 1]!.Len == Constants.RRB_BRANCHING)
            {
                return false;
            }
            
            if(inode.Len == 0)
            { // inode is created above. Already editable.
                inode.Children[0] = new InternalNode<T>(0, _token);
                inode.Len++;
            }

            // Check whether the last node is full. If it is we make a new node
            InternalNode<T> currentNode;
            int cur;
            if (inode.Children[inode.Len - 1]!.Len < Constants.RRB_BRANCHING)
            {
                currentNode = RrbAlgorithm.AsInternal(inode.Children[inode.Len - 1]!).EnsureEditable(_token);
                inode.Children[inode.Len - 1] = currentNode;
                cur = currentNode.Len;
            }
            else
            {
                currentNode  = new InternalNode<T>(0, _token);
                inode.Children[inode.Len] = currentNode;
                cur = 0;
                inode.Len++;
            }
            
            // Here currentNode is set tho the last node in the tree with room. 
            // cur is set to the last available position in that node

            while (true)
            {
               
                for (; cur < Constants.RRB_BRANCHING; cur++)
                {
                    currentNode.Children[cur] = new LeafNode<T>(tail.Slice(pos, Constants.RRB_BRANCHING).ToArray(),
                        Constants.RRB_BRANCHING, _token);
                    currentNode.Len++;
                    pos += Constants.RRB_BRANCHING;
                }

                if (pos >= _tailCapacity)
                    return true;
                
                if (inode.Len == Constants.RRB_BRANCHING)
                    break;

                // here we know the inode is shorter than RRB_BRANCHING
                currentNode = new InternalNode<T>(0, _token);
                inode.Children[inode.Len] = currentNode;
                inode.Len++;
                cur = 0;
            }
            return pos < _tailCapacity;
        }

        if (inode.Len == 0)
        {
            inode.Children[0] = new InternalNode<T>(0, _token);
            inode.Len++;
        }
        
        // In the tree!
        InternalNode<T> next = RrbAlgorithm.AsInternal(inode.Children[inode.Len - 1]!);
        next = next.EnsureEditable(_token);
        while (!PushFatTailRec(next, tail, shift - Constants.RRB_BITS, ref pos))
        {
            // here we need to check whether we have room for a rightward expansion
            if (inode.Len == Constants.RRB_BRANCHING &&
                inode.Children[node.Len - 1]!.Len == Constants.RRB_BRANCHING)
                return false;
            inode.Children[inode.Len] = new InternalNode<T>(0, _token);
            inode.Len++;
            next = RrbAlgorithm.AsInternal(inode.Children[inode.Len - 1]!);
        }

        return true;



    }
}