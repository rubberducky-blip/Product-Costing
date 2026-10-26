import { useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useData } from '../lib/data'
import { useI18n } from '../lib/i18n'

interface Result {
  key: string
  to: string
  title: string
  subtitle: string
  badge: string
  /** matching note or comment, when the match is only there */
  note?: string
}

const norm = (s: string | null | undefined) =>
  (s ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')

export function SearchBar() {
  const { t } = useI18n()
  const { items, suppliers, supplierMap, linesByProduct, laborByProduct } = useData()
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const nav = useNavigate()
  const inputRef = useRef<HTMLInputElement>(null)

  const results = useMemo<Result[]>(() => {
    const needle = norm(q.trim())
    if (!needle) return []
    const out: Result[] = []
    // a match only in notes or comments shows the matching text instead of the usual subtitle
    const noteHit = (texts: (string | null | undefined)[]) => texts.find((x) => norm(x).includes(needle)) ?? undefined
    for (const s of suppliers) {
      const note = noteHit([s.notes])
      if (norm(s.name).includes(needle) || norm(s.country).includes(needle) || note) {
        out.push({ key: `s${s.id}`, to: `/supplier/${s.id}`, title: s.name, subtitle: s.country ?? '', note, badge: t('nav.suppliers') })
      }
    }
    for (const i of items) {
      const sup = i.supplier_id ? supplierMap.get(i.supplier_id)?.name : ''
      const main = [i.code, i.name, i.supplier_code, sup].some((f) => norm(f).includes(needle))
      const note = main
        ? undefined
        : noteHit([i.notes, ...(linesByProduct.get(i.id) ?? []).map((l) => l.comment), ...(laborByProduct.get(i.id) ?? []).map((l) => l.comment)])
      if (main || note) {
        out.push({
          key: `i${i.id}`,
          to: `/item/${i.id}`,
          title: i.code,
          subtitle: [i.name, sup].filter(Boolean).join(' · '),
          note,
          badge: t(`kind.${i.kind}`),
        })
      }
    }
    // exact / prefix code matches first
    out.sort(
      (a, b) =>
        Number(!norm(a.title).startsWith(needle)) - Number(!norm(b.title).startsWith(needle)) ||
        Number(Boolean(a.note)) - Number(Boolean(b.note)),
    )
    return out.slice(0, 25)
  }, [q, items, suppliers, supplierMap, linesByProduct, laborByProduct, t])

  const go = (r: Result) => {
    nav(r.to)
    setQ('')
    setOpen(false)
    inputRef.current?.blur()
  }

  return (
    <div className="search">
      <input
        ref={inputRef}
        type="search"
        value={q}
        placeholder={t('search.placeholder')}
        onChange={(e) => {
          setQ(e.target.value)
          setOpen(true)
          setActive(0)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') setActive((a) => Math.min(a + 1, results.length - 1))
          else if (e.key === 'ArrowUp') setActive((a) => Math.max(a - 1, 0))
          else if (e.key === 'Enter' && results[active]) go(results[active])
          else if (e.key === 'Escape') setOpen(false)
        }}
      />
      {open && q.trim() && (
        <ul className="search-results">
          {results.length === 0 && <li className="muted">{t('search.none')}</li>}
          {results.map((r, idx) => (
            <li key={r.key} className={idx === active ? 'active' : ''} onMouseDown={() => go(r)}>
              <span className="badge">{r.badge}</span>
              <span className="search-title">{r.title}</span>
              <span className="muted">{r.note ? `💬 ${r.note}` : r.subtitle}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
