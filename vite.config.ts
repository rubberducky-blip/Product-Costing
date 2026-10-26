import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// base './' keeps the build working on any static host (GitHub Pages sub-path, Vercel, Netlify).
export default defineConfig({
  base: './',
  plugins: [react()],
})
