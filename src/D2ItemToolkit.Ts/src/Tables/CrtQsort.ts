// The MSVC CRT qsort the game links (_qsort 0x685b50, calling _shortsort 0x685ac0). It is ported
// rather than replaced by Array.prototype.sort because its permutation of EQUAL elements is
// observable: SORT_ItemDescPriority 0x6379d0 compares only the priority word and returns 0 for a
// tie, so which of two stats sharing a descpriority is emitted first is decided by nothing but
// this algorithm. 75 of the 207 described stats sit in a tie group.

// 0x685bfe: cmp eax, 8 / ja — a partition this size or smaller goes to _shortsort.
const CUTOFF = 8;

export function crtQsort<T>(items: T[], compare: (a: T, b: T) => number): void {
  if (items.length < 2) {
    return; // 0x685bd1
  }

  const lowStack: number[] = [];
  const highStack: number[] = [];

  let low = 0;
  let high = items.length - 1;

  const at = (index: number): T => items[index] as T;

  const swap = (a: number, b: number): void => {
    const temp = at(a);
    items[a] = at(b);
    items[b] = temp;
  };

  for (;;) {
    const size = high - low + 1;

    if (size <= CUTOFF) {
      shortSort(items, low, high, compare);
    } else {
      let mid = low + (size >> 1);

      // Median of three, ordering {low, mid, high} in place so the pivot ends up at mid —
      // 0x685c49, 0x685c64, 0x685c7f. The older CRT swapped mid down to low and pivoted there
      // instead; that variant produces a different permutation.
      if (compare(at(low), at(mid)) > 0) {
        swap(low, mid);
      }

      if (compare(at(low), at(high)) > 0) {
        swap(low, high);
      }

      if (compare(at(mid), at(high)) > 0) {
        swap(mid, high);
      }

      let lowGuy = low;
      let highGuy = high;

      for (;;) {
        if (mid > lowGuy) {
          do {
            ++lowGuy;
          } while (lowGuy < mid && compare(at(lowGuy), at(mid)) <= 0);
        }

        if (mid <= lowGuy) {
          do {
            ++lowGuy;
          } while (lowGuy <= high && compare(at(lowGuy), at(mid)) <= 0);
        }

        do {
          --highGuy;
        } while (highGuy > mid && compare(at(highGuy), at(mid)) > 0);

        if (highGuy < lowGuy) {
          break; // 0x685cee
        }

        swap(lowGuy, highGuy);

        // The pivot element itself just moved; follow it. 0x685d26.
        if (mid === highGuy) {
          mid = lowGuy;
        }
      }

      // Walk back over the run of pivot-equal elements so they are not re-sorted.
      // 0x685d35 and 0x685d60.
      ++highGuy;

      if (mid < highGuy) {
        do {
          --highGuy;
        } while (highGuy > mid && compare(at(highGuy), at(mid)) === 0);
      }

      if (mid >= highGuy) {
        do {
          --highGuy;
        } while (highGuy > low && compare(at(highGuy), at(mid)) === 0);
      }

      // Stack the smaller half, loop on the larger. 0x685d8a, a SIGNED compare of the two spans.
      // Note the halves are [low, highGuy] and [lowGuy, high] — the older CRT stacked
      // highGuy - width, which is a different partition.
      if (highGuy - low >= high - lowGuy) {
        if (low < highGuy) {
          lowStack.push(low);
          highStack.push(highGuy);
        }

        if (lowGuy < high) {
          low = lowGuy;
          continue;
        }
      } else {
        if (lowGuy < high) {
          lowStack.push(lowGuy);
          highStack.push(high);
        }

        if (low < highGuy) {
          high = highGuy;
          continue;
        }
      }
    }

    if (lowStack.length === 0) {
      break; // 0x685c16
    }

    high = highStack.pop() as number;
    low = lowStack.pop() as number;
  }
}

// 0x685ac0. A selection sort, and NOT stable: it swaps the running maximum into the last slot, so
// a run of equal elements comes out rotated left by one.
function shortSort<T>(
  items: T[],
  low: number,
  high: number,
  compare: (a: T, b: T) => number,
): void {
  while (high > low) {
    let max = low;

    for (let p = low + 1; p <= high; ++p) {
      if (compare(items[p] as T, items[max] as T) > 0) {
        max = p;
      }
    }

    const temp = items[max] as T;
    items[max] = items[high] as T;
    items[high] = temp;
    --high;
  }
}
