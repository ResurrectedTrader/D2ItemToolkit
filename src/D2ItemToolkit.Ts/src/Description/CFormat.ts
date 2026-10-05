const utf8Encoder = new TextEncoder();
const utf8Decoder = new TextDecoder('utf-8');

/**
 * The C runtime's printf, as D2R's string formatting reaches it (SStrPrintf 0x14014f1f0 and the
 * descfunc writers). D2R moved the signs and percent signs INTO its strings — "Fire Absorb %+d%%"
 * — which the 1.14d formatter (0x5269d0, `%d %s %u` only) has no notion of.
 */
export const CFormat = {
  sprintf(format: string | null | undefined, ...args: unknown[]): string {
    if (format === null || format === undefined || format.length === 0) {
      return '';
    }

    let builder = '';
    const cursor = { next: 0 };

    for (let i = 0; i < format.length; ++i) {
      const c = format.charAt(i);
      if (c !== '%') {
        builder += c;
        continue;
      }
      ++i;

      let leftAlign = false;
      let plus = false;
      let space = false;
      let zero = false;
      let alternate = false;
      for (; i < format.length; ++i) {
        const flag = format.charAt(i);
        if (flag === '-') leftAlign = true;
        else if (flag === '+') plus = true;
        else if (flag === ' ') space = true;
        else if (flag === '0') zero = true;
        else if (flag === '#') alternate = true;
        else break;
      }

      let read = readNumber(format, i);
      const width = read.value;
      i = read.at;
      let precision = -1;
      if (i < format.length && format.charAt(i) === '.') {
        read = readNumber(format, i + 1);
        precision = read.value;
        i = read.at;
      }

      // A conversion still open at the NUL writes nothing: the loop just ends (0x140b477d5) and the
      // a3 == 0 callers skip the deferred pass (0x140b47c29).
      if (i >= format.length) {
        break;
      }

      const spec = format.charAt(i);
      let body: string;
      switch (spec) {
        case '%':
          builder += '%';
          continue;

        case 'd':
        case 'i': {
          const value = toInteger(next(args, cursor));
          const digits = toDigits(Math.abs(value), precision, 10, false);
          const sign = value < 0 ? '-' : plus ? '+' : space ? ' ' : '';
          body = pad(sign, digits, width, leftAlign, zero && precision < 0);
          break;
        }

        case 'u':
        case 'x':
        case 'X': {
          const value = toInteger(next(args, cursor)) >>> 0;
          const radix = spec === 'u' ? 10 : 16;
          const digits = toDigits(value, precision, radix, spec === 'X');
          const prefix =
            alternate && radix === 16 && value !== 0 ? (spec === 'X' ? '0X' : '0x') : '';
          body = pad(prefix, digits, width, leftAlign, zero && precision < 0);
          break;
        }

        case 'c': {
          const arg = next(args, cursor);
          const text =
            typeof arg === 'string' && arg.length === 1
              ? arg
              : String.fromCharCode(toInteger(arg) & 0xffff);
          body = pad('', text, width, leftAlign, false);
          break;
        }

        case 's': {
          const arg = next(args, cursor);
          let text = typeof arg === 'string' ? arg : '(null)';
          if (precision >= 0 && text.length > precision) {
            text = text.substring(0, precision);
          }

          body = pad('', text, width, leftAlign, false);
          break;
        }

        default:
          // sub_140b47700 prints an unknown conversion character as itself.
          body = spec;
          break;
      }

      builder += body;
    }

    return builder;
  },

  /**
   * The positional wrappers (sub_14060cb00 and siblings): `%0`, `%1`, ... become `%d` or `%s` by
   * the argument's type and pick arguments by index. A format with no markers is plain printf; one
   * that names only SOME of the arguments writes nothing.
   */
  positional(format: string | null | undefined, ...args: unknown[]): string {
    if (format === null || format === undefined || format.length === 0) {
      return '';
    }

    const order: unknown[] = [];
    const seen = new Set<number>();
    let rewritten = '';
    for (let i = 0; i < format.length; ++i) {
      const c = format.charAt(i);
      if (c === '%' && i + 1 < format.length) {
        const following = format.charAt(i + 1);
        if (following === '%') {
          rewritten += '%%';
          ++i;
          continue;
        }

        if (following >= '0' && following <= '9') {
          const index = following.charCodeAt(0) - 0x30;
          const arg = index < args.length ? args[index] : null;
          rewritten += typeof arg === 'string' ? '%s' : '%d';
          order.push(arg);
          seen.add(index);
          ++i;
          continue;
        }
      }

      rewritten += c;
    }

    if (seen.size === 0) {
      return CFormat.sprintf(format, ...args);
    }

    for (let index = 0; index < args.length; ++index) {
      if (!seen.has(index)) {
        return '';
      }
    }

    return CFormat.sprintf(rewritten, ...order);
  },

  /**
   * sub_14060d840, descfunc 15's (value, level, name) wrapper. Each of `%0`, `%1`, `%2` is found by
   * its own scan, and only its first occurrence is rewritten (to `d`, `d`, `s`); the arguments
   * are passed in the order the markers appear. `%0` without both others, or `%1`/`%2` without
   * `%0`, writes nothing.
   */
  positionalValueLevelName(
    format: string | null | undefined,
    value: number,
    level: number,
    name: string,
  ): string {
    return CFormat.positionalWrapper(format, 'dds', false, value, level, name);
  },

  /**
   * The descfunc positional wrappers share one shape. sub_14060cb00 (d d, func 11's repair
   * string), sub_14060cea0 (d s, funcs 16 and 28), sub_14060d840 (d d s, func 15),
   * sub_14060de20 (d s s, func 27) and sub_14060e430 (d s d d, func 24).
   *
   * Each scans for its markers, rewrites the first copy of each marker to its fixed type, and
   * passes the arguments in the order the markers appear. If `%0` is present and any other marker
   * is missing, nothing is written. With no `%0`, the format is plain printf when no other marker
   * is present either, and nothing otherwise. sub_14060e430 also writes nothing unless `%0` and
   * `%1` both precede `%2` and `%2` precedes `%3` (0x14060e6ca). The format is cut to 1023 bytes
   * and the output to 255.
   */
  positionalWrapper(
    format: string | null | undefined,
    types: string,
    chargesOrder: boolean,
    ...args: unknown[]
  ): string {
    if (format === null || format === undefined || format.length === 0) {
      return '';
    }

    const text = CFormat.bounded(format, 0x400).split('');
    const positions: number[] = [];
    for (let k = 0; k < types.length; ++k) {
      positions.push(findPositionalMarker(text, String(k)));
    }

    const at = (k: number): number => positions[k] ?? -1;
    let ordered: unknown[];
    if (at(0) >= 0) {
      if (positions.some(p => p < 0)) {
        return '';
      }

      if (chargesOrder && !(at(0) < at(2) && at(1) < at(2) && at(2) < at(3))) {
        return '';
      }

      for (let k = 0; k < positions.length; ++k) {
        text[at(k)] = types[k] ?? 'd';
      }

      ordered = positions
        .map((p, k) => ({ at: p, arg: args[k] }))
        .sort((a, b) => a.at - b.at)
        .map(entry => entry.arg);
    } else {
      if (positions.some(p => p >= 0)) {
        return '';
      }

      ordered = args;
    }

    return CFormat.bounded(CFormat.sprintf(text.join(''), ...ordered), 0x100);
  },

  utf8Length(text: string | null | undefined): number {
    return utf8Encoder.encode(text ?? '').length;
  },

  /**
   * What fits a C buffer of `size` bytes: at most size - 1 bytes of the UTF-8 text, cut
   * mid-character if that is where the limit falls.
   */
  bounded(text: string | null | undefined, size: number): string {
    const bytes = utf8Encoder.encode(text ?? '');
    return bytes.length < size ? (text ?? '') : utf8Decoder.decode(bytes.subarray(0, size - 1));
  },
};

// A '%' toggles "inside a conversion"; flag and length characters (' ' # + - . I L h j l q t w
// z, the bitmasks at 0x14060d8ab-0x14060d8bf) keep it open, anything else closes it. The marker
// is the wanted digit met while open.
function findPositionalMarker(text: readonly string[], digit: string): number {
  const keepsOpen = ' #+-.ILhjlqtwz';

  let open = false;
  for (let i = 0; i < text.length; ++i) {
    const c = text[i] as string;
    if (c === '%') {
      open = !open;
    } else if (!keepsOpen.includes(c)) {
      if (open && c === digit) {
        return i;
      }

      open = false;
    }
  }

  return -1;
}

function next(args: readonly unknown[], cursor: { next: number }): unknown {
  if (cursor.next >= args.length) {
    throw new Error('More format specifiers than arguments.');
  }

  return args[cursor.next++];
}

/** Convert.ToInt64: a null argument is 0. */
function toInteger(arg: unknown): number {
  if (arg === null || arg === undefined) {
    return 0;
  }

  if (typeof arg === 'boolean') {
    return arg ? 1 : 0;
  }

  return Math.trunc(Number(arg));
}

function readNumber(format: string, at: number): { value: number; at: number } {
  let value = 0;
  while (at < format.length && format.charAt(at) >= '0' && format.charAt(at) <= '9') {
    value = value * 10 + (format.charCodeAt(at) - 0x30);
    ++at;
  }

  return { value, at };
}

function toDigits(value: number, precision: number, radix: number, upper: boolean): string {
  // C: a zero value with an explicit zero precision prints no digits at all.
  if (value === 0 && precision === 0) {
    return '';
  }

  let digits = value.toString(radix);
  if (upper) {
    digits = digits.toUpperCase();
  }

  return precision > digits.length ? '0'.repeat(precision - digits.length) + digits : digits;
}

function pad(
  prefix: string,
  digits: string,
  width: number,
  leftAlign: boolean,
  zero: boolean,
): string {
  const length = prefix.length + digits.length;
  if (length >= width) {
    return prefix + digits;
  }

  const fill = (zero && !leftAlign ? '0' : ' ').repeat(width - length);
  if (leftAlign) {
    return prefix + digits + fill;
  }

  return zero ? prefix + fill + digits : fill + prefix + digits;
}
