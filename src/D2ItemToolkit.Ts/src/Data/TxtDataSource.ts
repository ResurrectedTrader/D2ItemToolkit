// Browser-safe. Every filesystem primitive comes from ./FileSystem.js, which the `browser` field
// swaps for a stub, so importing this module costs nothing and touches nothing.
import { findDataRoot, hasFileSystem, listFiles, readIfPresent } from './FileSystem.js';

/**
 * Where a set of game tables comes from. The C# reads assembly resources; this is the seam that
 * lets a browser build hand `D2DataFiles.build` bytes it obtained some other way.
 */
export type ByteSource = (name: string) => Uint8Array | null;

/** False in a browser bundle, where the directory loaders throw rather than read. */
export const canReadFromDisk = hasFileSystem;

// Resolved on first use and cached, not at module evaluation: a published install never needs the
// repository's data/ tree, and a browser cannot answer the question at all.
let cachedRoot: string | null = null;

/** The repository's `data/` tree, which holds the same files the C# embeds. */
export function dataRoot(): string {
  cachedRoot ??= findDataRoot();
  return cachedRoot;
}

export function excelDirectory(): string {
  return dataRoot() + 'excel';
}

export function localeDirectory(): string {
  return dataRoot() + 'locale/eng';
}

export function globalDirectory(): string {
  return dataRoot() + 'global';
}

export function directorySource(directory: string | null): ByteSource {
  return name => (directory === null ? null : readIfPresent(directory, name));
}

export function listDirectory(directory: string): readonly string[] {
  return listFiles(directory);
}
