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
using System.Runtime.InteropServices;

namespace Collections;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public abstract class Node<T>
{
    public uint Owner;
    public ushort Gen;
    public byte Len; // Actual number of elements used
    public NodeFlags Flags; // If null, node is immutable
}
[StructLayout(LayoutKind.Sequential, Size = 32, Pack = 1)]
public sealed class LeafNode<T> : Node<T>
{
    public static readonly LeafNode<T> Empty = new(0,   OwnerId.None);
    public T[] Items;

    public LeafNode(int size, OwnerId owner)
    {
        Len = (byte)size;
        Owner = owner.Id;
        Gen = owner.Gen;
        Flags = NodeFlags.IsLeaf;
        // If we have an owner (Transient), allocate full capacity (32) for cheap appends.
        // If immutable (null), allocate exact fit.
        Items = new T[!owner.IsNone ? Constants.RRB_BRANCHING : size];
    }

    public LeafNode(T[] items, int len, OwnerId owner)
    {
        Items = items;
        Len = (byte)len;
        Owner = owner.Id;
        Gen = owner.Gen;
        Flags = NodeFlags.IsLeaf;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeafNode<T> EnsureEditable(OwnerId target)
    {
        // 1. Check if we already own it (Fast integer compare)
        if (!target.IsNone && Owner == target.Id && Gen == target.Gen)
            return this;

        // 2. Clone logic
        var newCap = !target.IsNone ? Constants.RRB_BRANCHING : Len;
        var newItems = new T[newCap];
        Array.Copy(Items, 0, newItems, 0, Len);

        return new LeafNode<T>(newItems, Len, target);
    }

    public LeafNode<T> Freeze()
    {
        if (Items.Length == Len)
        {
            Owner = 0; // Clear ID
            Gen = 0;
            Flags |= NodeFlags.IsFrozen; // Optional: mark as frozen
            return this;
        }

        var newItems = new T[Len];
        Array.Copy(Items, newItems, Len);
        return new LeafNode<T>(newItems, Len, OwnerId.None);
    }
    
    
    // Fast clone for persistent updates
    // Returns a new node with one item changed
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LeafNode<T> CloneAndSet(int index, T value)
    {
        var newItems = new T[Len];
        Array.Copy(Items, newItems, Len);
        newItems[index] = value;
        // Returns immutable node (Owner 0)
        return new LeafNode<T>(newItems, Len, OwnerId.None); 
    }
}
[StructLayout(LayoutKind.Sequential, Size = 42, Pack = 1)]
public sealed class InternalNode<T> : Node<T>
{
    public readonly Node<T>?[] Children;
    public readonly int[]? SizeTable; 

    public InternalNode(int size, OwnerId owner)
    {
        Len = (byte)size;
        Owner = owner.Id;
        Gen = owner.Gen;
        Children = new Node<T>?[!owner.IsNone ? Constants.RRB_BRANCHING : size];
        // Flags defaulted to None (0) which is correct for Internal
    }

    public InternalNode(Node<T>?[] children, int[]? sizeTable, int len, OwnerId owner)
    {
        Children = children;
        SizeTable = sizeTable;
        Len = (byte)len;
        Owner = owner.Id;
        Gen = owner.Gen;
        
        if (sizeTable != null) Flags |= NodeFlags.IsRelaxed; // Set Relaxed flag
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InternalNode<T> EnsureEditable(OwnerId target)
    {
        if (!target.IsNone && Owner == target.Id && Gen == target.Gen)
            return this;

        var newCap = !target.IsNone ? Constants.RRB_BRANCHING : Len;
        var newChildren = new Node<T>?[newCap];
        Array.Copy(Children, 0, newChildren, 0, Len);

        int[]? newSizeTable = null;
        if (SizeTable != null)
        {
            newSizeTable = new int[newCap];
            Array.Copy(SizeTable, 0, newSizeTable, 0, Len);
        }

        return new InternalNode<T>(newChildren, newSizeTable, Len, target);
    }

    public InternalNode<T> Freeze()
    {
        // Already immutable and packed.
        if (Owner == 0 && Gen == 0 && Children.Length == Len) 
            return this;

        // In-Place Freeze
        // The node fits perfectly, but it currently has an Owner.
        // Strip the owner. 
        if (Children.Length == Len)
        {
            Owner = 0;
            Gen = 0;
            Flags |= NodeFlags.IsFrozen; // If you are using flags
            return this;
        }

        // Shrink to Fit (Allocation required)
        // The arrays are too big (example: size 32 but only holding 5 items).
        // Shrinkylicious
    
        var newChildren = new Node<T>?[Len];
        Array.Copy(Children, newChildren, Len);

        int[]? newTable = null;
        if (SizeTable != null)
        {
            newTable = new int[Len];
            Array.Copy(SizeTable, newTable, Len);
        }

        // Return new immutable node (Owner 0, Gen 0)
        // Note: We preserve the IsRelaxed flag if newTable exists
        return new InternalNode<T>(newChildren, newTable, Len, OwnerId.None);
    }

    // Clone and replace a single child (Path Copying)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public InternalNode<T> CloneAndSetChild(int childIdx, Node<T> newChild)
    {
        var newChildren = new Node<T>?[Len];
        Array.Copy(Children, newChildren, Len);
        newChildren[childIdx] = newChild;

        // We share the SizeTable reference because it hasn't changed
        return new InternalNode<T>(newChildren, SizeTable, Len, OwnerId.None);
    }
}