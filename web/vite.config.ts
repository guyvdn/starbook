import { defineConfig } from 'vite';

// The API serves the build from its wwwroot. In dev, Vite serves the page and proxies the API.
const api = 'http://localhost:5080';

export default defineConfig({
  build: {
    outDir: '../src/Bookmarks.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/img': api,
    },
  },
});
