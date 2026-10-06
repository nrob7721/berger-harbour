import { resolve } from 'node:path';
import { defineConfig } from 'vite';

// Local development proxies /api to admin-api (dotnet run, which bypasses Cloudflare Access only in Development).
export default defineConfig({
  resolve: { alias: { '@shared': resolve(__dirname, '../shared/src') } },
  server: {
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': {
        target: process.env.ADMIN_API_URL ?? 'http://localhost:5081',
        changeOrigin: true,
        headers: { 'X-Edge-Proxy-Secret': process.env.EDGE_PROXY_SECRET ?? 'dev-edge-secret' },
      },
    },
  },
  build: { target: 'es2022' },
});
