// npm resolves `files`, and the README it shows on the package page, relative to the PACKAGE
// directory — but the README and LICENCE live at the repository root, shared with the C# package.
// Copy them in before packing rather than keeping two of each in sync by hand. Both copies are
// gitignored; the root ones are the originals.
import { copyFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageDir = dirname(dirname(fileURLToPath(import.meta.url)));
const repoRoot = join(packageDir, '..', '..');

for (const name of ['README.md', 'LICENSE']) {
  copyFileSync(join(repoRoot, name), join(packageDir, name));
  console.log(`copied ${name}`);
}
