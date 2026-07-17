import * as esbuild from 'esbuild';

const entries = [
  'prompt-submitted',
  'agent-started',
  'agent-completed',
  'agent-failed',
  'agent-cancelled',
  'session-started',
  'session-ended',
  'diagnostics',
];

await esbuild.build({
  entryPoints: entries.map((name) => `src/${name}.ts`),
  bundle: true,
  outdir: 'dist',
  platform: 'node',
  target: 'node18',
  format: 'cjs',
  sourcemap: true,
  logLevel: 'info',
  banner: {
    js: '#!/usr/bin/env node',
  },
});
