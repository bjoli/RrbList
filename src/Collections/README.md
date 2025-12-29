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

Following are some benchmarks comparing List, ImmutableList and RrbList (balanced and unbalanced). Addrange for List<T> removed because it is too slow. This benchmarks Lookups, removals, iteration, slicing and merging. 

To make sense of this: Nothing beats List<T> for adding an item to the end. RrbBuilder comes closest, but is still about 6x slower. 

Indexing is also faster with List<int>. The dense RrbList comes closest, but is still much slower. Currently I have a performance regression here, where some of the optimizations are making the code slower. The numbers for a dense list should be closer to 2.6, 3.5, and 5.5. I am trying to figure this out.

Inserting is a different beast: the unbalanced RrbList is the fastest. Don't look too close at the dense RrbList. One Insert will turn a dense list into an unbalanced list. The unbalanced list used in the benchmark is also _VERY_ unbalanced, meaning there is very little overhead when doing an insertion with regards to creating new lookup tables. 

Iteration is a weird one as well. RrbList is a lot faster than ImmutableList, but a bit more than 3x slower than List<T>. Using the higher order function Fold, we remove the iterator overhead and end up slightly beating List<T> - despite the new deabstraction stuff in .net 10. Why? I believe List<T>, to ensure being correct, checks the lists version every time so that it does not try to iterate over a list that has been changed by another thread. using for(var i=0; i < mylist.Count; i++) {} will certainly be faster. 

Merge: the tree (AVL?) in ImmutableList shows where it is king! I messed up the list benchmark, but list is slow.

RemoveAt: List<T> has a strong start but fails miserably after N=500. RrbList wins again. 

SetItem: List<T> wins. If you need to do a lot of SetItem, convert your RrbTree to a Builder and make sure to do a ToImmutable() and you are fine! The current benchmark is pretty bad for the Builder since it is updating random indices and it starts by doing lots of copying. In the real world this is likely a lot faster. 

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
| Merge_List                 | 10000           | 1            | 100    |   1,182.8542 ns |   14418 B |
| Merge_List                 | 10000           | 1            | 10000  |     809.0630 ns |    8184 B |
| Merge_List                 | 10000           | 1            | 100000 |   1,090.7471 ns |   10160 B |
|                            |                 |              |        |                 |           |
| RrbList.Add                | Default         | 16           | 100    |      16.1995 ns |     136 B |
| RrbListUnbalanced.Add      | Default         | 16           | 100    |      15.8833 ns |     128 B |
| RrbBuilder.Add             | Default         | 16           | 100    |       5.1326 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 100    |      51.0882 ns |     360 B |
| List.Add                   | Default         | 16           | 100    |       0.8748 ns |         - |
| RrbList.Add                | Default         | 16           | 10000  |      17.1974 ns |     184 B |
| RrbListUnbalanced.Add      | Default         | 16           | 10000  |      17.4597 ns |     176 B |
| RrbBuilder.Add             | Default         | 16           | 10000  |       5.5684 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 10000  |      99.1471 ns |     696 B |
| List.Add                   | Default         | 16           | 10000  |       0.8655 ns |         - |
| RrbList.Add                | Default         | 16           | 100000 |      15.4483 ns |     120 B |
| RrbListUnbalanced.Add      | Default         | 16           | 100000 |      16.3114 ns |     120 B |
| RrbBuilder.Add             | Default         | 16           | 100000 |       5.2880 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 100000 |     127.8313 ns |     840 B |
| List.Add                   | Default         | 16           | 100000 |       0.8616 ns |         - |
|                            |                 |              |        |                 |           |
| &#39;RrbList[i]&#39;               | Default         | 16           | 100    |       3.0570 ns |         - |
| &#39;RrbListUnbalanced[i]&#39;     | Default         | 16           | 100    |       5.9842 ns |         - |
| &#39;ImmutableList[i]&#39;         | Default         | 16           | 100    |       6.1528 ns |         - |
| &#39;List[i]&#39;                  | Default         | 16           | 100    |       0.5193 ns |         - |
| &#39;RrbList[i]&#39;               | Default         | 16           | 10000  |       7.4451 ns |         - |
| &#39;RrbListUnbalanced[i]&#39;     | Default         | 16           | 10000  |      10.2834 ns |         - |
| &#39;ImmutableList[i]&#39;         | Default         | 16           | 10000  |      12.9971 ns |         - |
| &#39;List[i]&#39;                  | Default         | 16           | 10000  |       0.5137 ns |         - |
| &#39;RrbList[i]&#39;               | Default         | 16           | 100000 |      15.8828 ns |         - |
| &#39;RrbListUnbalanced[i]&#39;     | Default         | 16           | 100000 |      22.6037 ns |         - |
| &#39;ImmutableList[i]&#39;         | Default         | 16           | 100000 |      16.3123 ns |         - |
| &#39;List[i]&#39;                  | Default         | 16           | 100000 |       0.5200 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Insert             | Default         | 16           | 100    |      55.4892 ns |     616 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 100    |      37.9352 ns |     376 B |
| ImmutableList.Insert       | Default         | 16           | 100    |      54.1717 ns |     360 B |
| List.Insert                | Default         | 16           | 100    |  29,213.7741 ns |         - |
| RrbList.Insert             | Default         | 16           | 10000  |     154.9207 ns |    1296 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 10000  |      88.4387 ns |     936 B |
| ImmutableList.Insert       | Default         | 16           | 10000  |     105.0094 ns |     696 B |
| List.Insert                | Default         | 16           | 10000  |  29,330.2317 ns |         - |
| RrbList.Insert             | Default         | 16           | 100000 |     281.4931 ns |    1816 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 100000 |     114.1616 ns |    1344 B |
| ImmutableList.Insert       | Default         | 16           | 100000 |     129.0683 ns |     840 B |
| List.Insert                | Default         | 16           | 100000 |  30,939.2367 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Foreach            | Default         | 16           | 100    |      43.3697 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 100    |      27.4774 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 100    |      42.4336 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 100    |     361.9875 ns |         - |
| List.Foreach               | Default         | 16           | 100    |      30.4741 ns |         - |
| RrbList.Foreach            | Default         | 16           | 10000  |   7,665.9175 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 10000  |   2,557.7334 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 10000  |   7,925.0362 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 10000  |  39,648.4692 ns |         - |
| List.Foreach               | Default         | 16           | 10000  |   2,957.6831 ns |         - |
| RrbList.Foreach            | Default         | 16           | 100000 |  75,993.2233 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 100000 |  25,804.5319 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 100000 |  79,131.4468 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 100000 | 511,009.9098 ns |         - |
| List.Foreach               | Default         | 16           | 100000 |  29,519.2055 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Merge              | Default         | 16           | 100    |     384.1996 ns |    2008 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100    |     377.4185 ns |    2048 B |
| ImmutableList.AddRange     | Default         | 16           | 100    |     277.5808 ns |     696 B |
| RrbList.Merge              | Default         | 16           | 10000  |     418.0334 ns |    2376 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 10000  |     411.7132 ns |    2376 B |
| ImmutableList.AddRange     | Default         | 16           | 10000  |     401.5184 ns |    1080 B |
| RrbList.Merge              | Default         | 16           | 100000 |     565.9292 ns |    3384 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100000 |     611.4363 ns |    3640 B |
| ImmutableList.AddRange     | Default         | 16           | 100000 |     423.3641 ns |    1176 B |
|                            |                 |              |        |                 |           |
| RrbList.RemoveAt           | Default         | 16           | 100    |      34.9887 ns |     376 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100    |      37.3748 ns |     368 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100    |      50.1907 ns |     312 B |
| List.RemoveAt              | Default         | 16           | 100    |       8.4806 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 10000  |      80.9877 ns |     936 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 10000  |      80.8150 ns |     928 B |
| ImmutableList.RemoveAt     | Default         | 16           | 10000  |     111.1281 ns |     648 B |
| List.RemoveAt              | Default         | 16           | 10000  |     134.0254 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 100000 |     108.5789 ns |    1344 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100000 |     111.6326 ns |    1336 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100000 |     151.9118 ns |     792 B |
| List.RemoveAt              | Default         | 16           | 100000 |   1,533.3857 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.SetItem            | Default         | 16           | 100    |     617.4252 ns |    6720 B |
| RrbBuilder.SetItem         | Default         | 16           | 100    |     148.8264 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 100    |     639.2177 ns |    6720 B |
| ImmutableList.SetItem      | Default         | 16           | 100    |     862.5177 ns |    5472 B |
| &#39;List[i] = x&#39;              | Default         | 16           | 100    |       9.7521 ns |         - |
| RrbList.SetItem            | Default         | 16           | 10000  |   1,248.8954 ns |   14400 B |
| RrbBuilder.SetItem         | Default         | 16           | 10000  |     276.8025 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 10000  |   1,271.5874 ns |   14400 B |
| ImmutableList.SetItem      | Default         | 16           | 10000  |   2,594.8615 ns |   12960 B |
| &#39;List[i] = x&#39;              | Default         | 16           | 10000  |       9.7624 ns |         - |
| RrbList.SetItem            | Default         | 16           | 100000 |   1,766.6367 ns |   20000 B |
| RrbBuilder.SetItem         | Default         | 16           | 100000 |     399.3678 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 100000 |   1,813.1959 ns |   20000 B |
| ImmutableList.SetItem      | Default         | 16           | 100000 |   3,285.8284 ns |   15504 B |
| &#39;List[i] = x&#39;              | Default         | 16           | 100000 |       9.6382 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Slice              | Default         | 16           | 100    |      54.9605 ns |     488 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 100    |      60.3968 ns |     520 B |
| ImmutableList.GetRange     | Default         | 16           | 100    |     208.9326 ns |    1224 B |
| List.GetRange              | Default         | 16           | 100    |       9.4254 ns |     160 B |
| RrbList.Slice              | Default         | 16           | 10000  |     113.1438 ns |    1232 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 10000  |     111.1560 ns |     976 B |
| ImmutableList.GetRange     | Default         | 16           | 10000  |  31,596.8255 ns |  120024 B |
| List.GetRange              | Default         | 16           | 10000  |     238.4689 ns |   10056 B |
| RrbList.Slice              | Default         | 16           | 100000 |     157.8932 ns |    1688 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 100000 |     167.1771 ns |    1680 B |
| ImmutableList.GetRange     | Default         | 16           | 100000 | 922,213.7533 ns | 1200024 B |
| List.GetRange              | Default         | 16           | 100000 |   9,917.8972 ns |  100107 B |
