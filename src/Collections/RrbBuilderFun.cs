using System;
using System.Collections.Generic;

namespace Collections;

public static class RrbBuilderFun
{
    public static RrbBuilder<T> Empty<T>() => new RrbBuilder<T>();
    public static RrbBuilder<T> FromList<T>(RrbList<T> list) => list.ToBuilder();
    
    public static RrbBuilder<T> Add<T>(RrbBuilder<T> builder, T item)
    {
        builder.Add(item);
        return builder;
    }
    
    public static RrbBuilder<T> SetItem<T>(RrbBuilder<T> builder, int index, T item)
    {
        builder.SetItem(index, item);
        return builder;
    }
    
    public static T Get<T>(RrbBuilder<T> builder, int index) => builder[index];
    
    public static int Count<T>(RrbBuilder<T> builder) => builder.Count;
    
    public static RrbList<T> ToImmutable<T>(RrbBuilder<T> builder) => builder.ToImmutable();
}
