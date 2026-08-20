import { gunzipSync } from 'fflate';
import { EmbeddedArchiveBase64 } from './EmbeddedDataBlob.js';
import type { ByteSource } from './TxtDataSource.js';

/**
 * The game tables, carried inside the package the way the C# carries them as assembly resources
 * (`EmbeddedResource` in D2ItemToolkit.Net.csproj).
 *
 * The alternative — reading the repository's `data/` directory through `node:fs` — makes the
 * published package useless outside this repo and ties it to Node. Everything here is synchronous
 * so `D2DataFiles.load()` keeps the signature the C# has and the whole test suite already uses;
 * that is why the inflate comes from fflate rather than the browser's async DecompressionStream.
 */

interface Archive {
  /** Keyed by lowercased `tree/name`, because extractions vary in case. */
  readonly files: Map<string, Uint8Array>;
  /** The names as stored, so `dataFileNames` reports real filenames rather than lowercased ones. */
  readonly names: readonly string[];
}

let archive: Archive | null = null;

function fromBase64(text: string): Uint8Array {
  const scope = globalThis as { atob?: (data: string) => string };

  if (typeof scope.atob === 'function') {
    const binary = scope.atob(text);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; ++i) {
      bytes[i] = binary.charCodeAt(i);
    }

    return bytes;
  }

  // Node before 16 has no atob; Buffer is a Uint8Array, so this is a view, not a copy.
  return new Uint8Array(Buffer.from(text, 'base64'));
}

/** Reverses the container `scripts/generate-data.mjs` writes. */
function parse(bytes: Uint8Array): Archive {
  const files = new Map<string, Uint8Array>();
  const names: string[] = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const decoder = new TextDecoder();

  if (decoder.decode(bytes.subarray(0, 4)) !== 'D2TD') {
    throw new Error('embedded data is not a D2TD container');
  }

  const count = view.getUint32(4, true);
  let at = 8;

  for (let i = 0; i < count; ++i) {
    const nameLength = view.getUint16(at, true);
    at += 2;
    const name = decoder.decode(bytes.subarray(at, at + nameLength));
    at += nameLength;
    const length = view.getUint32(at, true);
    at += 4;
    files.set(name.toLowerCase(), bytes.subarray(at, at + length));
    names.push(name);
    at += length;
  }

  return { files, names };
}

function load(): Archive {
  if (archive === null) {
    archive = parse(gunzipSync(fromBase64(EmbeddedArchiveBase64)));
  }

  return archive;
}

/** True when the package was built with a populated archive. */
export function hasEmbeddedData(): boolean {
  return load().files.size > 0;
}

/**
 * A {@link ByteSource} over one embedded tree — `excel`, `locale/eng` or `global`. Names are
 * matched case-insensitively, matching the directory reader: extractions vary in case.
 */
export function embeddedSource(tree: string): ByteSource {
  return name => load().files.get((tree + '/' + name).toLowerCase()) ?? null;
}

/** The file names under one embedded tree, for `D2DataFiles.dataFileNames`. */
export function embeddedFiles(tree: string): readonly string[] {
  const prefix = tree + '/';

  return load()
    .names.filter(name => name.startsWith(prefix))
    .map(name => name.substring(prefix.length));
}
