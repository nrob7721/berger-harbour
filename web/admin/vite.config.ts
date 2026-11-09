import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

const r = (p: string) => fileURLToPath(new URL(p, import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': r('./src'), '@shared': r('../shared/src') } },
  server: {
    port: 5174,
    strictPort: true,
    proxy: { '/api': 'http://localhost:5081' },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/testing/setup.tsx'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
    env: { TZ: 'UTC' },
  },
});
