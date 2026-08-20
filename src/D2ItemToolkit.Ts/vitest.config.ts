import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // Tests live outside the package, mirroring the C# layout: src/<impl> and tests/<impl>.Tests.
    include: ['../../tests/D2ItemToolkit.Ts.Tests/**/*.test.ts'],
    coverage: {
      provider: 'v8',
      include: ['src/**/*.ts'],
      reporter: ['text', 'cobertura'],
    },
  },
});
