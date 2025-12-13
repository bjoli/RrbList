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

Merge: the tree (AVL?) in ImmutableList shows where it is king! I messed up the list benchmark, but list is slow. RrbTrees are about 3x slower, except for when N=10000. I suspect there is some tree growing going on for that particular N, or there is a bug or something. 

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
| Merge_List                 | 10000           | 1            | 100    |   1,491.1712 ns |   14418 B |
| Merge_List                 | 10000           | 1            | 10000  |   1,042.3289 ns |    8184 B |
| Merge_List                 | 10000           | 1            | 100000 |   1,095.4181 ns |   10160 B |
|                            |                 |              |        |                 |           |
| RrbList.Add                | Default         | 16           | 100    |      21.1779 ns |     216 B |
| RrbListUnbalanced.Add      | Default         | 16           | 100    |      20.6035 ns |     208 B |
| RrbBuilder.Add             | Default         | 16           | 100    |       5.1905 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 100    |      51.7088 ns |     360 B |
| List.Add                   | Default         | 16           | 100    |       0.8742 ns |         - |
| RrbList.Add                | Default         | 16           | 10000  |      22.6743 ns |     312 B |
| RrbListUnbalanced.Add      | Default         | 16           | 10000  |      22.4126 ns |     304 B |
| RrbBuilder.Add             | Default         | 16           | 10000  |       5.0100 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 10000  |     100.2479 ns |     696 B |
| List.Add                   | Default         | 16           | 10000  |       1.0967 ns |         - |
| RrbList.Add                | Default         | 16           | 100000 |      19.5442 ns |     184 B |
| RrbListUnbalanced.Add      | Default         | 16           | 100000 |      19.5919 ns |     184 B |
| RrbBuilder.Add             | Default         | 16           | 100000 |       5.0455 ns |       6 B |
| ImmutableList.Add          | Default         | 16           | 100000 |     125.5090 ns |     840 B |
| List.Add                   | Default         | 16           | 100000 |       0.8690 ns |         - |
|                            |                 |              |        |                 |           |
| 'RrbList[i]'               | Default         | 16           | 100    |       3.0858 ns |         - |
| 'RrbListUnbalanced[i]'     | Default         | 16           | 100    |       6.0696 ns |         - |
| 'ImmutableList[i]'         | Default         | 16           | 100    |       6.1713 ns |         - |
| 'List[i]'                  | Default         | 16           | 100    |       0.5268 ns |         - |
| 'RrbList[i]'               | Default         | 16           | 10000  |       5.2383 ns |         - |
| 'RrbListUnbalanced[i]'     | Default         | 16           | 10000  |       9.0488 ns |         - |
| 'ImmutableList[i]'         | Default         | 16           | 10000  |      13.1443 ns |         - |
| 'List[i]'                  | Default         | 16           | 10000  |       0.5144 ns |         - |
| 'RrbList[i]'               | Default         | 16           | 100000 |       8.6894 ns |         - |
| 'RrbListUnbalanced[i]'     | Default         | 16           | 100000 |      19.0616 ns |         - |
| 'ImmutableList[i]'         | Default         | 16           | 100000 |      16.1348 ns |         - |
| 'List[i]'                  | Default         | 16           | 100000 |       0.5031 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Insert             | Default         | 16           | 100    |      54.7249 ns |     616 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 100    |      38.6621 ns |     376 B |
| ImmutableList.Insert       | Default         | 16           | 100    |      55.9821 ns |     360 B |
| List.Insert                | Default         | 16           | 100    |  28,247.8769 ns |         - |
| RrbList.Insert             | Default         | 16           | 10000  |     147.8293 ns |    1296 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 10000  |      81.7153 ns |     936 B |
| ImmutableList.Insert       | Default         | 16           | 10000  |     103.3818 ns |     696 B |
| List.Insert                | Default         | 16           | 10000  |  28,819.7762 ns |         - |
| RrbList.Insert             | Default         | 16           | 100000 |     244.8282 ns |    1816 B |
| RrbListUnbalanced.Insert   | Default         | 16           | 100000 |     111.2889 ns |    1344 B |
| ImmutableList.Insert       | Default         | 16           | 100000 |     124.8185 ns |     840 B |
| List.Insert                | Default         | 16           | 100000 |  30,127.2639 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Foreach            | Default         | 16           | 100    |      43.1705 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 100    |      30.1015 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 100    |      47.5692 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 100    |     356.0021 ns |         - |
| List.Foreach               | Default         | 16           | 100    |      29.6789 ns |         - |
| RrbList.Foreach            | Default         | 16           | 10000  |   7,663.4364 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 10000  |   2,476.4009 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 10000  |   7,827.4871 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 10000  |  38,369.5174 ns |         - |
| List.Foreach               | Default         | 16           | 10000  |   2,927.6839 ns |         - |
| RrbList.Foreach            | Default         | 16           | 100000 |  75,298.0843 ns |     184 B |
| RrbList.Fold               | Default         | 16           | 100000 |  29,049.6601 ns |         - |
| RrbListUnbalanced.Foreach  | Default         | 16           | 100000 |  78,462.0114 ns |     184 B |
| ImmutableList.Foreach      | Default         | 16           | 100000 | 510,527.8887 ns |         - |
| List.Foreach               | Default         | 16           | 100000 |  29,490.5313 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Merge              | Default         | 16           | 100    |     745.5818 ns |    8584 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100    |     747.2307 ns |    8624 B |
| ImmutableList.AddRange     | Default         | 16           | 100    |     274.0214 ns |     696 B |
| RrbList.Merge              | Default         | 16           | 10000  |   2,199.6677 ns |   18368 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 10000  |   2,329.8189 ns |   19928 B |
| ImmutableList.AddRange     | Default         | 16           | 10000  |     395.8682 ns |    1080 B |
| RrbList.Merge              | Default         | 16           | 100000 |   1,815.4709 ns |   15632 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100000 |   1,817.7346 ns |   16696 B |
| ImmutableList.AddRange     | Default         | 16           | 100000 |     414.0976 ns |    1176 B |
|                            |                 |              |        |                 |           |
| RrbList.RemoveAt           | Default         | 16           | 100    |      35.4821 ns |     376 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100    |      36.6727 ns |     368 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100    |      49.6739 ns |     312 B |
| List.RemoveAt              | Default         | 16           | 100    |       8.9983 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 10000  |      79.3879 ns |     936 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 10000  |      79.9860 ns |     928 B |
| ImmutableList.RemoveAt     | Default         | 16           | 10000  |     106.5024 ns |     648 B |
| List.RemoveAt              | Default         | 16           | 10000  |     134.8199 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 100000 |     109.3106 ns |    1344 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100000 |     103.5729 ns |    1336 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100000 |     149.4996 ns |     792 B |
| List.RemoveAt              | Default         | 16           | 100000 |   1,501.4633 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.SetItem            | Default         | 16           | 100    |      29.1431 ns |     336 B |
| RrbBuilder.SetItem         | Default         | 16           | 100    |       7.0031 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 100    |      30.5691 ns |     336 B |
| ImmutableList.SetItem      | Default         | 16           | 100    |      11.0566 ns |      72 B |
| 'List[i] = x'              | Default         | 16           | 100    |       0.3931 ns |         - |
| RrbList.SetItem            | Default         | 16           | 10000  |      50.6992 ns |     720 B |
| RrbBuilder.SetItem         | Default         | 16           | 10000  |      11.6400 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 10000  |      50.7436 ns |     720 B |
| ImmutableList.SetItem      | Default         | 16           | 10000  |      10.3386 ns |      72 B |
| 'List[i] = x'              | Default         | 16           | 10000  |       0.3597 ns |         - |
| RrbList.SetItem            | Default         | 16           | 100000 |      71.0247 ns |    1000 B |
| RrbBuilder.SetItem         | Default         | 16           | 100000 |      16.4514 ns |         - |
| RrbListUnbalanced.SetItem  | Default         | 16           | 100000 |      69.3106 ns |    1000 B |
| ImmutableList.SetItem      | Default         | 16           | 100000 |      10.6086 ns |      72 B |
| 'List[i] = x'              | Default         | 16           | 100000 |       0.3733 ns |         - |
|                            |                 |              |        |                 |           |
| RrbList.Slice              | Default         | 16           | 100    |      55.0680 ns |     488 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 100    |      60.3776 ns |     520 B |
| ImmutableList.GetRange     | Default         | 16           | 100    |     212.8562 ns |    1224 B |
| List.GetRange              | Default         | 16           | 100    |       9.3927 ns |     160 B |
| RrbList.Slice              | Default         | 16           | 10000  |     103.4191 ns |    1184 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 10000  |     114.4577 ns |     976 B |
| ImmutableList.GetRange     | Default         | 16           | 10000  |  34,684.2435 ns |  120024 B |
| List.GetRange              | Default         | 16           | 10000  |     247.9265 ns |   10056 B |
| RrbList.Slice              | Default         | 16           | 100000 |     145.8090 ns |    1560 B |
| RrbListUnbalanced.Slice    | Default         | 16           | 100000 |     173.6493 ns |    1680 B |
| ImmutableList.GetRange     | Default         | 16           | 100000 | 873,411.3213 ns | 1200024 B |
| List.GetRange              | Default         | 16           | 100000 |  10,701.3566 ns |  100103 B |

