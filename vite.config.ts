import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// The Supabase URL and anon key are public by design (data is protected by login + RLS).
// Accept them under several names so hosts that reject public prefixes (VITE_…) can use
// plain/secret variables such as SUPABASE_URL / SUPABASE_ANON_KEY, as set by the Supabase–Vercel integration.
const pick = (env: Record<string, string>, names: string[]) => names.map((n) => env[n]).find((v) => v?.trim())?.trim() ?? ''

export default defineConfig(({ mode }) => {
  const env = { ...loadEnv(mode, process.cwd(), ''), ...process.env } as Record<string, string>
  const url = pick(env, ['VITE_SUPABASE_URL', 'SUPABASE_URL', 'NEXT_PUBLIC_SUPABASE_URL'])
  const key = pick(env, [
    'VITE_SUPABASE_ANON_KEY',
    'SUPABASE_ANON_KEY',
    'NEXT_PUBLIC_SUPABASE_ANON_KEY',
    'SUPABASE_PUBLISHABLE_KEY',
    'NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY',
  ])

  return {
    // base './' keeps the build working on any static host (GitHub Pages sub-path, Vercel, Netlify).
    base: './',
    plugins: [react()],
    define: {
      __SUPABASE_URL__: JSON.stringify(url.replace(/\/+$/, '').replace(/\/rest\/v1$/, '')),
      __SUPABASE_ANON_KEY__: JSON.stringify(key),
    },
  }
})
