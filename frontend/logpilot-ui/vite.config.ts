import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// /api isteklerini backend'e yönlendiriyoruz: tarayıcıda CORS sorunu yok.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
});
