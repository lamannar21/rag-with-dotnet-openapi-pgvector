import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const proxy = { '/owners-manual': { target: process.env.AGENTS_API_URL || 'http://localhost:5290', changeOrigin: true } };
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true, proxy },
  preview: { proxy },
});
