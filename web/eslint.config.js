import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import importPlugin from 'eslint-plugin-import';
import globals from 'globals';

/** Bulletproof React import rules for one app: features stay independent and imports flow shared → app-wide → features → app. */
function appZones(app, features) {
  const src = `./${app}/src`;
  return [
    // A feature never imports from another feature (compose them in the app layer instead).
    ...features.map((f) => ({
      target: `${src}/features/${f}`,
      from: `${src}/features`,
      except: [`./${f}`],
      message: 'A feature must not import another feature; compose them in src/app.',
    })),
    // Features never import the app layer.
    { target: `${src}/features`, from: `${src}/app`, message: 'Features must not import from src/app.' },
    // App-wide code never imports features or the app layer.
    ...['components', 'hooks', 'lib', 'utils', 'config'].map((dir) => ({
      target: `${src}/${dir}`,
      from: [`${src}/features`, `${src}/app`],
      message: 'App-wide code must not import features or src/app.',
    })),
  ];
}

export default tseslint.config(
  { ignores: ['**/dist/**', '**/node_modules/**', '**/.wrangler/**', 'shared/src/api/public-api.ts', 'shared/src/api/admin-api.ts'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: { globals: { ...globals.browser } },
    plugins: { 'react-hooks': reactHooks, import: importPlugin },
    settings: {
      'import/resolver': {
        typescript: { project: ['booking/tsconfig.json', 'admin/tsconfig.json', 'shared/tsconfig.json'], noWarnOnMultipleProjects: true },
      },
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      'import/no-restricted-paths': [
        'error',
        {
          zones: [
            // shared never imports from an app.
            { target: './shared', from: ['./booking', './admin'], message: 'shared must not import from an app.' },
            // The apps never import each other.
            { target: './booking', from: './admin' },
            { target: './admin', from: './booking' },
            ...appZones('booking', ['dates-and-boat', 'details', 'addons', 'summary', 'deposit-checkout', 'complete', 'balance-payment']),
            ...appZones('admin', ['bookings', 'fleet', 'customers', 'settings']),
          ],
        },
      ],
    },
  },
  {
    files: ['*/functions/**/*.ts'],
    languageOptions: { globals: { ...globals.serviceworker } },
  },
  {
    files: ['**/*.test.{ts,tsx}', '**/testing/**'],
    languageOptions: { globals: { ...globals.node } },
  },
);
