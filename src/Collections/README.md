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
* Fix Split so that it is fast.

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
tail. Scalas finger trees offers some benefits over RRB trees, the largest one being increadibly fast slicing. Something like 4x the speed of this RRB tree implementation. The complexity however is astounding.

# Potential speedups

I do think there are some potential speedups that I see as someone who has never written anything serious in C# before, but I believe I am done with most of the larger optimizations. There might be some tricks with changing recursion to iteration in some places, and some minor tricks. I am looking at minimizing allocations in some algorithms, but it is not always as simple as using ArrayPool since that might actually slow things down compared to using stackalloc.

There might be places where loop unrolling would work, but that is also not really a definite win. In indexing for example, the loop is at most 7 levels deep. The JIT does wonders with the loop we have now. 

Then there is using unsafe code when compiled with RELEASE. This I already do with casting (trusting shift > 0 to always mean internal nodes), but that is also not always a win because it leaves the JIT with less information. 

# Benchmarks

Following are some benchmarks comparing List, ImmutableList and RrbList (balanced and unbalanced).

```

BenchmarkDotNet v0.15.6, Linux openSUSE Tumbleweed-Slowroll
AMD Ryzen 9 7900 3.02GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```

### Add to the end

List will always beat everyone at adding a single element to the end of the sequence. This benchmark builds a list of N elements using Add, which makes things look different. At N = 10000 RrbBuilder is close second, but beating List at N = 100000.

| Method                     | Job        | InvocationCount | UnrollFactor | N      | Mean               | Allocated  |
|--------------------------- |----------- |---------------- |------------- |------- |-------------------:|-----------:|
| RrbList.Add                | DefaultJob | Default         | 16           | 100    |      1,169.9679 ns |    14128 B |
| RrbBuilder.Add             | DefaultJob | Default         | 16           | 100    |        215.3139 ns |     5472 B |
| ImmutableList.Add          | DefaultJob | Default         | 16           | 100    |      5,694.3567 ns |    34704 B |
| List.Add                   | DefaultJob | Default         | 16           | 100    |        120.7431 ns |     1184 B |
| RrbList.Add                | DefaultJob | Default         | 16           | 10000  |    133,867.2184 ns |  1508160 B |
| RrbBuilder.Add             | DefaultJob | Default         | 16           | 10000  |      9,068.9866 ns |    68184 B |
| ImmutableList.Add          | DefaultJob | Default         | 16           | 10000  |  1,179,852.3205 ns |  6653616 B |
| List.Add                   | DefaultJob | Default         | 16           | 10000  |      7,787.4929 ns |   131400 B |
| RrbList.Add                | DefaultJob | Default         | 16           | 100000 |  1,621,085.8071 ns | 15773616 B |
| RrbBuilder.Add             | DefaultJob | Default         | 16           | 100000 |     84,081.5073 ns |   663144 B |
| ImmutableList.Add          | DefaultJob | Default         | 16           | 100000 | 22,806,083.6500 ns | 82508648 B |
| List.Add                   | DefaultJob | Default         | 16           | 100000 |    108,542.9375 ns |  1049413 B |

### Indexing

This benchmark indexes into the data at three different points. List is of course by far the fastest. RrbList is slightly faster than immutableList for small lists, but much faster for larger ones.  

| Method                     | Job        | InvocationCount | UnrollFactor | N      | Mean               | Allocated  |
|--------------------------- |----------- |---------------- |------------- |------- |-------------------:|-----------:|
| 'RrbList[i]'               | DefaultJob | Default         | 16           | 100    |          2.7946 ns |          - |
| 'RrbListUnbalanced[i]'     | DefaultJob | Default         | 16           | 100    |          4.8442 ns |          - |
| 'ImmutableList[i]'         | DefaultJob | Default         | 16           | 100    |          6.1162 ns |          - |
| 'List[i]'                  | DefaultJob | Default         | 16           | 100    |          0.5200 ns |          - |
| 'RrbList[i]'               | DefaultJob | Default         | 16           | 10000  |          3.8996 ns |          - |
| 'RrbListUnbalanced[i]'     | DefaultJob | Default         | 16           | 10000  |          9.6498 ns |          - |
| 'ImmutableList[i]'         | DefaultJob | Default         | 16           | 10000  |         12.9889 ns |          - |
| 'List[i]'                  | DefaultJob | Default         | 16           | 10000  |          1.4091 ns |          - |
| 'RrbList[i]'               | DefaultJob | Default         | 16           | 100000 |          4.7132 ns |          - |
| 'RrbListUnbalanced[i]'     | DefaultJob | Default         | 16           | 100000 |         13.6766 ns |          - |
| 'ImmutableList[i]'         | DefaultJob | Default         | 16           | 100000 |         16.3973 ns |          - |
| 'List[i]'                  | DefaultJob | Default         | 16           | 100000 |          0.5084 ns |          - |

### Insert

Here the unbalanced RrbList wins big. Something is going on with the small List, and it should probably be the fastest
at N=100 but the slowest from something like N=500. Don't look too much at the dense list. It will degrade into an
unbalanced list on the first insert, which will make later inserts faster.


| Method                     | Job        | InvocationCount | UnrollFactor | N      | Mean               | Allocated  |
|--------------------------- |----------- |---------------- |------------- |------- |-------------------:|-----------:|
| RrbList.Insert             | DefaultJob | Default         | 16           | 100    |         55.9802 ns |      616 B |
| RrbListUnbalanced.Insert   | DefaultJob | Default         | 16           | 100    |         38.6932 ns |      376 B |
| ImmutableList.Insert       | DefaultJob | Default         | 16           | 100    |         55.6263 ns |      360 B |
| List.Insert                | DefaultJob | Default         | 16           | 100    |          9.0131 ns |          - |
| RrbList.Insert             | DefaultJob | Default         | 16           | 10000  |        154.3614 ns |     1296 B |
| RrbListUnbalanced.Insert   | DefaultJob | Default         | 16           | 10000  |         83.4055 ns |      936 B |
| ImmutableList.Insert       | DefaultJob | Default         | 16           | 10000  |        106.1314 ns |      696 B |
| List.Insert                | DefaultJob | Default         | 16           | 10000  |        130.8397 ns |          - |
| RrbList.Insert             | DefaultJob | Default         | 16           | 100000 |        244.3082 ns |     1816 B |
| RrbListUnbalanced.Insert   | DefaultJob | Default         | 16           | 100000 |        112.7059 ns |     1344 B |
| ImmutableList.Insert       | DefaultJob | Default         | 16           | 100000 |        131.2398 ns |      840 B |
| List.Insert                | DefaultJob | Default         | 16           | 100000 |      1,526.9674 ns |          - |

### Iteration

The absolute fastest way to iterate over a List is using for(var i=0; i < mylist.Count; i++) {...}. The enumerator has
some safety checks baked in to make it thread safe. External iteration on a list is the fastest in all cases.
ImmutableList is slow. Internal iteration of the tree (RrbList.Fold) is sliiightly faster than enumerating a List.

| Method                     | Job        | InvocationCount | UnrollFactor | N      | Mean               | Allocated  |
|--------------------------- |----------- |---------------- |------------- |------- |-------------------:|-----------:|
| RrbList.Foreach            | DefaultJob | Default         | 16           | 100    |         45.1823 ns |      184 B |
| RrbList.Fold               | DefaultJob | Default         | 16           | 100    |         26.8854 ns |          - |
| RrbListUnbalanced.Foreach  | DefaultJob | Default         | 16           | 100    |         42.0788 ns |      184 B |
| ImmutableList.Foreach      | DefaultJob | Default         | 16           | 100    |        356.2626 ns |          - |
| List.Foreach               | DefaultJob | Default         | 16           | 100    |         30.2207 ns |          - |
| RrbList.Foreach            | DefaultJob | Default         | 16           | 10000  |      7,744.6192 ns |      184 B |
| RrbList.Fold               | DefaultJob | Default         | 16           | 10000  |      2,668.9276 ns |          - |
| RrbListUnbalanced.Foreach  | DefaultJob | Default         | 16           | 10000  |      7,937.8595 ns |      184 B |
| ImmutableList.Foreach      | DefaultJob | Default         | 16           | 10000  |     38,498.2915 ns |          - |
| List.Foreach               | DefaultJob | Default         | 16           | 10000  |      2,970.4183 ns |          - |
| RrbList.Foreach            | DefaultJob | Default         | 16           | 100000 |     76,180.7899 ns |      184 B |
| RrbList.Fold               | DefaultJob | Default         | 16           | 100000 |     25,707.0896 ns |          - |
| RrbListUnbalanced.Foreach  | DefaultJob | Default         | 16           | 100000 |     79,558.4544 ns |      184 B |
| ImmutableList.Foreach      | DefaultJob | Default         | 16           | 100000 |    516,728.2509 ns |          - |
| List.Foreach               | DefaultJob | Default         | 16           | 100000 |     29,484.9584 ns |          - |

### Merge
List, being mutable, became _very_ large during this test, and ended up getting more than 2 billion items. Trust me when I say it would 
be slow anyway.

| Method                     | InvocationCount | UnrollFactor | N      | Mean               | Gen0      | Gen1     | Gen2     | Allocated  |
|--------------------------- |---------------- |------------- |------- |-------------------:|----------:|---------:|---------:|-----------:|
| RrbList.Merge              | Default         | 16           | 100    |        189.7629 ns |    0.1013 |   0.0002 |        - |     1696 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100    |        189.0178 ns |    0.1037 |        - |        - |     1736 B |
| ImmutableList.AddRange     | Default         | 16           | 100    |        282.0089 ns |    0.0415 |        - |        - |      696 B |
| RrbList.Merge              | Default         | 16           | 10000  |        335.2976 ns |    0.1798 |   0.0005 |        - |     3008 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 10000  |        331.1668 ns |    0.1912 |   0.0005 |        - |     3200 B |
| ImmutableList.AddRange     | Default         | 16           | 10000  |        410.4530 ns |    0.0644 |        - |        - |     1080 B |
| RrbList.Merge              | Default         | 16           | 100000 |        323.2425 ns |    0.1988 |   0.0010 |        - |     3328 B |
| RrbListUnbalanced.Merge    | Default         | 16           | 100000 |        362.6298 ns |    0.2275 |   0.0010 |        - |     3808 B |
| ImmutableList.AddRange     | Default         | 16           | 100000 |        425.4831 ns |    0.0701 |        - |        - |     1176 B |


### RemoveAt

Removes an item in the list. RrbList wins.

| Method                     | InvocationCount | UnrollFactor | N      |          Mean | Allocated |
|----------------------------|-----------------|--------------|--------|--------------:|----------:|
| RrbList.RemoveAt           | Default         | 16           | 100    |    34.9887 ns |     376 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100    |    37.3748 ns |     368 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100    |    50.1907 ns |     312 B |
| List.RemoveAt              | Default         | 16           | 100    |     8.4806 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 10000  |    80.9877 ns |     936 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 10000  |    80.8150 ns |     928 B |
| ImmutableList.RemoveAt     | Default         | 16           | 10000  |   111.1281 ns |     648 B |
| List.RemoveAt              | Default         | 16           | 10000  |   134.0254 ns |         - |
| RrbList.RemoveAt           | Default         | 16           | 100000 |   108.5789 ns |    1344 B |
| RrbListUnbalanced.RemoveAt | Default         | 16           | 100000 |   111.6326 ns |    1336 B |
| ImmutableList.RemoveAt     | Default         | 16           | 100000 |   151.9118 ns |     792 B |
| List.RemoveAt              | Default         | 16           | 100000 | 1,533.3857 ns |         - |

### SetItem

This sets 50 elements in the list. Unsurprisingly, List wins. I suspect a more realistic load would make RrbBuilder
faster, but it would still be something like 5x slower than List.

| Method                    | InvocationCount | UnrollFactor | N      |          Mean | Allocated |
|---------------------------|-----------------|--------------|--------|--------------:|----------:|
| RrbList.SetItem           | Default         | 16           | 100    |   617.4252 ns |    6720 B |
| RrbBuilder.SetItem        | Default         | 16           | 100    |   148.8264 ns |         - |
| RrbListUnbalanced.SetItem | Default         | 16           | 100    |   639.2177 ns |    6720 B |
| ImmutableList.SetItem     | Default         | 16           | 100    |   862.5177 ns |    5472 B |
| &#39;List[i] = x&#39;     | Default         | 16           | 100    |     9.7521 ns |         - |
| RrbList.SetItem           | Default         | 16           | 10000  | 1,248.8954 ns |   14400 B |
| RrbBuilder.SetItem        | Default         | 16           | 10000  |   276.8025 ns |         - |
| RrbListUnbalanced.SetItem | Default         | 16           | 10000  | 1,271.5874 ns |   14400 B |
| ImmutableList.SetItem     | Default         | 16           | 10000  | 2,594.8615 ns |   12960 B |
| &#39;List[i] = x&#39;     | Default         | 16           | 10000  |     9.7624 ns |         - |
| RrbList.SetItem           | Default         | 16           | 100000 | 1,766.6367 ns |   20000 B |
| RrbBuilder.SetItem        | Default         | 16           | 100000 |   399.3678 ns |         - |
| RrbListUnbalanced.SetItem | Default         | 16           | 100000 | 1,813.1959 ns |   20000 B |
| ImmutableList.SetItem     | Default         | 16           | 100000 | 3,285.8284 ns |   15504 B |
| &#39;List[i] = x&#39;     | Default         | 16           | 100000 |     9.6382 ns |         - |

### Slice

This takes a slice of the datastructure (between 25% and 50% of the list).

| Method                     | Job        | InvocationCount | UnrollFactor | N      | Mean               | Allocated  |
|--------------------------- |----------- |---------------- |------------- |------- |-------------------:|-----------:|
| RrbList.Slice              | DefaultJob | Default         | 16           | 100    |         20.6199 ns |      176 B |
| RrbListUnbalanced.Slice    | DefaultJob | Default         | 16           | 100    |         21.1942 ns |      176 B |
| ImmutableList.GetRange     | DefaultJob | Default         | 16           | 100    |        222.2364 ns |     1224 B |
| List.GetRange              | DefaultJob | Default         | 16           | 100    |          9.6182 ns |      160 B |
| RrbList.Slice              | DefaultJob | Default         | 16           | 10000  |         93.1176 ns |     1024 B |
| RrbListUnbalanced.Slice    | DefaultJob | Default         | 16           | 10000  |         95.8241 ns |      760 B |
| ImmutableList.GetRange     | DefaultJob | Default         | 16           | 10000  |     31,093.4738 ns |   120024 B |
| List.GetRange              | DefaultJob | Default         | 16           | 10000  |        243.9510 ns |    10056 B |
| RrbList.Slice              | DefaultJob | Default         | 16           | 100000 |        130.8667 ns |     1424 B |
| RrbListUnbalanced.Slice    | DefaultJob | Default         | 16           | 100000 |        163.3413 ns |     1512 B |
| ImmutableList.GetRange     | DefaultJob | Default         | 16           | 100000 |  1,001,365.5427 ns |  1200024 B |
| List.GetRange              | DefaultJob | Default         | 16           | 100000 |      9,884.0617 ns |   100107 B |
