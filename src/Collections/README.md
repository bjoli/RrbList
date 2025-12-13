# RrbList - A C# RRB tree implementation

We all know about clojure's persistent vectors. They are old news by this point, not only are they no longer really
exciting, but there are greener pastures. In short: RRB trees are the bee's knees. They are exactly like clojure's
pvectors, but with an extra twist: you are now allowed to do fast splits and merges. That adds the lovely ability of
inserting elements arbitrarily. Those extras come with a cost, but only when you use them. Other than that the tree is
exactly like clojures tries.

Concatenations and splits are O(log N) in RRB-trees, but they will result in a slightly unbalanced tree, where lookups
now rely on a 32-long look up table of how indices are layed out, but only for the paths that have done a slice/merge.

This started as a port of   [C-RRB by Jean Niklas l'Orange](https://github.com/hypirion/c-rrb), but made some different
choices along the way.

For an explanation on how this data structure works in detail, read
http://infoscience.epfl.ch/record/169879/files/RMTrees.pdf and then his thesis:
http://hypirion.com/thesis

# Examples

```csharp

    var list = new RrbList<int>(Enumerable.Range(0, 10000); 
    var list2 = list.RemoveAt(3);
    list.Count == list2.Count // is false
        
    // iterators are supported    
    int sum = 0;
    foreach (int i in list) 
    {
        sum += list;
    }
    
    // Unless we are specifically using a builder (see below)
    // nothing changes the original list
    var list3 = list.merge(list2);
    
    // If we want faster update or appendings, we can use a builder.
    // This sets up a builder with a "fat tail" of 1024 elements, meaning we get faster appends. 
    // Like this it is about 2.5x slower to build than List<int>,
    // which is pretty ok for building a tree.
    
    var buildme = RrbBuilder<int>(1024);
    buildme.Add(11);
    buildme.Add(65);
    
    foreach (int b in Enumerable.Range(0, 10000)) 
    {
        buildme.Add(b);
    }
    
    // in the end we make it persistent:
    var persistent = buildme.ToImmutable();
    
    

```

RrbList efficiently supports split, slice, merge, indexing and index based updates. Adding to that, the interfaces
IEnumerable and IImmutableList are implemented.

# Things that have to be made better before a stable release

* Indexing into unbalanced trees should use AVX (DONE)
* Insert and RemoveAt could do their own zip to not have to rely on a merge. I have an almost-working version of
  RemoveAt. (DONE)
* Do we know SetSizes doesn't do too much work?
* Move functionality from the IimmutableList interface to the "main" class and just delegate to it.
* Testing. Right now I have relied a lot on fuzzing. I need better testing of known edge cases

# Things that would be nice to change

* Move the tail into the main class instead of having it as a pointer to a leaf.
* Cleaning up array copies. array.AsSpan().ToArray() is as fast as our index-handling Array.Copy.
* Clean up all the different ways to push a tail. (DONE!)
* If the builder har a fat tail, we would save a lot of pointer chasing by making our own 32-way node and insert that
  as-is.

# API docs

If someone could provide me with a docfx config that just generates docs from the .cs files in this directory I would be
very grateful. I am doing something stupid. 

# AI disclosure

While the original port was mine and mine only, and I _did_ get things working just fine without AI, I did manage to get
something like a 2x speedup using AI help. I have never written much c#. I spent most of my life writing hobby scheme
code (and guile scheme is still where my heart is at), but during my paternity leave I did a course called "c# for
beginners". After that I found f# and wasn't quite happy with the persistent vectors.

I had wanted to write RRB trees for scheme ever since I had a beer with Phil Bagwell, but despite trying twice I never
really made it work. With c# I was much closer to c-rrb, which is a nice high quality implementation in c so I decided
to give it a try.

So what is done by the ai? First of all: all of RrbEnumerator*.cs. Then most of the basic tests. The split function in
RrbAlgorithms and RrbList has substantial parts written by AI. PromoteTail in RrbAlgorithm and Normalize in RrbList.cs
are also actually completely AI (and currently untested!). All of the code that just utilizes already existing code to
implement IImmutableList is also ai. The code I wrote myself there is new functionality stuff, that should probably just
be copied verbatim into RrbList.cs

Other than that, I think AI mostly sent me down wrong paths while trying to fix bugs. It especially wasted my time when
debugging tail pushing. All in all, it was a net positive though.

# Why not N=32 finger trees?

Because I value my sanity. I am not a programmer, and lets just call my theoretical rigor "throwing things at the
compiler and hope my tests work". I spent more time debugging tail push issues than I will ever admit (concat was
nothing in comparison, which is odd because it should be much more complex). A prefix has so many more issues than a
tail.

# Potential speedups

I do think there are some potential speedups that I see as someone who has never written anything serious in C# before.
A lot of the casting is done in places where it would make sense to do it using Unsafe.As.

# Benchmarks

Following are some benchmarks comparing List, ImmutableList and RrbList (balanced and unbalanced). For some reason Addrange didn't work for lists, but I can't be bothered to re-run it. I didn't even look into it. This benchmarks Lookups, removals, iteration, slicing and merging. 

To make sense of this: Nothing beats List<T> for adding an item to the end. RrbBuilder comes closest, but is still about 6x slower. 

Indexing is also faster with List<int>. The dense RrbList comes closest, but is still much slower. 

Inserting is a different beast: the unbalanced RrbList is the fastest. Don't look too close at the dense RrbList. One Insert will turn a dense list into an unbalanced list. The unbalanced list used in the benchmark is also _VERY_ unbalanced, meaning there is very little overhead when doing an insertion with regards to creating new lookup tables. 

Iteration is a weird one as well. RrbList is a lot faster than ImmutableList, but a bit more than 3x slower than List<T>. Using the higher order function Fold, we remove the iterator overhead and end up slightly beating List<T> - despite the new deabstraction stuff in .net 10. Why? I believe List<T>, to ensure being correct, checks the lists version every time so that it does not try to iterate over a list that has been changed by another thread. using for(var i=0; i < mylist.Count; i++) {} will certainly be faster. 

Merge: the tree (AVL?) in ImmutableList shows where it is king! I messed up the list benchmark, but list is slow.

RemoveAt: List<T> has a strong start but fails miserably after N=500. RrbList wins again. 

SetItem: List<T> wins. ImmutableList is also reall fast. If you need to do a lot of SetItem, convert your RrbTree to a Builder and make sure to do a ToImmutable() and you are fine!.

Slicing: RrbTrees win. List is slow above something like 500 items. ImmutableList is just slow. 
```

BenchmarkDotNet v0.15.6, Linux openSUSE Tumbleweed-Slowroll
AMD Ryzen 9 7900 3.02GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | InvocationCount | UnrollFactor | N      | Mean            | Allocated |
|--------------------------- |---------------- |------------- |------- |----------------:|----------:|
| Merge_List                 | 10000           | 1            | 100    |   1,492.4599 ns |   14418 B |
| Merge_List                 | 10000           | 1            | 10000  |   1,198.9325 ns |    8184 B |
| Merge_List                 | 10000           | 1            | 100000 |   1,059.0360 ns |   10160 B |
|                            |                 |              |        |                 |           |
| RrbList.Add                | Default         | 16           | 100    |      20.6047 ns |     216 B |
| RrbListUnbalanced.Add      | Default         | 16           | 100    |      22.2846 ns |     208 B |
| RrbBuilder.Add             | Default         | 16           | 100    |       5.3121 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 100    |      51.2013 ns |     360 B |
| List.Add                   | Default         | 16           | 100    |       0.8855 ns |         - |
| RrbList.Add                | Default         | 16           | 10000  |      22.8488 ns |     312 B |
| RrbListUnbalanced.Add      | Default         | 16           | 10000  |      23.2565 ns |     304 B |
| RrbBuilder.Add             | Default         | 16           | 10000  |       5.0373 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 10000  |     102.1935 ns |     696 B |
| List.Add                   | Default         | 16           | 10000  |       0.8590 ns |         - |
| RrbList.Add                | Default         | 16           | 100000 |      19.7146 ns |     184 B |
| RrbListUnbalanced.Add      | Default         | 16           | 100000 |      19.9919 ns |     184 B |
| RrbBuilder.Add             | Default         | 16           | 100000 |       5.2620 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 100000 |     128.9416 ns |     840 B |
| List.Add                   | Default         | 16           | 100000 |       0.8702 ns |         - |
|                            |                 |              |        |                 |           |
| &#39;RrbList[i]&#39;               | Default         | 16           | 100    |       3.8762 ns |         - |
| &#39;RrbListUnbalanced[i]&#39;     | Default         | 16           | 100    |       6.0528 ns |         - |
| &#39;ImmutableList[i]&#39;         | Default         | 16           | 100    |       6.1332 ns |         - |
| &#39;List[i]&#39;                  | Default         | 16           | 100    |       0.5119 ns |         - |
| &#39;RrbList[i]&#39;               | Default         | 16           | 10000  |       5.1623 ns |         - |
| &#39;RrbListUnbalanced[i]&#39;     | Default         | 16           | 10000  |       8.9585 ns |         - |
| &#39;ImmutableList[i]&#39;         | Default         | 16           | 10000  |      13.5488 ns |         - |
| &#39;List[i]&#39;                  | Default         | 16           | 10000  |       0.5114 ns |         - |
| &#39;RrbList[i]&#39;               | Default         | 16           | 100000 |       8.7869 ns |         - |
| &#39;RrbListUnbalanced[i]&#39;     | Default         | 16           | 100000 |      18.8647 ns |         - |
| &#39;ImmutableList[i]&#39;         | Default         | 16           | 100000 |      16.0968 ns |         - |
| &#39;List[i]&#39;                  | Default         | 16           | 100000 |       0.5210 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Insert             | Default         | 16           | 100    |      55.1434 ns |     616 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 100    |      38.2359 ns |     376 B |
| ImmutableList.Insert       | Default         | 16           | 100    |      54.6379 ns |     360 B |
| List.Insert                | Default         | 16           | 100    |  29,162.1136 ns |         - |
| RrbList.Insert             | Default         | 16           | 10000  |     158.0689 ns |    1296 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 10000  |      81.3320 ns |     936 B |
| ImmutableList.Insert       | Default         | 16           | 10000  |     105.4634 ns |     696 B |
| List.Insert                | Default         | 16           | 10000  |  28,933.3420 ns |         - |
| RrbList.Insert             | Default         | 16           | 100000 |     250.0480 ns |    1816 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 100000 |     109.6292 ns |    1344 B |
| ImmutableList.Insert       | Default         | 16           | 100000 |     134.3487 ns |     840 B |
| List.Insert                | Default         | 16           | 100000 |  30,554.8893 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Foreach            | Default         | 16           | 100    |      42.8016 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 100    |      30.0626 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 100    |      42.5846 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 100    |     355.3090 ns |         - |
| List.Foreach               | Default         | 16           | 100    |      30.4414 ns |         - |
| RrbList.Foreach            | Default         | 16           | 10000  |   7,626.2492 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 10000  |   2,597.3740 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 10000  |   7,866.8897 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 10000  |  38,554.3800 ns |         - |
| List.Foreach               | Default         | 16           | 10000  |   2,946.5824 ns |         - |
| RrbList.Foreach            | Default         | 16           | 100000 |  75,632.2331 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 100000 |  29,173.5716 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 100000 |  78,490.6065 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 100000 | 513,680.2962 ns |         - |
| List.Foreach               | Default         | 16           | 100000 |  29,541.1172 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Merge              | Default         | 16           | 100    |     370.8560 ns |    2008 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100    |     400.9129 ns |    2048 B |
| ImmutableList.AddRange     | Default         | 16           | 100    |     281.1504 ns |     696 B |
| RrbList.Merge              | Default         | 16           | 10000  |     707.8476 ns |    3488 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 10000  |     705.5440 ns |    3680 B |
| ImmutableList.AddRange     | Default         | 16           | 10000  |     420.5448 ns |    1080 B |
| RrbList.Merge              | Default         | 16           | 100000 |     581.4182 ns |    3344 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100000 |     623.3601 ns |    3640 B |
| ImmutableList.AddRange     | Default         | 16           | 100000 |     436.8163 ns |    1176 B |
|                            |                 |              |        |                 |           |
| RrbList.RemoveAt           | Default         | 16           | 100    |      36.8620 ns |     376 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100    |      37.4200 ns |     368 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100    |      49.5947 ns |     312 B |
| List.RemoveAt              | Default         | 16           | 100    |       9.4238 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 10000  |      79.0332 ns |     936 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 10000  |      79.8166 ns |     928 B |
| ImmutableList.RemoveAt     | Default         | 16           | 10000  |     114.0610 ns |     648 B |
| List.RemoveAt              | Default         | 16           | 10000  |     137.0431 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 100000 |     122.3165 ns |    1344 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100000 |     111.1407 ns |    1336 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100000 |     153.5581 ns |     792 B |
| List.RemoveAt              | Default         | 16           | 100000 |   1,533.0211 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.SetItem            | Default         | 16           | 100    |      29.6696 ns |     336 B |
| RrbBuilder.SetItem         | Default         | 16           | 100    |       7.0802 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 100    |      29.9302 ns |     336 B |
| ImmutableList.SetItem      | Default         | 16           | 100    |      11.5480 ns |      72 B |
| &#39;List[i] = x&#39;              | Default         | 16           | 100    |       0.4189 ns |         - |
| RrbList.SetItem            | Default         | 16           | 10000  |      50.8023 ns |     720 B |
| RrbBuilder.SetItem         | Default         | 16           | 10000  |      11.7413 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 10000  |      52.1026 ns |     720 B |
| ImmutableList.SetItem      | Default         | 16           | 10000  |      11.3641 ns |      72 B |
| &#39;List[i] = x&#39;              | Default         | 16           | 10000  |       0.3558 ns |         - |
| RrbList.SetItem            | Default         | 16           | 100000 |      70.5730 ns |    1000 B |
| RrbBuilder.SetItem         | Default         | 16           | 100000 |      16.7207 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 100000 |      74.0570 ns |    1000 B |
| ImmutableList.SetItem      | Default         | 16           | 100000 |      11.3955 ns |      72 B |
| &#39;List[i] = x&#39;              | Default         | 16           | 100000 |       0.3782 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Slice              | Default         | 16           | 100    |      56.0060 ns |     488 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 100    |      62.4199 ns |     520 B |
| ImmutableList.GetRange     | Default         | 16           | 100    |     229.0720 ns |    1224 B |
| List.GetRange              | Default         | 16           | 100    |       9.5418 ns |     160 B |
| RrbList.Slice              | Default         | 16           | 10000  |     103.9055 ns |    1184 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 10000  |     112.1564 ns |     976 B |
| ImmutableList.GetRange     | Default         | 16           | 10000  |  34,525.8129 ns |  120024 B |
| List.GetRange              | Default         | 16           | 10000  |     254.0227 ns |   10056 B |
| RrbList.Slice              | Default         | 16           | 100000 |     143.1813 ns |    1560 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 100000 |     171.9254 ns |    1680 B |
| ImmutableList.GetRange     | Default         | 16           | 100000 | 955,913.5667 ns | 1200024 B |
| List.GetRange              | Default         | 16           | 100000 |  10,073.1787 ns |  100110 B |
