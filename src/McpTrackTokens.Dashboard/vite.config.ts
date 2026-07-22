/**
 * Vite + Vitest config. Typed via tsconfig.node.json (moduleResolution: bundler).
 */
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

const dashboardRoot = path.dirname(fileURLToPath(import.meta.url));
const sharedRoot = path.resolve(dashboardRoot, '../../../frontend-shared');
const sharedDist = path.resolve(sharedRoot, 'dist').replace(/\\/g, '/');

export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 900,
  },
  server: {
    port: 5180,
    fs: {
      allow: [sharedRoot, dashboardRoot],
    },
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
  optimizeDeps: {
    exclude: ['@lunarq/frontend-shared'],
  },
  resolve: {
    alias: [
      {
        find: /^@lunarq\/frontend-shared$/,
        replacement: `${sharedDist}/index.js`,
      },
      {
        find: /^@lunarq\/frontend-shared\/(admin|components|hooks|theme|auth|maintenance|utils)$/,
        replacement: `${sharedDist}/$1/index.js`,
      },
      {
        find: /^@lunarq\/frontend-shared\/(.*)/,
        replacement: `${sharedDist}/$1`,
      },
    ],
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/tests/setup.ts',
    css: true,
  },
});
