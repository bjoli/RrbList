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

namespace Collections;

internal abstract class Node<T>
{
    public int Len; // Actual number of elements used
    public OwnerToken? Owner; // If null, node is immutable
}

internal sealed class LeafNode<T> : Node<T>
{
    public static readonly LeafNode<T> Empty = new(0, null);
    public T[] Items;

    public LeafNode(int size, OwnerToken? owner)
    {
        Len = size;
        Owner = owner;
        // If we have an owner (Transient), allocate full capacity (32) for cheap appends.
        // If immutable (null), allocate exact fit.
        Items = new T[owner != null ? Constants.RRB_BRANCHING : size];
    }

    public LeafNode(T[] items, int len, OwnerToken? owner)
    {
        Items = items;
        Len = len;
        Owner = owner;
    }

    // Corresponds to 'ensure_leaf_editable' in rrb_transients.h
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeafNode<T> EnsureEditable(OwnerToken? targetOwner)
    {
        // If targetOwner is null, we are doing a persistent op -> Always clone.
        // If targetOwner matches our owner, we are doing a transient op -> Mutate in place.
        if (targetOwner != null && Owner == targetOwner)
            return this;

        // Otherwise, Clone.
        // If targetOwner is null (Persistent), allocate Exact-Fit.
        // If targetOwner is valid (Transient), allocate Full Capacity.
        var newCap = targetOwner != null ? Constants.RRB_BRANCHING : Len;
        var newItems = new T[newCap];

        // If growing (e.g. persistent node becoming transient), we copy what we have.
        // If shrinking (e.g. transient node becoming persistent), we copy what fits.
        Array.Copy(Items, 0, newItems, 0, Len);

        return new LeafNode<T>(newItems, Len, targetOwner);
    }

    // Corresponds to 'leaf_node_clone' in c-rrb but freezes size
    public LeafNode<T> Freeze()
    {
        if (Items.Length == Len)
        {
            Owner = null;
            return this;
        }

        // Shrink to fit
        var newItems = new T[Len];
        Array.Copy(Items, newItems, Len);
        return new LeafNode<T>(newItems, Len, null);
    }

    // Fast clone for persistent updates
    // Returns a new node with one item changed
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeafNode<T> CloneAndSet(int index, T value)
    {
        var newItems = new T[Len];
        Array.Copy(Items, newItems, Len);
        newItems[index] = value;
        return new LeafNode<T>(newItems, Len, null); // Owner null = Immutable
    }
}

internal sealed class InternalNode<T> : Node<T>
{
    public readonly Node<T>?[] Children;
    public readonly int[]? SizeTable; // Can be null if fully dense

    public InternalNode(int size, OwnerToken? owner)
    {
        Len = size;
        Owner = owner;
        Children = new Node<T>?[owner != null ? Constants.RRB_BRANCHING : size];
    }

    public InternalNode(Node<T>?[] children, int[]? sizeTable, int len, OwnerToken? owner)
    {
        Children = children;
        SizeTable = sizeTable;
        Len = len;
        Owner = owner;
    }

    // 'ensure_internal_editable' in c-rrb in rrb_transients.h
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InternalNode<T> EnsureEditable(OwnerToken? targetOwner)
    {
        if (targetOwner != null && Owner == targetOwner)
            return this;

        var newCap = targetOwner != null ? Constants.RRB_BRANCHING : Len;
        var newChildren = new Node<T>?[newCap];
        Array.Copy(Children, 0, newChildren, 0, Len);

        int[]? newSizeTable = null;
        if (SizeTable != null)
        {
            newSizeTable = new int[newCap];
            Array.Copy(SizeTable, 0, newSizeTable, 0, Len);
        }

        return new InternalNode<T>(newChildren, newSizeTable, Len, targetOwner);
    }

    public InternalNode<T> Freeze()
    {
        // If we are already exact-fit and immutable, do nothing
        if (Owner == null && Children.Length == Len) return this;

        // Shrink Children
        var newChildren = new Node<T>?[Len];
        Array.Copy(Children, newChildren, Len);

        // Shrink SizeTable
        int[]? newTable = null;
        if (SizeTable != null)
        {
            newTable = new int[Len];
            Array.Copy(SizeTable, newTable, Len);
        }

        return new InternalNode<T>(newChildren, newTable, Len, null);
    }

    // Clone and replace a single child (Path Copying)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InternalNode<T> CloneAndSetChild(int childIdx, Node<T> newChild)
    {
        var newChildren = new Node<T>?[Len];
        Array.Copy(Children, newChildren, Len);
        newChildren[childIdx] = newChild;

        // We share the SizeTable reference because it hasn't changed
        return new InternalNode<T>(newChildren, SizeTable, Len, null);
    }
}