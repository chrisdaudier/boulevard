import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const certDir = new URL('../interop/certs/', import.meta.url)

// https://vite.dev/config/
export default defineConfig({
  // HERE Core's fins:// URL scheme only fetches manifests over HTTPS, and the runtime rejects an
  // untrusted self-signed cert outright (no click-through) - this app hosts the platform manifest
  // and provider.html, so it uses a real mkcert-issued, locally-trusted cert (see
  // frontend/interop/certs/, generated via `mkcert -install && mkcert localhost 127.0.0.1 ::1`).
  plugins: [react()],
  server: {
    port: 5174,
    https: {
      cert: readFileSync(new URL('localhost+2.pem', certDir)),
      key: readFileSync(new URL('localhost+2-key.pem', certDir)),
    },
  },
})
