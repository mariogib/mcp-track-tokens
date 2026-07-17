/**
 * Vite + Vitest config. Typed via tsconfig.node.json (moduleResolution: bundler).
 */
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 900,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5187',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://127.0.0.1:5187',
        changeOrigin: true,
      },
      '/ready': {
        target: 'http://127.0.0.1:5187',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/tests/setup.ts',
    css: true,
  },
});
