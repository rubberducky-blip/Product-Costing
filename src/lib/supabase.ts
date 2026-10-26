import { createClient } from '@supabase/supabase-js'

// injected at build time by vite.config.ts
const url = __SUPABASE_URL__
const key = __SUPABASE_ANON_KEY__

export const supabaseConfigured = Boolean(url && key)

export const supabase = createClient(url || 'http://localhost', key || 'missing-key', {
  auth: { persistSession: true, autoRefreshToken: true },
})

/** Maps a Supabase/Postgres error to an i18n error key where we recognise it. */
export function errorKey(err: { message?: string; code?: string } | null | undefined): string {
  if (!err) return 'err.generic'
  const m = err.message ?? ''
  if (m.includes('conflict')) return 'err.conflict'
  if (m.includes('in_use') || err.code === '23503') return 'err.inUse'
  if (m.includes('invalid_material')) return 'err.invalidMaterial'
  if (m.includes('invalid_backup')) return 'err.invalidBackup'
  if (m.includes('not_found')) return 'err.notFound'
  if (err.code === '23505') return 'err.duplicate'
  if (m.includes('Invalid login credentials')) return 'err.login'
  if (m.includes('Email not confirmed')) return 'err.emailNotConfirmed'
  if (m.includes('Failed to fetch') || m.includes('NetworkError')) return 'err.network'
  return 'err.generic'
}
