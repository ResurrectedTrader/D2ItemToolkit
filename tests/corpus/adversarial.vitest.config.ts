import { defineConfig } from 'vitest/config';

// A separate config so the adversarial corpus can be replayed without touching the package's own
// vitest include list. Run from src/D2ItemToolkit.Ts:
//   npx vitest run --config ../../tests/corpus/adversarial.vitest.config.ts
export default defineConfig({
  test: {
    root: '.',
    include: ['../../tests/corpus/adversarial.compare.test.ts'],
  },
});
