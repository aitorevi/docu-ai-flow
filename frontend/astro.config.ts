import { defineConfig } from 'astro/config';

// Builds a static site straight into the Worker's wwwroot, so the same .NET process that watches
// the inbox also serves the review screen — one app, one port, no separate frontend to deploy.
export default defineConfig({
  output: 'static',
  outDir: '../src/InvoiceProcessor.Worker/wwwroot',
  build: { assets: '_astro' },
  vite: {
    // `npm run dev` proxies the API to the running Worker so the UI can be developed with HMR.
    server: { proxy: { '/api': 'http://localhost:5000' } },
  },
});
