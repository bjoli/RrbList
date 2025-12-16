using System.Buffers;
using System.Runtime.CompilerServices;

namespace Collections;

public static class misc
{
    
    // This should return a pretty unbalanced RrbList.
    public static RrbList<int> MakeUnbalanced(int length)
    {
        var list = new RrbList<int>(Enumerable.Range(0, length));

        for (int i = 0; i < list.Count; i += Constants.RRB_BRANCHING)
        {
            list = list.RemoveAt(i);
        }

        return list;
    }
}

// internal readonly struct ArrayPoolScope<TItem> : IDisposable
//
// {
//
//     private readonly TItem[] _array;
//
//
//     public ArrayPoolScope(int minLength)
//     {
//         _array = ArrayPool<TItem>.Shared.Rent(minLength);
//     }
//
//
//     public void Dispose()
//     { 
//         bool needsClear = RuntimeHelpers.IsReferenceOrContainsReferences<TItem>();
//         ArrayPool<TItem>.Shared.Return(_array, clearArray: needsClear);
//     }
//
// // Allow implicit conversion to Span for easy usage
//
//     public static implicit operator Span<TItem>(ArrayPoolScope<TItem> scope) => scope.Span;
//
// } 