// The browser stand-in for FileSystem.ts, substituted by `package.json`'s `browser` field. It has
// the same exports and imports nothing from Node, which is what lets a bundler resolve the package
// at all — the real module's `node:fs` import is unresolvable for a browser target.
//
// These do not silently return nothing. A browser reaching here means the caller asked for the
// filesystem loaders, and a wrong tooltip built from zero tables is far worse to debug than an
// error that names the working alternative.

const Unsupported =
  'D2ItemToolkit: the filesystem loaders are not available in a browser. ' +
  'Use TooltipEngine.embedded / D2DataFiles.load() with no arguments, which reads the embedded ' +
  'tables, or D2DataFiles.build(...) with byte sources you fetch yourself.';

export const hasFileSystem = false;

export function findDataRoot(): string {
  throw new Error(Unsupported);
}

export function readIfPresent(_directory: string, _name: string): Uint8Array | null {
  throw new Error(Unsupported);
}

export function listFiles(_directory: string): readonly string[] {
  throw new Error(Unsupported);
}
