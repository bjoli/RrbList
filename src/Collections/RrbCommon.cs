/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Linus Björnstam
 *
 * Portions of this code are based on a port of c-rrb (https://github.com/hypirion/c-rrb),
 * Copyright (c) 2013-2014 Jean Niklas L'orange, licensed under the MIT License.
 */

// While I don't personally consider tweaking these constants as changing the code enough to trigger
// any clause in the MPL to make you have to reshare, this should probably be solved somehow.

namespace Collections;

internal static class Constants
{
    public const int RRB_BITS = 5;
    public const int RRB_BRANCHING = 1 << RRB_BITS; // 32 for RRB_BITS = 5
    public const int RRB_MASK = RRB_BRANCHING - 1;

    // RRB_INVARIANT. Any value higher than one means we allow nodes with fewer than 32
    // values when concatenating. This can lead to faster concatenation, but a loss of 
    // lookup performance and higher memory usage.
    public const int RRB_INVARIANT = 1;

    // We allow 2 incomplete nodes after a merge to prevent situations where we shift the whole tree. 
    // We should try to see what real world usage leads to and tweak this accordingly. Setting this to 0 means you
    // will have a mostly dense tree with fast lookups, at the cost of slower merges.
    public const int RRB_EXTRAS = 2;

    // We are limited to 2 billion elements by .net anyway, so this doesn't limit us at all.
    public const int RRB_MAX_HEIGHT = 10;

    // Constants, to be used, for when to use fat tail in builder. 4096 is conservative.
    public const int WHEN_FAT_TAIL = 4096;
    public const int FAT_TAIL_SIZE = 1024;
}

public readonly struct OwnerId : IEquatable<OwnerId>
{
    private static long _globalCounter = 1; // Start at 1, 0 is reserved for "Immutable/None"

    public readonly uint Id;
    public readonly ushort Gen;

    public OwnerId(uint id, ushort gen)
    {
        Id = id;
        Gen = gen;
    }

    // Generates a new unique 48-bit ID
    public static OwnerId Next()
    {
        var val = Interlocked.Increment(ref _globalCounter);
        return new OwnerId((uint)val, (ushort)(val >> 32));
    }

    // 0 ID implies the node is Immutable/Persistent
    public bool IsNone => Id == 0 && Gen == 0;
    
    public static readonly OwnerId None = new(0, 0);

    // Fast equality check (just two integer comparisons)
    public bool Equals(OwnerId other) => Id == other.Id && Gen == other.Gen;
    public static bool operator ==(OwnerId a, OwnerId b) => a.Equals(b);
    public static bool operator !=(OwnerId a, OwnerId b) => !a.Equals(b);
    public override bool Equals(object? obj) => obj is OwnerId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Gen);
}