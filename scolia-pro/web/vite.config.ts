import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// L'application est servie par le serveur SCOLIA (.NET) : le build est copié dans son dossier wwwroot.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/Scolia.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 900,
  },
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
  test: { environment: 'node' },
});
