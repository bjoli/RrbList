namespace Collections;

/// <summary>
///     The path from the root of a list's tree to one leaf, so that a walk
///     finds the next leaf with a step, not a descent from the root.
/// </summary>
/// <remarks>
///     For a relaxed tree, <see cref="RrbList{T}.LeafAt"/> searches a size
///     table at each relaxed level, for each leaf. The path keeps the node and
///     the child index at each level, so the next leaf is one step along the
///     lowest level that has a next child, and then down the first children.
///     A walk of a vec joined from 1000-element pieces took half the time with
///     it. For a dense tree, LeafAt is as fast, so a walk uses LeafAt there.
///     <para>
///     Only the tree is on the path. The head and tail are found with LeafAt.
///     </para>
/// </remarks>
[NonUserCode]
public sealed class TreePath<T>
{
    /// <summary>The list whose tree the path is in.</summary>
    public readonly RrbList<T> List;

    // The internal node at each level, and the index of the child on the path.
    private readonly Node<T>[] _nodes;
    private readonly int[] _at;

    // The list index after the leaf at the end of the path; -1 before Seek.
    private int _leafEnd = -1;

    public TreePath(RrbList<T> list)
    {
        List = list;
        var levels = list.Shift / Constants.RRB_BITS;
        _nodes = new Node<T>[levels];
        _at = new int[levels];
    }

    /// <summary>
    ///     Sets the path to the leaf of element <paramref name="index"/>, which
    ///     must be in the tree, and answers the leaf as <see cref="RrbList{T}.LeafAt"/>
    ///     does.
    /// </summary>
    public T[] Seek(int index, out int position, out int length)
    {
        var node = List.Root!;
        var i = index - List.HeadLen + List.Gap;
        var shift = List.Shift;
        for (var level = 0; shift > 0; level++, shift -= Constants.RRB_BITS)
        {
            var inner = RrbAlgorithm.AsInternal(node);
            int child;
            if (inner.IsRelaxed())
            {
                (child, i) = RrbAlgorithm.GetRelaxedIndex(inner, i, shift);
            }
            else
            {
                child = (i >> shift) & Constants.RRB_MASK;
                i &= (1 << shift) - 1;
            }

            _nodes[level] = inner;
            _at[level] = child;
            node = inner.Children[child]!;
        }

        var leaf = RrbAlgorithm.AsLeaf(node);
        position = i;
        length = leaf.Len;
        _leafEnd = index - i + length;
        return leaf.Items;
    }

    /// <summary>
    ///     Moves the path to the next leaf and answers it, with
    ///     <paramref name="position"/> 0. Answers null if
    ///     <paramref name="index"/> is not the first element of the next leaf,
    ///     or the next leaf is not in the tree; the path is then not changed.
    /// </summary>
    public T[]? Step(int index, out int position, out int length)
    {
        position = length = 0;
        if (index != _leafEnd || index >= List.Count - List.TailLen) return null;

        var level = _nodes.Length - 1;
        while (level >= 0 && _at[level] + 1 >= _nodes[level].Len) level--;
        if (level < 0) return null;

        var node = RrbAlgorithm.AsInternal(_nodes[level]).Children[++_at[level]]!;
        for (level++; level < _nodes.Length; level++)
        {
            _nodes[level] = node;
            _at[level] = 0;
            node = RrbAlgorithm.AsInternal(node).Children[0]!;
        }

        var leaf = RrbAlgorithm.AsLeaf(node);
        length = leaf.Len;
        _leafEnd += length;
        return leaf.Items;
    }
}
