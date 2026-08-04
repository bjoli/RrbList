using System;
using System.Collections.Generic;

namespace Collections;

/// <summary>
/// Provides static functional-style helpers for <see cref="RrbList{T}"/>,
/// where the first argument is the list.
/// </summary>
public static class RrbFun
{
    // --- Creation ---
    public static RrbList<T> Empty<T>() where T : notnull => RrbList<T>.Empty;

    public static RrbList<T> Create<T>(IEnumerable<T> items) where T : notnull => RrbList<T>.Create(items);

    // --- Element access ---
    public static T Get<T>(RrbList<T> list, int index) where T : notnull => list[index];

    public static RrbList<T> SetItem<T>(RrbList<T> list, int index, T value) where T : notnull => list.SetItem(index, value);

    // --- Addition / Insertion / Removal ---
    public static RrbList<T> Add<T>(RrbList<T> list, T item) where T : notnull => list.Add(item);

    public static RrbList<T> Insert<T>(RrbList<T> list, int index, T item) where T : notnull => list.Insert(index, item);

    public static RrbList<T> RemoveAt<T>(RrbList<T> list, int index) where T : notnull => list.RemoveAt(index);

    public static RrbList<T> Pop<T>(RrbList<T> list) where T : notnull => list.Pop();

    public static RrbList<T> PopFirst<T>(RrbList<T> list) where T : notnull => list.PopFirst();

    // --- Slicing & Merging ---
    public static RrbList<T> Slice<T>(RrbList<T> list, int start, int count) where T : notnull => list.Slice(start, count);

    public static RrbList<T> Merge<T>(RrbList<T> list, RrbList<T> other, bool pure = false) where T : notnull => list.Merge(other, pure);

    public static (RrbList<T> Left, RrbList<T> Right) Split<T>(RrbList<T> list, int index) where T : notnull => list.Split(index);

    // --- Higher-order functions ---
    public static RrbList<TResult> Map<T, TResult>(RrbList<T> list, Func<T, TResult> mapper) where T : notnull where TResult : notnull => list.Map(mapper);

    public static RrbList<T> Filter<T>(RrbList<T> list, Func<T, bool> predicate) where T : notnull => list.Filter(predicate);

    public static TState Fold<T, TState>(RrbList<T> list, TState seed, Func<TState, T, TState> func) where T : notnull => list.Fold(seed, func);

    public static T Reduce<T>(RrbList<T> list, Func<T, T, T> func) where T : notnull => list.Reduce(func);

    public static T? Find<T>(RrbList<T> list, Func<T, bool> predicate) where T : notnull => list.Find(predicate);

    public static void ForEach<T>(RrbList<T> list, Action<T> action, int index = 0, int count = -1) where T : notnull =>
        list.ForEach(action, index, count);

    public static bool Iter<T>(RrbList<T> list, Func<T, bool> action) where T : notnull => list.Iter(action);

    // --- Copying / Counting / Searching ---
    public static int Count<T>(RrbList<T> list) where T : notnull => list.Count;

    public static bool Contains<T>(RrbList<T> list, T item) where T : notnull => list.Contains(item);

    public static void CopyTo<T>(RrbList<T> list, T[] array, int arrayIndex) where T : notnull => list.CopyTo(array, arrayIndex);

    public static void CopyRange<T>(RrbList<T> list, int sourceIndex, T[] destination, int destinationIndex, int count) where T : notnull =>
        list.CopyTo(sourceIndex, destination, destinationIndex, count);

    // --- Utility ---
    public static RrbBuilder<T> ToBuilder<T>(RrbList<T> list, int leafCapacity = Constants.RRB_BRANCHING) where T : notnull =>
        list.ToBuilder(leafCapacity);

    public static RrbList<T> Compact<T>(RrbList<T> list) where T : notnull => list.Compact();

    public static string ToString<T>(RrbList<T> list) where T : notnull => list.ToString();
}
