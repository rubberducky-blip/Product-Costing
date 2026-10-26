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
