import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { norm, type Option } from '../lib/folders'
import { useI18n } from '../lib/i18n'

interface Props {
  value: string
  options: Option[]
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  /** shown when value is not among the options (e.g. a material that is no longer usable) */
  fallbackLabel?: string
}

/**
 * A select you can type into: typing filters the options (codes, names and folder names).
 * Without a query the options keep their folder layout; while searching each match shows its folder.
 */
export function Combobox({ value, options, onChange, placeholder, className, fallbackLabel }: Props) {
  const { t } = useI18n()
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [pos, setPos] = useState<{ left: number; width: number; top?: number; bottom?: number; maxHeight: number } | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)

  const selected = options.find((o) => o.value === value && !o.header)
  const selectedLabel = selected ? `${selected.label}${selected.sub ? ` — ${selected.sub}` : ''}` : value ? fallbackLabel ?? '' : ''

  const shown = useMemo(() => {
    const needle = norm(query.trim())
    if (!needle) return options
    return options.filter((o) => !o.header && norm(`${o.label} ${o.sub ?? ''} ${o.path ?? ''}`).includes(needle))
  }, [options, query])
  const pickable = shown.filter((o) => !o.header)

  // the list is position: fixed so cards with overflow do not clip it
  useLayoutEffect(() => {
    if (!open) return
    const place = () => {
      const r = inputRef.current?.getBoundingClientRect()
      if (!r) return
      const below = window.innerHeight - r.bottom - 8
      const above = r.top - 8
      const width = Math.max(r.width, 260)
      const left = Math.max(8, Math.min(r.left, window.innerWidth - width - 8))
      if (below >= 220 || below >= above) setPos({ left, width, top: r.bottom + 4, maxHeight: Math.min(360, below) })
      else setPos({ left, width, bottom: window.innerHeight - r.top + 4, maxHeight: Math.min(360, above) })
    }
    place()
    window.addEventListener('resize', place)
    window.addEventListener('scroll', place, true)
    return () => {
      window.removeEventListener('resize', place)
      window.removeEventListener('scroll', place, true)
    }
  }, [open])

  useEffect(() => {
    listRef.current?.querySelector('.active')?.scrollIntoView({ block: 'nearest' })
  }, [active, open])

  const openList = () => {
    setOpen(true)
    setQuery('')
    setActive(Math.max(0, options.filter((o) => !o.header).findIndex((o) => o.value === value)))
  }

  const pick = (o: Option) => {
    onChange(o.value)
    setOpen(false)
    setQuery('')
  }

  let idx = -1
  return (
    <div className={`combo${className ? ` ${className}` : ''}`}>
      <input
        ref={inputRef}
        value={open ? query : selectedLabel}
        placeholder={open ? selectedLabel || placeholder : placeholder}
        onFocus={openList}
        onClick={() => !open && openList()}
        onBlur={() => setOpen(false)}
        onChange={(e) => {
          setQuery(e.target.value)
          setActive(0)
          setOpen(true)
        }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') {
            e.preventDefault()
            if (!open) openList()
            else setActive((a) => Math.min(a + 1, pickable.length - 1))
          } else if (e.key === 'ArrowUp') {
            e.preventDefault()
            setActive((a) => Math.max(a - 1, 0))
          } else if (e.key === 'Enter') {
            if (open) {
              e.preventDefault()
              if (pickable[active]) pick(pickable[active])
            }
          } else if (e.key === 'Escape') {
            setOpen(false)
          }
        }}
        role="combobox"
        aria-expanded={open}
        autoComplete="off"
      />
      <span className="combo-arrow" aria-hidden>▾</span>
      {open && pos && (
        <ul ref={listRef} className="combo-list" style={{ position: 'fixed', ...pos }} role="listbox">
          {pickable.length === 0 && <li className="muted combo-empty">{t('search.none')}</li>}
          {shown.map((o) => {
            if (o.header) {
              return (
                <li key={o.value} className="combo-folder" style={{ paddingLeft: 10 + (o.depth ?? 0) * 16 }}>
                  📁 {o.label}
                </li>
              )
            }
            const i = ++idx
            return (
              <li
                key={o.value}
                role="option"
                aria-selected={o.value === value}
                className={`${i === active ? 'active' : ''}${o.value === value ? ' selected' : ''}`}
                style={{ paddingLeft: 10 + (query ? 0 : (o.depth ?? 0) * 16) }}
                onMouseDown={(e) => {
                  e.preventDefault()
                  pick(o)
                }}
                onMouseEnter={() => setActive(i)}
              >
                <span>{o.label}</span>
                {o.sub && <span className="muted"> — {o.sub}</span>}
                {query && o.path && <span className="combo-path">📁 {o.path}</span>}
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
