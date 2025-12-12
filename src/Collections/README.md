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

```

BenchmarkDotNet v0.15.6, Linux openSUSE Tumbleweed-Slowroll
AMD Ryzen 9 7900 3.02GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                         | N      | Mean            | Error          | StdDev        | Rank | Gen0    | Gen1    | Gen2   | Allocated |
|--------------------------------|------- |----------------:|---------------:|--------------:|-----:|--------:|--------:|-------:|----------:|
| RrbList.Add                    | 100    |      20.8773 ns |      3.0291 ns |     0.1660 ns |    3 |  0.0129 |       - |      - |     216 B |
| RrbListUnbalanced.Add          | 100    |      21.0371 ns |      5.2589 ns |     0.2883 ns |    3 |  0.0124 |       - |      - |     208 B |
| RrbBuilder.Add                 | 100    |       5.2861 ns |      2.5797 ns |     0.1414 ns |    2 |  0.0004 |  0.0004 |      - |       6 B |
| ImmutableList.Add              | 100    |      49.6456 ns |      5.8750 ns |     0.3220 ns |    4 |  0.0215 |       - |      - |     360 B |
| List.Add                       | 100    |       0.8645 ns |      0.0688 ns |     0.0038 ns |    1 |       - |       - |      - |         - |
| RrbList.Add                    | 10000  |      22.3613 ns |      1.9116 ns |     0.1048 ns |    3 |  0.0186 |       - |      - |     312 B |
| RrbListUnbalanced.Add          | 10000  |      24.8455 ns |      1.0210 ns |     0.0560 ns |    3 |  0.0181 |       - |      - |     304 B |
| RrbBuilder.Add                 | 10000  |       5.1776 ns |      1.7968 ns |     0.0985 ns |    2 |  0.0004 |  0.0004 |      - |       6 B |
| ImmutableList.Add              | 10000  |     103.2771 ns |     13.5309 ns |     0.7417 ns |    5 |  0.0416 |       - |      - |     696 B |
| List.Add                       | 10000  |       0.8533 ns |      0.0189 ns |     0.0010 ns |    1 |       - |       - |      - |         - |
| RrbList.Add                    | 100000 |      20.0090 ns |      1.3580 ns |     0.0744 ns |    3 |  0.0110 |       - |      - |     184 B |
| RrbListUnbalanced.Add          | 100000 |      20.1300 ns |      2.3571 ns |     0.1292 ns |    3 |  0.0110 |       - |      - |     184 B |
| RrbBuilder.Add                 | 100000 |       4.9867 ns |      0.4296 ns |     0.0235 ns |    2 |  0.0004 |  0.0004 |      - |       6 B |
| ImmutableList.Add              | 100000 |     128.2558 ns |      6.1147 ns |     0.3352 ns |    6 |  0.0501 |       - |      - |     840 B |
| List.Add                       | 100000 |       0.8655 ns |      0.1477 ns |     0.0081 ns |    1 |       - |       - |      - |         - |
| INDEXING                       |        |                 |                |               |      |         |         |        |           |
| &#39;RrbList[i]&#39;           | 100    |       3.3763 ns |      0.3386 ns |     0.0186 ns |    2 |       - |       - |      - |         - |
| &#39;RrbListUnbalanced[i]&#39; | 100    |       6.6502 ns |      0.3552 ns |     0.0195 ns |    3 |       - |       - |      - |         - |
| &#39;ImmutableList[i]&#39;     | 100    |       6.0984 ns |      0.0807 ns |     0.0044 ns |    3 |       - |       - |      - |         - |
| &#39;List[i]&#39;              | 100    |       0.5297 ns |      0.1066 ns |     0.0058 ns |    1 |       - |       - |      - |         - |
| &#39;RrbList[i]&#39;           | 10000  |       5.1548 ns |      0.2591 ns |     0.0142 ns |    3 |       - |       - |      - |         - |
| &#39;RrbListUnbalanced[i]&#39; | 10000  |       8.7793 ns |      0.3960 ns |     0.0217 ns |    4 |       - |       - |      - |         - |
| &#39;ImmutableList[i]&#39;     | 10000  |      13.0575 ns |      0.5833 ns |     0.0320 ns |    5 |       - |       - |      - |         - |
| &#39;List[i]&#39;              | 10000  |       0.5147 ns |      0.1299 ns |     0.0071 ns |    1 |       - |       - |      - |         - |
| &#39;RrbList[i]&#39;           | 100000 |       8.7757 ns |      0.6378 ns |     0.0350 ns |    4 |       - |       - |      - |         - |
| &#39;RrbListUnbalanced[i]&#39; | 100000 |      19.1461 ns |      0.8570 ns |     0.0470 ns |    6 |       - |       - |      - |         - |
| &#39;ImmutableList[i]&#39;     | 100000 |      16.4992 ns |      0.8412 ns |     0.0461 ns |    6 |       - |       - |      - |         - |
| &#39;List[i]&#39;              | 100000 |       0.5135 ns |      0.0659 ns |     0.0036 ns |    1 |       - |       - |      - |         - |
| INSERTION                      |        |                 |                |               |      |         |         |        |           |
| RrbList.Insert                 | 100    |      55.6649 ns |      4.1836 ns |     0.2293 ns |    2 |  0.0368 |       - |      - |     616 B |
| RrbListUnbalanced.Insert       | 100    |      38.1325 ns |      4.5723 ns |     0.2506 ns |    1 |  0.0225 |       - |      - |     376 B |
| ImmutableList.Insert           | 100    |      54.3543 ns |      1.7339 ns |     0.0950 ns |    2 |  0.0215 |       - |      - |     360 B |
| List.Insert                    | 100    |  28,741.2230 ns | 80,391.5650 ns | 4,406.5330 ns |    6 |       - |       - |      - |         - |
| RrbList.Insert                 | 10000  |     149.4920 ns |      5.6913 ns |     0.3120 ns |    4 |  0.0772 |       - |      - |    1296 B |
| RrbListUnbalanced.Insert       | 10000  |      81.9220 ns |      4.4557 ns |     0.2442 ns |    3 |  0.0559 |       - |      - |     936 B |
| ImmutableList.Insert           | 10000  |     104.3892 ns |     14.2476 ns |     0.7810 ns |    4 |  0.0416 |       - |      - |     696 B |
| List.Insert                    | 10000  |  28,712.0783 ns | 80,253.7490 ns | 4,398.9788 ns |    6 |       - |       - |      - |         - |
| RrbList.Insert                 | 100000 |     249.0454 ns |     36.3651 ns |     1.9933 ns |    5 |  0.1082 |       - |      - |    1816 B |
| RrbListUnbalanced.Insert       | 100000 |     111.0756 ns |      5.7860 ns |     0.3171 ns |    4 |  0.0802 |  0.0002 |      - |    1344 B |
| ImmutableList.Insert           | 100000 |     126.6094 ns |      5.8932 ns |     0.3230 ns |    4 |  0.0501 |       - |      - |     840 B |
| List.Insert                    | 100000 |  30,528.9306 ns | 75,448.5174 ns | 4,135.5878 ns |    6 |       - |       - |      - |         - |
| ITERATION                      |        |                 |                |               |      |         |         |        |           |
| RrbList.Foreach                | 100    |      42.9767 ns |      0.9497 ns |     0.0521 ns |    2 |  0.0110 |       - |      - |     184 B |
| RrbList.Fold                   | 100    |      30.2994 ns |      0.6332 ns |     0.0347 ns |    1 |       - |       - |      - |         - |
| RrbListUnbalanced.Foreach      | 100    |      42.1677 ns |      3.1336 ns |     0.1718 ns |    2 |  0.0110 |       - |      - |     184 B |
| ImmutableList.Foreach          | 100    |     357.6232 ns |      4.2211 ns |     0.2314 ns |    3 |       - |       - |      - |         - |
| List.Foreach                   | 100    |      29.7331 ns |      4.5676 ns |     0.2504 ns |    1 |       - |       - |      - |         - |
| RrbList.Foreach                | 10000  |   7,777.5934 ns |  1,689.6155 ns |    92.6135 ns |    6 |       - |       - |      - |     184 B |
| RrbList.Fold                   | 10000  |   2,386.8257 ns |    812.8188 ns |    44.5533 ns |    4 |       - |       - |      - |         - |
| RrbListUnbalanced.Foreach      | 10000  |   7,903.6939 ns |    254.4942 ns |    13.9497 ns |    6 |       - |       - |      - |     184 B |
| ImmutableList.Foreach          | 10000  |  38,747.9564 ns |  7,272.4103 ns |   398.6254 ns |    8 |       - |       - |      - |         - |
| List.Foreach                   | 10000  |   2,963.2499 ns |     88.6812 ns |     4.8609 ns |    5 |       - |       - |      - |         - |
| RrbList.Foreach                | 100000 |  76,714.5789 ns |  8,360.1734 ns |   458.2493 ns |    9 |       - |       - |      - |     184 B |
| RrbList.Fold                   | 100000 |  29,285.1984 ns |    509.3808 ns |    27.9209 ns |    7 |       - |       - |      - |         - |
| RrbListUnbalanced.Foreach      | 100000 |  78,869.6132 ns |  2,881.0004 ns |   157.9174 ns |    9 |       - |       - |      - |     184 B |
| ImmutableList.Foreach          | 100000 | 539,318.2256 ns |  9,041.4092 ns |   495.5901 ns |   10 |       - |       - |      - |         - |
| List.Foreach                   | 100000 |  29,938.3745 ns |  5,106.9372 ns |   279.9285 ns |    7 |       - |       - |      - |         - |
| MERGE                          |        |                 |                |               |      |         |         |        |           |
| RrbList.Merge                  | 100    |     761.2164 ns |    150.8209 ns |     8.2670 ns |    3 |  0.5131 |  0.0105 |      - |    8584 B |
| RrbListUnbalanced.Merge        | 100    |     770.8139 ns |    161.2283 ns |     8.8375 ns |    3 |  0.5150 |  0.0124 |      - |    8624 B |
| ImmutableList.AddRange         | 100    |     280.3818 ns |      6.6126 ns |     0.3625 ns |    1 |  0.0415 |       - |      - |     696 B |
| List.AddRange                  | 100    |              NA |             NA |            NA |    ? |      NA |      NA |     NA |        NA |
| RrbList.Merge                  | 10000  |   2,244.8289 ns |    635.4286 ns |    34.8300 ns |    5 |  1.0948 |  0.0496 |      - |   18368 B |
| RrbListUnbalanced.Merge        | 10000  |   2,348.5464 ns |    479.0286 ns |    26.2572 ns |    5 |  1.1902 |  0.0496 |      - |   19928 B |
| ImmutableList.AddRange         | 10000  |     403.6397 ns |     26.3271 ns |     1.4431 ns |    2 |  0.0644 |       - |      - |    1080 B |
| List.AddRange                  | 10000  |              NA |             NA |            NA |    ? |      NA |      NA |     NA |        NA |
| RrbList.Merge                  | 100000 |   1,846.5999 ns |    308.6939 ns |    16.9206 ns |    4 |  0.9327 |  0.0343 |      - |   15632 B |
| RrbListUnbalanced.Merge        | 100000 |   1,865.5073 ns |    391.1176 ns |    21.4385 ns |    4 |  0.9975 |  0.0381 |      - |   16696 B |
| ImmutableList.AddRange         | 100000 |     420.0270 ns |     19.3301 ns |     1.0596 ns |    2 |  0.0701 |       - |      - |    1176 B |
| List.AddRange                  | 100000 |              NA |             NA |            NA |    ? |      NA |      NA |     NA |        NA |
| REMOVEAT                       |        |                 |                |               |      |         |         |        |           |
| RrbList.RemoveAt               | 100    |      37.2778 ns |      4.7184 ns |     0.2586 ns |    2 |  0.0225 |       - |      - |     376 B |
| RrbListUnbalanced.RemoveAt     | 100    |      37.4144 ns |      1.0913 ns |     0.0598 ns |    2 |  0.0220 |       - |      - |     368 B |
| ImmutableList.RemoveAt         | 100    |      50.1468 ns |      3.3032 ns |     0.1811 ns |    3 |  0.0186 |       - |      - |     312 B |
| List.RemoveAt                  | 100    |       9.3606 ns |      0.4024 ns |     0.0221 ns |    1 |       - |       - |      - |         - |
| RrbList.RemoveAt               | 10000  |      78.6869 ns |      6.7632 ns |     0.3707 ns |    4 |  0.0559 |       - |      - |     936 B |
| RrbListUnbalanced.RemoveAt     | 10000  |      84.4466 ns |     18.0636 ns |     0.9901 ns |    4 |  0.0554 |       - |      - |     928 B |
| ImmutableList.RemoveAt         | 10000  |     109.0606 ns |     43.1413 ns |     2.3647 ns |    5 |  0.0386 |       - |      - |     648 B |
| List.RemoveAt                  | 10000  |     135.4603 ns |      2.9796 ns |     0.1633 ns |    6 |       - |       - |      - |         - |
| RrbList.RemoveAt               | 100000 |     112.2070 ns |      3.0688 ns |     0.1682 ns |    5 |  0.0802 |  0.0002 |      - |    1344 B |
| RrbListUnbalanced.RemoveAt     | 100000 |     105.8480 ns |      9.9871 ns |     0.5474 ns |    5 |  0.0798 |  0.0002 |      - |    1336 B |
| ImmutableList.RemoveAt         | 100000 |     145.1719 ns |     19.6618 ns |     1.0777 ns |    6 |  0.0472 |       - |      - |     792 B |
| List.RemoveAt                  | 100000 |   1,516.4727 ns |     40.5737 ns |     2.2240 ns |    7 |       - |       - |      - |         - |
| SETITEM                        |        |                 |                |               |      |         |         |        |           |
| RrbList.SetItem                | 100    |      29.3385 ns |      5.5445 ns |     0.3039 ns |    5 |  0.0200 |       - |      - |     336 B |
| RrbBuilder.SetItem             | 100    |       6.9917 ns |      0.3386 ns |     0.0186 ns |    2 |       - |       - |      - |         - |
| RrbListUnbalanced.SetItem      | 100    |      28.9992 ns |      1.1419 ns |     0.0626 ns |    5 |  0.0200 |       - |      - |     336 B |
| ImmutableList.SetItem          | 100    |      11.0461 ns |      2.2683 ns |     0.1243 ns |    3 |  0.0043 |       - |      - |      72 B |
| &#39;List[i] = x&#39;          | 100    |       0.3994 ns |      0.0960 ns |     0.0053 ns |    1 |       - |       - |      - |         - |
| RrbList.SetItem                | 10000  |      49.7590 ns |      3.6062 ns |     0.1977 ns |    6 |  0.0430 |  0.0001 |      - |     720 B |
| RrbBuilder.SetItem             | 10000  |      11.9896 ns |      0.8585 ns |     0.0471 ns |    3 |       - |       - |      - |         - |
| RrbListUnbalanced.SetItem      | 10000  |      50.7703 ns |     12.8995 ns |     0.7071 ns |    6 |  0.0430 |  0.0001 |      - |     720 B |
| ImmutableList.SetItem          | 10000  |      11.0051 ns |      3.3754 ns |     0.1850 ns |    3 |  0.0043 |       - |      - |      72 B |
| &#39;List[i] = x&#39;          | 10000  |       0.3538 ns |      0.0565 ns |     0.0031 ns |    1 |       - |       - |      - |         - |
| RrbList.SetItem                | 100000 |      68.7256 ns |      1.8857 ns |     0.1034 ns |    7 |  0.0597 |       - |      - |    1000 B |
| RrbBuilder.SetItem             | 100000 |      16.5795 ns |      0.5039 ns |     0.0276 ns |    4 |       - |       - |      - |         - |
| RrbListUnbalanced.SetItem      | 100000 |      68.7790 ns |      2.0288 ns |     0.1112 ns |    7 |  0.0597 |       - |      - |    1000 B |
| ImmutableList.SetItem          | 100000 |      11.4811 ns |      2.7991 ns |     0.1534 ns |    3 |  0.0043 |       - |      - |      72 B |
| &#39;List[i] = x&#39;          | 100000 |       0.3541 ns |      0.1114 ns |     0.0061 ns |    1 |       - |       - |      - |         - |
| SLICING                        |        |                 |                |               |      |         |         |        |           |
| RrbList.Slice                  | 100    |      53.8754 ns |      2.0287 ns |     0.1112 ns |    2 |  0.0291 |       - |      - |     488 B |
| RrbListUnbalanced.Slice        | 100    |      60.4404 ns |      5.6016 ns |     0.3070 ns |    2 |  0.0310 |       - |      - |     520 B |
| ImmutableList.GetRange         | 100    |     215.4070 ns |     15.4836 ns |     0.8487 ns |    6 |  0.0730 |       - |      - |    1224 B |
| List.GetRange                  | 100    |       9.6326 ns |      2.2846 ns |     0.1252 ns |    1 |  0.0096 |       - |      - |     160 B |
| RrbList.Slice                  | 10000  |     103.8645 ns |     11.9988 ns |     0.6577 ns |    3 |  0.0707 |  0.0001 |      - |    1184 B |
| RrbListUnbalanced.Slice        | 10000  |     112.0552 ns |     16.6685 ns |     0.9137 ns |    3 |  0.0583 |       - |      - |     976 B |
| ImmutableList.GetRange         | 10000  |  31,928.9612 ns |  5,151.5664 ns |   282.3747 ns |    8 |  7.1411 |  1.3428 |      - |  120024 B |
| List.GetRange                  | 10000  |     240.7226 ns |     39.5415 ns |     2.1674 ns |    6 |  0.6013 |  0.0215 |      - |   10056 B |
| RrbList.Slice                  | 100000 |     136.3709 ns |      9.8910 ns |     0.5422 ns |    4 |  0.0932 |       - |      - |    1560 B |
| RrbListUnbalanced.Slice        | 100000 |     167.9345 ns |      8.1150 ns |     0.4448 ns |    5 |  0.1004 |  0.0002 |      - |    1680 B |
| ImmutableList.GetRange         | 100000 | 875,558.1751 ns | 54,004.5409 ns | 2,960.1711 ns |    9 | 71.2891 | 36.1328 |      - | 1200024 B |
| List.GetRange                  | 100000 |   9,735.5468 ns |    862.2519 ns |    47.2629 ns |    7 |  9.1400 |  9.1400 | 9.1400 |  100107 B |
