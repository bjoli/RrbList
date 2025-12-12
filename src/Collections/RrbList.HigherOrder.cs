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



namespace Collections;

public sealed partial class RrbList<T>
{
    
    /**
     * <summary>
     *     Applies an accumulator function over a sequence.
     * </summary>
     * <typeparam name="TState">The type of the accumulator value.</typeparam>
     * <param name="seed">The initial accumulator value.</param>
     * <param name="func">An accumulator function to be invoked on each element with the arguments (state, value)</param>
     * <returns>The final accumulator value.</returns>
     */
    public TState Fold<TState>(TState seed, Func<TState, T, TState> func)
    {
        var state = seed;

        // Fold over the tree part
        if (Root != null) state = FoldNode(Root, Shift, state, func);

        // Fold over the tail part
        if (TailLen > 0)
        {
            var items = Tail.Items;
            for (var i = 0; i < TailLen; i++) state = func(state, items[i]);
        }

        return state;
    }

    private TState FoldNode<TState>(Node<T> node, int shift, TState state, Func<TState, T, TState> func)
    {
        // Base case: We are at a leaf node
        if (shift == 0)
        {
            var leaf = (LeafNode<T>)node;
            var items = leaf.Items;
            var len = leaf.Len;

            // Iterate directly over the array - extremely fast
            for (var i = 0; i < len; i++) state = func(state, items[i]);
            return state;
        }

        // Recursive step: We are at an internal node
        var internalNode = (InternalNode<T>)node;
        var lenInternal = internalNode.Len;

        for (var i = 0; i < lenInternal; i++)
            // Recurse down to children
            state = FoldNode(internalNode.Children[i]!, shift - Constants.RRB_BITS, state, func);

        return state;
    }
}