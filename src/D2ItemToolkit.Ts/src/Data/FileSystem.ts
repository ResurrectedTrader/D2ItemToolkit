// The ONLY module in the package that touches Node. Everything filesystem-shaped funnels through
// here so exactly one file has to be swapped for a browser build, which `package.json`'s `browser`
// field does — see FileSystem.browser.ts. Import it from nowhere else, or the entry graph grows a
// `node:fs` edge again and a browser bundle stops resolving.
//
// Nothing here runs at module evaluation. It used to: `DataRoot` was a top-level const, so merely
// importing the package probed the disk up to eight times, in a published install where the answer
// could never be useful.
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const hasFileSystem = true;

/**
 * Located by walking up from this module rather than by a fixed `../../..`, because that count is a
 * function of where this FILE sits. Moving it into a subfolder silently repointed it at a directory
 * that does not exist, and every table then failed to load at RUNTIME while `tsc` still reported a
 * clean build — a relative depth is not something the type checker can verify.
 */
export function findDataRoot(): string {
  let directory = fileURLToPath(new URL('./', import.meta.url));

  // src/ and dist/ sit at the same depth, so one search serves the sources and the built package.
  for (let up = 0; up < 8; ++up) {
    const candidate = path.join(directory, 'data');
    if (existsSync(path.join(candidate, 'excel'))) {
      return candidate + path.sep;
    }

    const parent = path.dirname(directory);
    if (parent === directory) {
      break;
    }

    directory = parent;
  }

  // Fall back to the conventional location so a missing tree still fails downstream with
  // "Required data file not found: <name>" rather than at module load.
  return fileURLToPath(new URL('../../../../data/', import.meta.url));
}

// Extractions vary in case, so fall back to a case-insensitive scan of the directory.
export function readIfPresent(directory: string, name: string): Uint8Array | null {
  const exact = directory + '/' + name;
  if (existsSync(exact) && statSync(exact).isFile()) {
    return new Uint8Array(readFileSync(exact));
  }

  if (!existsSync(directory)) {
    return null;
  }

  for (const candidate of readdirSync(directory)) {
    if (candidate.toLowerCase() === name.toLowerCase()) {
      return new Uint8Array(readFileSync(directory + '/' + candidate));
    }
  }

  return null;
}

export function listFiles(directory: string): readonly string[] {
  return existsSync(directory) ? readdirSync(directory) : [];
}
