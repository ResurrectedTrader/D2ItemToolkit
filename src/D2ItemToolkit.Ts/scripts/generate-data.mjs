/**
 * Packs the repository's `data/` tree into a single gzipped, base64'd TypeScript module so the
 * published package carries the game tables the way the C# assembly embeds them as resources.
 *
 * Run with `npm run generate:data` whenever `data/` changes. The output is generated — edit this
 * script, never EmbeddedDataBlob.ts.
 */
import { readFileSync, writeFileSync, readdirSync, statSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'fflate';

const here = fileURLToPath(new URL('./', import.meta.url));
const dataRoot = path.resolve(here, '../../../data');
const outFile = path.resolve(here, '../src/Data/EmbeddedDataBlob.ts');

// The three trees the C# embeds, under the same names D2DataFiles asks for.
const TREES = ['excel', 'locale/eng', 'global'];

function collect() {
  const entries = [];

  for (const tree of TREES) {
    const directory = path.join(dataRoot, ...tree.split('/'));
    for (const name of readdirSync(directory).sort()) {
      const full = path.join(directory, name);
      if (!statSync(full).isFile()) {
        continue;
      }

      entries.push({ name: tree + '/' + name, bytes: new Uint8Array(readFileSync(full)) });
    }
  }

  return entries;
}

/**
 * Container layout, little-endian, mirroring what EmbeddedData.ts parses back:
 *
 *   'D2TD' | u32 count | per entry: u16 nameLength | name (UTF-8) | u32 length | bytes
 */
function pack(entries) {
  const encoder = new TextEncoder();
  const named = entries.map(e => ({ ...e, name: encoder.encode(e.name) }));

  let size = 8;
  for (const entry of named) {
    size += 2 + entry.name.length + 4 + entry.bytes.length;
  }

  const out = new Uint8Array(size);
  const view = new DataView(out.buffer);
  let at = 0;

  out.set(encoder.encode('D2TD'), at);
  at += 4;
  view.setUint32(at, named.length, true);
  at += 4;

  for (const entry of named) {
    view.setUint16(at, entry.name.length, true);
    at += 2;
    out.set(entry.name, at);
    at += entry.name.length;
    view.setUint32(at, entry.bytes.length, true);
    at += 4;
    out.set(entry.bytes, at);
    at += entry.bytes.length;
  }

  return out;
}

const entries = collect();
const raw = pack(entries);
// mtime 0 is load-bearing: the gzip header carries a modification time, so without pinning it the
// output differs on every run and the "is the blob stale?" check in CI can never pass.
const compressed = gzipSync(raw, { level: 9, mtime: 0 });
const base64 = Buffer.from(compressed).toString('base64');

// Chunked so the generated file stays openable in an editor and does not sit on one 700 KB line.
const chunks = [];
for (let at = 0; at < base64.length; at += 1000) {
  chunks.push("  '" + base64.slice(at, at + 1000) + "',");
}

const source = `/* eslint-disable */
// GENERATED FILE — do not edit by hand.
// Regenerate with \`npm run generate:data\` after changing the repository's data/ tree.
//
// ${entries.length} files, ${raw.length} bytes raw, ${compressed.length} bytes gzipped.

/** A gzipped 'D2TD' container; EmbeddedData.ts inflates and parses it on first use. */
export const EmbeddedArchiveBase64: string = [
${chunks.join('\n')}
].join('');
`;

mkdirSync(path.dirname(outFile), { recursive: true });
writeFileSync(outFile, source, 'utf8');

console.log(
  entries.length + ' files, ' + (raw.length / 1024).toFixed(0) + ' KB raw -> '
  + (compressed.length / 1024).toFixed(0) + ' KB gzip -> '
  + (base64.length / 1024).toFixed(0) + ' KB base64');
