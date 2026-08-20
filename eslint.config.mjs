import js from '@eslint/js';
import tseslint from 'typescript-eslint';

// At the repository root rather than inside the package: a flat config cannot lint files above its
// own directory, and the suite deliberately lives outside the package (mirroring src/<impl> and
// tests/<impl>.Tests on the C# side).
export default tseslint.config(
  {
    ignores: [
      '**/dist/**',
      '**/node_modules/**',
      // Generated from data/ by scripts/generate-data.mjs — one ~700 KB base64 literal.
      'src/D2ItemToolkit.Ts/src/Data/EmbeddedDataBlob.ts',
      // Corpus harnesses, generated fixtures and vitest configs: outside the package tsconfig,
      // so type-aware linting has no project to resolve them against.
      'tests/corpus/**',
      '**/vitest.config.ts',
      '**/*.mjs',
    ],
  },

  {
    // The type-checked rules must be scoped to the files the project actually covers, or ESLint
    // aborts the whole run on the first file with no type information.
    files: ['src/D2ItemToolkit.Ts/src/**/*.ts', 'tests/D2ItemToolkit.Ts.Tests/**/*.ts'],
    extends: [js.configs.recommended, ...tseslint.configs.recommendedTypeChecked],
    languageOptions: {
      parserOptions: {
        // An explicit project rather than `projectService`: the suite lives outside the package,
        // and the service does not associate those files with this tsconfig.
        project: ['./src/D2ItemToolkit.Ts/tsconfig.json'],
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      // The engine models a 32-bit binary: `| 0`, `<< 8` and `>>> 0` are the semantics, not
      // sloppiness, and they appear on almost every arithmetic line.
      'no-bitwise': 'off',

      // The DescFunc writers mirror C# `arg.ToString()` on a boxed value. Narrowing the parameter
      // would change which overload runs, and the differential pins the rendered strings.
      '@typescript-eslint/no-base-to-string': 'off',

      // Static comparators are passed to `sort` by reference (`compareByLayer`); they take no
      // `this`, so the unbound-method warning does not apply.
      '@typescript-eslint/unbound-method': 'off',

      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
    },
  },

  {
    // Tests assert against a corpus the suite has already proven present, so a non-null assertion
    // there is load-bearing brevity rather than a missing check.
    files: ['tests/D2ItemToolkit.Ts.Tests/**/*.ts'],
    rules: {
      '@typescript-eslint/no-non-null-assertion': 'off',
    },
  },
);
