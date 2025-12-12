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


| Method                     | N      | Mean            | Error          | StdDev        | Rank | Gen0    | Gen1    | Gen2   | Allocated |
|--------------------------- |------- |----------------:|---------------:|--------------:|-----:|--------:|--------:|-------:|----------:|
| 'RrbList[i]'               | 100    |       3.0711 ns |      0.1008 ns |     0.0055 ns |    4 |       - |       - |      - |         - |
| 'RrbListUnbalanced[i]'     | 100    |       6.0543 ns |      1.0788 ns |     0.0591 ns |    5 |       - |       - |      - |         - |
| 'ImmutableList[i]'         | 100    |       6.2089 ns |      0.6991 ns |     0.0383 ns |    5 |       - |       - |      - |         - |
| 'List[i]'                  | 100    |       0.5341 ns |      0.1654 ns |     0.0091 ns |    2 |       - |       - |      - |         - |
| RrbList.SetItem            | 100    |      29.1683 ns |     10.2110 ns |     0.5597 ns |    8 |  0.0200 |       - |      - |     336 B |
| RrbListUnbalanced.SetItem  | 100    |      29.0792 ns |      4.1172 ns |     0.2257 ns |    8 |  0.0200 |       - |      - |     336 B |
| ImmutableList.SetItem      | 100    |      10.7149 ns |      2.7379 ns |     0.1501 ns |    6 |  0.0043 |       - |      - |      72 B |
| 'List[i] = x'              | 100    |       0.3848 ns |      0.0096 ns |     0.0005 ns |    1 |       - |       - |      - |         - |
| RrbList.Insert             | 100    |      55.9786 ns |     15.6202 ns |     0.8562 ns |    8 |  0.0368 |       - |      - |     616 B |
| RrbListUnbalanced.Insert   | 100    |      36.8849 ns |      4.3929 ns |     0.2408 ns |    8 |  0.0225 |       - |      - |     376 B |
| ImmutableList.Insert       | 100    |      55.9699 ns |      2.2151 ns |     0.1214 ns |    8 |  0.0215 |       - |      - |     360 B |
| List.Insert                | 100    |  28,251.2978 ns | 74,793.1471 ns | 4,099.6648 ns |   17 |       - |       - |      - |         - |
| RrbList.RemoveAt           | 100    |      35.5954 ns |      2.3967 ns |     0.1314 ns |    8 |  0.0225 |       - |      - |     376 B |
| RrbListUnbalanced.RemoveAt | 100    |      37.3697 ns |      4.7421 ns |     0.2599 ns |    8 |  0.0220 |       - |      - |     368 B |
| ImmutableList.RemoveAt     | 100    |      52.8213 ns |      1.7374 ns |     0.0952 ns |    8 |  0.0186 |       - |      - |     312 B |
| List.RemoveAt              | 100    |       8.9396 ns |      1.5359 ns |     0.0842 ns |    6 |       - |       - |      - |         - |
| RrbList.Foreach            | 100    |      50.0175 ns |      5.6705 ns |     0.3108 ns |    8 |  0.0110 |       - |      - |     184 B |
| RrbListUnbalanced.Foreach  | 100    |      41.8253 ns |      0.3797 ns |     0.0208 ns |    8 |  0.0110 |       - |      - |     184 B |
| ImmutableList.Foreach      | 100    |     357.2402 ns |     36.6576 ns |     2.0093 ns |   10 |       - |       - |      - |         - |
| List.Foreach               | 100    |      29.7490 ns |      3.1023 ns |     0.1700 ns |    8 |       - |       - |      - |         - |
| RrbList.Add                | 100    |      20.6416 ns |      3.6142 ns |     0.1981 ns |    7 |  0.0129 |       - |      - |     216 B |
| RrbListUnbalanced.Add      | 100    |      22.6778 ns |      7.5324 ns |     0.4129 ns |    7 |  0.0124 |       - |      - |     208 B |
| ImmutableList.Add          | 100    |      51.5233 ns |      4.4370 ns |     0.2432 ns |    8 |  0.0215 |       - |      - |     360 B |
| List.Add                   | 100    |       0.8933 ns |      0.1686 ns |     0.0092 ns |    3 |       - |       - |      - |         - |
| RrbList.Slice              | 100    |      54.7310 ns |      6.8238 ns |     0.3740 ns |    8 |  0.0291 |       - |      - |     488 B |
| RrbListUnbalanced.Slice    | 100    |      58.8102 ns |      5.4788 ns |     0.3003 ns |    8 |  0.0310 |       - |      - |     520 B |
| ImmutableList.GetRange     | 100    |     219.4612 ns |     35.7665 ns |     1.9605 ns |    9 |  0.0730 |       - |      - |    1224 B |
| List.GetRange              | 100    |       9.2335 ns |      0.9409 ns |     0.0516 ns |    6 |  0.0096 |       - |      - |     160 B |
| RrbList.Merge              | 100    |     740.6986 ns |    156.3846 ns |     8.5720 ns |   11 |  0.5150 |  0.0124 |      - |    8624 B |
| RrbListUnbalanced.Merge    | 100    |     730.3386 ns |    156.3633 ns |     8.5708 ns |   11 |  0.5178 |  0.0105 |      - |    8664 B |
| ImmutableList.AddRange     | 100    |     270.0101 ns |      5.2168 ns |     0.2860 ns |    9 |  0.0415 |       - |      - |     696 B |
| List.AddRange              | 100    |              NA |             NA |            NA |    ? |      NA |      NA |     NA |        NA |
| 'RrbList[i]'               | 10000  |       5.2458 ns |      0.2815 ns |     0.0154 ns |    5 |       - |       - |      - |         - |
| 'RrbListUnbalanced[i]'     | 10000  |      13.1895 ns |      0.7228 ns |     0.0396 ns |    6 |       - |       - |      - |         - |
| 'ImmutableList[i]'         | 10000  |      12.9890 ns |      1.1371 ns |     0.0623 ns |    6 |       - |       - |      - |         - |
| 'List[i]'                  | 10000  |       0.5456 ns |      0.0935 ns |     0.0051 ns |    2 |       - |       - |      - |         - |
| RrbList.SetItem            | 10000  |      49.8681 ns |      3.2680 ns |     0.1791 ns |    8 |  0.0430 |  0.0001 |      - |     720 B |
| RrbListUnbalanced.SetItem  | 10000  |      49.6464 ns |      1.1063 ns |     0.0606 ns |    8 |  0.0430 |  0.0001 |      - |     720 B |
| ImmutableList.SetItem      | 10000  |      10.4876 ns |      2.1070 ns |     0.1155 ns |    6 |  0.0043 |       - |      - |      72 B |
| 'List[i] = x'              | 10000  |       0.6320 ns |      0.1022 ns |     0.0056 ns |    2 |       - |       - |      - |         - |
| RrbList.Insert             | 10000  |     151.1126 ns |     22.3145 ns |     1.2231 ns |    8 |  0.0772 |       - |      - |    1296 B |
| RrbListUnbalanced.Insert   | 10000  |      82.0130 ns |      2.5370 ns |     0.1391 ns |    8 |  0.0559 |       - |      - |     936 B |
| ImmutableList.Insert       | 10000  |     107.0244 ns |      4.4871 ns |     0.2460 ns |    8 |  0.0416 |       - |      - |     696 B |
| List.Insert                | 10000  |  28,377.1661 ns | 78,856.7526 ns | 4,322.4047 ns |   17 |       - |       - |      - |         - |
| RrbList.RemoveAt           | 10000  |      83.3101 ns |      4.5873 ns |     0.2514 ns |    8 |  0.0559 |       - |      - |     936 B |
| RrbListUnbalanced.RemoveAt | 10000  |      80.5667 ns |      8.4426 ns |     0.4628 ns |    8 |  0.0554 |       - |      - |     928 B |
| ImmutableList.RemoveAt     | 10000  |     112.2392 ns |     11.7952 ns |     0.6465 ns |    8 |  0.0386 |       - |      - |     648 B |
| List.RemoveAt              | 10000  |     138.6794 ns |      1.4904 ns |     0.0817 ns |    8 |       - |       - |      - |         - |
| RrbList.Foreach            | 10000  |   7,720.1309 ns |    221.3495 ns |    12.1329 ns |   15 |       - |       - |      - |     184 B |
| RrbListUnbalanced.Foreach  | 10000  |   7,844.4867 ns |    259.9544 ns |    14.2490 ns |   15 |       - |       - |      - |     184 B |
| ImmutableList.Foreach      | 10000  |  38,221.1421 ns |    484.2274 ns |    26.5421 ns |   17 |       - |       - |      - |         - |
| List.Foreach               | 10000  |   2,947.3471 ns |    133.8940 ns |     7.3392 ns |   14 |       - |       - |      - |         - |
| RrbList.Add                | 10000  |      22.6413 ns |      4.8547 ns |     0.2661 ns |    7 |  0.0186 |       - |      - |     312 B |
| RrbListUnbalanced.Add      | 10000  |      22.2163 ns |      4.5332 ns |     0.2485 ns |    7 |  0.0181 |       - |      - |     304 B |
| ImmutableList.Add          | 10000  |      98.5254 ns |     12.8163 ns |     0.7025 ns |    8 |  0.0416 |       - |      - |     696 B |
| List.Add                   | 10000  |       0.8450 ns |      0.0267 ns |     0.0015 ns |    3 |       - |       - |      - |         - |
| RrbList.Slice              | 10000  |     101.6234 ns |      7.2916 ns |     0.3997 ns |    8 |  0.0707 |  0.0001 |      - |    1184 B |
| RrbListUnbalanced.Slice    | 10000  |     110.2510 ns |      5.8102 ns |     0.3185 ns |    8 |  0.0583 |       - |      - |     976 B |
| ImmutableList.GetRange     | 10000  |  32,428.6298 ns |  3,517.3322 ns |   192.7968 ns |   17 |  7.1411 |  1.3428 |      - |  120024 B |
| List.GetRange              | 10000  |     236.1319 ns |     62.7185 ns |     3.4378 ns |    9 |  0.6013 |  0.0215 |      - |   10056 B |
| RrbList.Merge              | 10000  |   2,295.1712 ns |    163.6108 ns |     8.9681 ns |   13 |  1.1940 |  0.0534 |      - |   19976 B |
| RrbListUnbalanced.Merge    | 10000  |   2,257.4149 ns |     62.9399 ns |     3.4499 ns |   13 |  1.2054 |  0.0534 |      - |   20168 B |
| ImmutableList.AddRange     | 10000  |     395.1787 ns |     24.5507 ns |     1.3457 ns |   10 |  0.0644 |       - |      - |    1080 B |
| List.AddRange              | 10000  |              NA |             NA |            NA |    ? |      NA |      NA |     NA |        NA |
| 'RrbList[i]'               | 100000 |       8.7316 ns |      0.2068 ns |     0.0113 ns |    6 |       - |       - |      - |         - |
| 'RrbListUnbalanced[i]'     | 100000 |      19.0098 ns |      0.7080 ns |     0.0388 ns |    7 |       - |       - |      - |         - |
| 'ImmutableList[i]'         | 100000 |      16.1930 ns |      0.1000 ns |     0.0055 ns |    7 |       - |       - |      - |         - |
| 'List[i]'                  | 100000 |       0.5269 ns |      0.0876 ns |     0.0048 ns |    2 |       - |       - |      - |         - |
| RrbList.SetItem            | 100000 |      68.7546 ns |      9.2912 ns |     0.5093 ns |    8 |  0.0597 |       - |      - |    1000 B |
| RrbListUnbalanced.SetItem  | 100000 |      68.7342 ns |      4.1330 ns |     0.2265 ns |    8 |  0.0597 |       - |      - |    1000 B |
| ImmutableList.SetItem      | 100000 |      11.1575 ns |      3.0878 ns |     0.1693 ns |    6 |  0.0043 |       - |      - |      72 B |
| 'List[i] = x'              | 100000 |       0.3633 ns |      0.1107 ns |     0.0061 ns |    1 |       - |       - |      - |         - |
| RrbList.Insert             | 100000 |     260.9199 ns |     45.4824 ns |     2.4930 ns |    9 |  0.1082 |       - |      - |    1816 B |
| RrbListUnbalanced.Insert   | 100000 |     110.9687 ns |      1.0200 ns |     0.0559 ns |    8 |  0.0802 |  0.0002 |      - |    1344 B |
| ImmutableList.Insert       | 100000 |     126.6421 ns |     19.7914 ns |     1.0848 ns |    8 |  0.0501 |       - |      - |     840 B |
| List.Insert                | 100000 |  29,861.4345 ns | 79,041.1066 ns | 4,332.5098 ns |   17 |       - |       - |      - |         - |
| RrbList.RemoveAt           | 100000 |     120.6002 ns |     10.0911 ns |     0.5531 ns |    8 |  0.0801 |  0.0002 |      - |    1344 B |
| RrbListUnbalanced.RemoveAt | 100000 |     107.2599 ns |     14.3445 ns |     0.7863 ns |    8 |  0.0798 |  0.0002 |      - |    1336 B |
| ImmutableList.RemoveAt     | 100000 |     144.6544 ns |      4.7654 ns |     0.2612 ns |    8 |  0.0472 |       - |      - |     792 B |
| List.RemoveAt              | 100000 |   1,511.3125 ns |     58.0628 ns |     3.1826 ns |   12 |       - |       - |      - |         - |
| RrbList.Foreach            | 100000 |  75,533.2036 ns |    508.4163 ns |    27.8680 ns |   18 |       - |       - |      - |     184 B |
| RrbListUnbalanced.Foreach  | 100000 |  78,652.9741 ns |    743.2623 ns |    40.7407 ns |   18 |       - |       - |      - |     184 B |
| ImmutableList.Foreach      | 100000 | 505,544.5391 ns | 22,850.9131 ns | 1,252.5357 ns |   19 |       - |       - |      - |         - |
| List.Foreach               | 100000 |  29,263.9758 ns |    648.1974 ns |    35.5299 ns |   17 |       - |       - |      - |         - |
| RrbList.Add                | 100000 |      19.3659 ns |      1.9774 ns |     0.1084 ns |    7 |  0.0110 |       - |      - |     184 B |
| RrbListUnbalanced.Add      | 100000 |      19.6092 ns |      5.1128 ns |     0.2803 ns |    7 |  0.0110 |       - |      - |     184 B |
| ImmutableList.Add          | 100000 |     124.6175 ns |      3.8940 ns |     0.2134 ns |    8 |  0.0501 |       - |      - |     840 B |
| List.Add                   | 100000 |       0.8467 ns |      0.0691 ns |     0.0038 ns |    3 |       - |       - |      - |         - |
| RrbList.Slice              | 100000 |     136.9973 ns |      3.3593 ns |     0.1841 ns |    8 |  0.0932 |       - |      - |    1560 B |
| RrbListUnbalanced.Slice    | 100000 |     168.1744 ns |     58.3988 ns |     3.2010 ns |    8 |  0.1004 |  0.0002 |      - |    1680 B |
| ImmutableList.GetRange     | 100000 | 857,432.9437 ns |  5,958.8592 ns |   326.6252 ns |   20 | 71.2891 | 36.1328 |      - | 1200024 B |
| List.GetRange              | 100000 |   9,833.4684 ns |  1,308.4357 ns |    71.7198 ns |   16 |  9.0485 |  9.0485 | 9.0485 |  100107 B |
| RrbList.Merge              | 100000 |   1,925.1274 ns |    359.0381 ns |    19.6801 ns |   13 |  1.0071 |  0.0381 |      - |   16904 B |
| RrbListUnbalanced.Merge    | 100000 |   1,816.0412 ns |    153.3655 ns |     8.4065 ns |   13 |  1.0090 |  0.0381 |      - |   16904 B |
| ImmutableList.AddRange     | 100000 |     413.1513 ns |     11.0593 ns |     0.6062 ns |   10 |  0.0701 |       - |      - |    1176 B |
| List.AddRange              | 100000 |              NA |             NA |            NA |    ? |      NA |      NA |     NA |        NA |
