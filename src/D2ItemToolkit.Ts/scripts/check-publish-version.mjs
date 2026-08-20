// Refuses to publish the placeholder version.
//
// package.json carries 0.0.0 on purpose: the real version comes from the git tag and the publish
// workflow writes it in, so there is one source of truth. The cost of that is a placeholder sitting
// in the file, and `npm publish` from a working tree would happily send it to the registry — where
// npm never lets a version be reused, not even after an unpublish. This runs from prepublishOnly,
// which fires before prepack, so it stops that before anything is built or uploaded.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageDir = dirname(dirname(fileURLToPath(import.meta.url)));
const manifest = JSON.parse(readFileSync(join(packageDir, 'package.json'), 'utf8'));

if (manifest.version === '0.0.0') {
  console.error(
    'Refusing to publish d2itemtoolkit@0.0.0.\n' +
      '\n' +
      '0.0.0 is the placeholder in package.json; the real version comes from the git tag. Publish\n' +
      'by pushing a tag (git tag -a v1.2.3 -m ... && git push origin v1.2.3) or by running the\n' +
      'Publish workflow, either of which sets the version first. To publish by hand anyway, set a\n' +
      'real version with `npm pkg set version=...` before packing.',
  );
  process.exit(1);
}

console.log('publishing d2itemtoolkit@' + manifest.version);
