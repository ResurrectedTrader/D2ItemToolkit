using System;

namespace D2ItemToolkit
{
    // The MSVC CRT qsort the game links (_qsort 0x685b50, calling _shortsort 0x685ac0). It is
    // ported rather than replaced by a library sort because its permutation of EQUAL elements is
    // observable: SORT_ItemDescPriority 0x6379d0 compares only the priority word and returns 0 for
    // a tie, so which of two stats sharing a descpriority is emitted first is decided by nothing
    // but this algorithm. 75 of the 207 described stats sit in a tie group.
    internal static class CrtQsort
    {
        // 0x685bfe: cmp eax, 8 / ja — a partition this size or smaller goes to _shortsort.
        private const int Cutoff = 8;

        // The binary's frame holds 30 entries per stack (var_F0 0x18, var_78 0x90, 0x78 bytes
        // each). Each push halves the remaining range, so 30 cannot be exceeded.
        private const int StackSize = 30;

        public static void Sort<T>(T[] items, Comparison<T> compare)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (compare == null)
            {
                throw new ArgumentNullException(nameof(compare));
            }

            if (items.Length < 2)
            {
                return; // 0x685bd1
            }

            var lowStack = new int[StackSize];
            var highStack = new int[StackSize];
            int stackTop = 0;

            int low = 0;
            int high = items.Length - 1;

            while (true)
            {
                int size = high - low + 1;

                if (size <= Cutoff)
                {
                    ShortSort(items, low, high, compare);
                }
                else
                {
                    int mid = low + (size >> 1);

                    // Median of three, ordering {low, mid, high} in place so the pivot ends up at
                    // mid — 0x685c49, 0x685c64, 0x685c7f. The older CRT swapped mid down to low
                    // and pivoted there instead; that variant produces a different permutation.
                    if (compare(items[low], items[mid]) > 0)
                    {
                        Swap(items, low, mid);
                    }

                    if (compare(items[low], items[high]) > 0)
                    {
                        Swap(items, low, high);
                    }

                    if (compare(items[mid], items[high]) > 0)
                    {
                        Swap(items, mid, high);
                    }

                    int lowGuy = low;
                    int highGuy = high;

                    while (true)
                    {
                        if (mid > lowGuy)
                        {
                            do
                            {
                                ++lowGuy;
                            }
                            while (lowGuy < mid && compare(items[lowGuy], items[mid]) <= 0);
                        }

                        if (mid <= lowGuy)
                        {
                            do
                            {
                                ++lowGuy;
                            }
                            while (lowGuy <= high && compare(items[lowGuy], items[mid]) <= 0);
                        }

                        do
                        {
                            --highGuy;
                        }
                        while (highGuy > mid && compare(items[highGuy], items[mid]) > 0);

                        if (highGuy < lowGuy)
                        {
                            break; // 0x685cee
                        }

                        Swap(items, lowGuy, highGuy);

                        // The pivot element itself just moved; follow it. 0x685d26.
                        if (mid == highGuy)
                        {
                            mid = lowGuy;
                        }
                    }

                    // Walk back over the run of pivot-equal elements so they are not re-sorted.
                    // 0x685d35 and 0x685d60.
                    ++highGuy;

                    if (mid < highGuy)
                    {
                        do
                        {
                            --highGuy;
                        }
                        while (highGuy > mid && compare(items[highGuy], items[mid]) == 0);
                    }

                    if (mid >= highGuy)
                    {
                        do
                        {
                            --highGuy;
                        }
                        while (highGuy > low && compare(items[highGuy], items[mid]) == 0);
                    }

                    // Stack the smaller half, loop on the larger. 0x685d8a, a SIGNED compare of
                    // the two spans. Note the halves are [low, highGuy] and [lowGuy, high] — the
                    // older CRT stacked highGuy - width, which is a different partition.
                    if (highGuy - low >= high - lowGuy)
                    {
                        if (low < highGuy)
                        {
                            lowStack[stackTop] = low;
                            highStack[stackTop] = highGuy;
                            ++stackTop;
                        }

                        if (lowGuy < high)
                        {
                            low = lowGuy;
                            continue;
                        }
                    }
                    else
                    {
                        if (lowGuy < high)
                        {
                            lowStack[stackTop] = lowGuy;
                            highStack[stackTop] = high;
                            ++stackTop;
                        }

                        if (low < highGuy)
                        {
                            high = highGuy;
                            continue;
                        }
                    }
                }

                if (--stackTop < 0)
                {
                    break; // 0x685c16
                }

                low = lowStack[stackTop];
                high = highStack[stackTop];
            }
        }

        // 0x685ac0. A selection sort, and NOT stable: it swaps the running maximum into the last
        // slot, so a run of equal elements comes out rotated left by one.
        private static void ShortSort<T>(T[] items, int low, int high, Comparison<T> compare)
        {
            while (high > low)
            {
                int max = low;

                for (int p = low + 1; p <= high; ++p)
                {
                    if (compare(items[p], items[max]) > 0)
                    {
                        max = p;
                    }
                }

                Swap(items, max, high);
                --high;
            }
        }

        private static void Swap<T>(T[] items, int a, int b)
        {
            T temp = items[a];
            items[a] = items[b];
            items[b] = temp;
        }
    }
}
