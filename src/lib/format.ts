export function money(v: number | null | undefined, currency: string, lang: string, digits = 2): string {
  if (v == null || !Number.isFinite(v)) return '—'
  const n = new Intl.NumberFormat(lang === 'el' ? 'el-GR' : 'en-GB', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(v)
  return `${n} ${currency}`
}

export function pct(v: number | null | undefined, lang: string): string {
  if (v == null || !Number.isFinite(v)) return '—'
  return `${new Intl.NumberFormat(lang === 'el' ? 'el-GR' : 'en-GB', { maximumFractionDigits: 1 }).format(v)}%`
}

export function dateTime(v: string, lang: string): string {
  return new Date(v).toLocaleString(lang === 'el' ? 'el-GR' : 'en-GB', { dateStyle: 'short', timeStyle: 'short' })
}

/** Parses user input that may use a comma as decimal separator. Empty → null. */
export function parseNum(v: string): number | null {
  const t = v.trim().replace(',', '.')
  if (t === '') return null
  const n = Number(t)
  return Number.isFinite(n) ? n : null
}

/** Seconds as a short duration, e.g. 45″, 2′ 05″, 1h 02′ 05″. */
export function duration(seconds: number | null | undefined): string {
  const s = Math.max(0, Math.round(seconds ?? 0))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = String(s % 60).padStart(2, '0')
  if (h) return `${h}h ${String(m).padStart(2, '0')}′ ${r}″`
  if (m) return `${m}′ ${r}″`
  return `${s}″`
}
