import { resolve } from 'node:path';
import { defineConfig } from 'vite';

// Local development proxies /api to public-api (dotnet run) and adds the edge proxy secret, like the Pages Function.
export default defineConfig({
  resolve: { alias: { '@shared': resolve(__dirname, '../shared/src') } },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: process.env.PUBLIC_API_URL ?? 'http://localhost:5080',
        changeOrigin: true,
        headers: { 'X-Edge-Proxy-Secret': process.env.EDGE_PROXY_SECRET ?? 'dev-edge-secret' },
      },
    },
  },
  build: {
    target: 'es2022',
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        pay: resolve(__dirname, 'pay/index.html'),
      },
    },
  },
});
