import { resolve } from 'node:path';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  resolve: { alias: { '@shared': resolve(__dirname, 'shared/src') } },
  test: {
    include: ['shared/src/**/*.test.ts', 'booking/src/**/*.test.ts', 'admin/src/**/*.test.ts'],
    environment: 'node',
  },
});
