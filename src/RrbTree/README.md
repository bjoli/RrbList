# Rrb.PersistentVector - A C# RRB tree implementation

We all know about clojure's persistent vectors. They are old news by this point, not only are they no longer really
exciting, but there are greener pastures. In short: RRB trees are the bee's knees. They are exactly like clojure's
pvectors, but with an extra twist: you are now allowed to do fast splits and merges. That adds the lovely ability of
inserting elements arbitrarily. Those extras come with a cost, but only when you use them. Other than that the tree is exactly like clojures tries.

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
RrbList efficiently supports split, slice, merge, indexing and index based updates. Adding to that, the interfaces IEnumerable and IImmutableList are implemented. 



# Things that have to be made better before a stable release

* Indexing into unbalanced trees should use AVX
* Insert and RemoveAt could do their own zip to not have to rely on a merge. I have an almost-working version of RemoveAt.
* Do we know SetSizes doesn't do too much work?
* Move functionality from the IimmutableList interface to the "main" class and just delegate to it.
* Testing. Right now I have relied a lot on fuzzing. I need better testing of known edge cases

# Things that would be nice to change

* Move the tail into the main class instead of having it as a pointer to a leaf.
* Cleaning up array copies. array.AsSpan().ToArray() is as fast as our index-handling Array.Copy.
* Clean up all the different ways to push a tail. (DONE!)
* If the builder har a fat tail, we would save a lot of pointer chasing by making our own 32-way node and insert that as-is.

# AI disclosure
While the original port was mine and mine only, and I _did_ get things working just fine without AI, I did manage to get something like a 2x speedup using AI help. I have never written much c#. I spent most of my life writing hobby scheme code (and guile scheme is still where my heart is at), but during my paternity leave I did a course called "c# for beginners". After that I found f# and wasn't quite happy with the persistent vectors. 

I had wanted to write RRB trees for scheme ever since I had a beer with Phil Bagwell, but despite trying twice I never really made it work. With c# I was much closer to c-rrb, which is a nice high quality implementation in c so I decided to give it a try.

So what is done by the ai? First of all: all of RrbEnumerator*.cs. Then most of the basic tests. The split function in RrbAlgorithms and RrbList has substantial parts written by AI. PromoteTail in RrbAlgorithm and Normalize in RrbList.cs are also actually completely AI (and currently untested!). All of the code that just utilizes already existing code to implement IImmutableList is also ai. The code I wrote myself there is new functionality stuff, that should probably just be copied verbatim into RrbList.cs

Other than that, I think AI mostly sent me down wrong paths while trying to fix bugs. It especially wasted my time when debugging tail pushing. All in all, it was a net positive though. 

# Why not N=32 finger trees?

Because I value my sanity. I am not a programmer, and lets just call my theoretical rigor "throwing things at the compiler and hope my tests work". I spent more time debugging tail push issues than I will ever admit (concat was nothing in comparison, which is odd because it should be much more complex). A prefix has so many more issues than a tail. 

# Potential speedups

I do think there are some potential speedups that I see as someone who has never written anything serious in C# before. A lot of the casting is done in places where it would make sense to do it using Unsafe.As. 

RemoveAt() and Insert() should probably both rely on some kind of zipping instead of slice/split/merge. I have a removeat implementation that adds considerable complexity, but does not really preform that much better - which it really should. Did I say I don't really know C#?