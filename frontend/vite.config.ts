import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    allowedHosts: ['.trycloudflare.com'],
    proxy: {
      '/api': {
        // The isolated E2E runner supplies its own API target. Keeping the
        // browser same-origin preserves the real cookie and CSRF flow.
        target: process.env.VITE_API_PROXY_TARGET ?? 'https://localhost:7226',
        secure: false,
      },
      '/health': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'https://localhost:7226',
        secure: false,
      },
    },
  },
})
