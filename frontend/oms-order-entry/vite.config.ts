import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const certDir = new URL('../interop/certs/', import.meta.url)

// https://vite.dev/config/
export default defineConfig({
  // Matches watchlist-mfe's mkcert-based HTTPS setup - keeps both views on the same trusted
  // scheme inside the HERE Core / OpenFin platform window.
  plugins: [react()],
  server: {
    port: 5173,
    https: {
      cert: readFileSync(new URL('localhost+2.pem', certDir)),
      key: readFileSync(new URL('localhost+2-key.pem', certDir)),
    },
  },
})
