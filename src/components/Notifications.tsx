import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { markupForPrice } from '../lib/calc'
import { useData } from '../lib/data'
import { dateTime, money, pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import { errorKey, supabase } from '../lib/supabase'
import type { PriceNotification } from '../lib/types'

interface Adjustment {
  id: string
  markup_pct: number
  price: number
  notifications: number[]
}

const same = (a: number, b: number) => Math.abs(a - b) < 0.005

/** Bell in the top bar listing price changes caused by other edits (newest first). */
export function NotificationBell() {
  const { t, lang } = useI18n()
  const { email } = useAuth()
  const { notifications, itemMap, costs, settings, markSeen, hideNotifications, reload } = useData()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const ref = useRef<HTMLDivElement>(null)
  const nav = useNavigate()
  const cur = settings?.currency ?? '€'

  const visible = notifications.filter((n) => itemMap.has(n.item_id) && !(n.hidden_by ?? []).includes(email))
  const list = visible.slice(0, 50)
  const unread = visible.filter((n) => !n.seen_by.includes(email)).map((n) => n.id)

  /** The markup that brings the item back to `target`, or null when it is already there or cannot be computed. */
  const adjustment = (itemId: string, target: number, ids: number[]): Adjustment | null => {
    const c = costs.get(itemId)
    const item = itemMap.get(itemId)
    if (!c || !item || item.kind === 'raw_material' || !settings || same(c.price, target)) return null
    const m = markupForPrice(c.cost, target, c.roundingStep, settings.rounding_mode)
    return m == null ? null : { id: itemId, markup_pct: m, price: target, notifications: ids }
  }
  const forNotification = (n: PriceNotification) => (n.adjusted_at ? null : adjustment(n.item_id, n.old_price, [n.id]))

  // Margin expansion: items that now sell for less than before the changes not yet adjusted, back to that price
  const expansion: Adjustment[] = []
  const byItem = new Map<string, PriceNotification[]>()
  for (const n of visible) if (!n.adjusted_at) byItem.set(n.item_id, [...(byItem.get(n.item_id) ?? []), n])
  for (const [itemId, ns] of byItem) {
    const before = ns[ns.length - 1].old_price // oldest pending change: the price before them
    const c = costs.get(itemId)
    if (!c || c.price >= before - 0.005) continue
    const a = adjustment(itemId, before, ns.map((n) => n.id))
    if (a) expansion.push(a)
  }

  const apply = async (changes: Adjustment[]) => {
    if (!changes.length) return
    setBusy(true)
    setError(null)
    const { error } = await supabase.rpc('adjust_markups', { p_changes: changes })
    if (error) setError(t(errorKey(error)))
    await markSeen(changes.flatMap((c) => c.notifications))
    await reload(['items', 'price_notifications'])
    setBusy(false)
  }

  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [open])

  return (
    <div className="bell" ref={ref}>
      <button type="button" className="btn small ghost bell-btn" aria-label={t('notify.title')} title={t('notify.title')} onClick={() => setOpen((o) => !o)}>
        🔔
        {unread.length > 0 && <span className="bell-count">{unread.length > 99 ? '99+' : unread.length}</span>}
      </button>
      {open && (
        <div className="bell-panel">
          <div className="bell-head">
            <strong>{t('notify.title')}</strong>
            <span className="bell-actions">
              {unread.length > 0 && (
                <button type="button" className="btn small ghost" onClick={() => markSeen(unread)}>{t('notify.markAll')}</button>
              )}
              {visible.length > 0 && (
                <button type="button" className="btn small ghost" onClick={() => hideNotifications(visible.map((n) => n.id))}>{t('notify.hideAll')}</button>
              )}
            </span>
          </div>
          {expansion.length > 0 && (
            <button
              type="button"
              className="btn small expansion"
              disabled={busy}
              title={t('notify.expansionHelp')}
              onClick={() => confirm(t('notify.expansionConfirm', { count: expansion.length })) && apply(expansion)}
            >
              📈 {t('notify.expansion', { count: expansion.length })}
            </button>
          )}
          {error && <p className="small warn-text">{error}</p>}
          {list.length === 0 && <p className="muted small">{t('notify.empty')}</p>}
          <ul>
            {list.map((n) => {
              const item = itemMap.get(n.item_id)!
              const up = n.new_price > n.old_price
              const seen = n.seen_by.includes(email)
              const adj = forNotification(n)
              return (
                <li
                  key={n.id}
                  className={seen ? '' : 'unread'}
                  onClick={() => {
                    markSeen([n.id])
                    setOpen(false)
                    nav(`/item/${item.id}`)
                  }}
                >
                  <span className={`trend ${up ? 'up' : 'down'}`} aria-hidden>{up ? '▲' : '▼'}</span>
                  <span className="bell-body">
                    <span>
                      <strong>{item.code}</strong> {item.name && <span className="muted">{item.name}</span>}
                    </span>
                    <span className="small">
                      {t(up ? 'notify.up' : 'notify.down')}: {money(n.old_price, cur, lang)} → <strong>{money(n.new_price, cur, lang)}</strong>
                      {' '}({up ? '+' : '−'}{money(Math.abs(n.new_price - n.old_price), cur, lang)})
                    </span>
                    <span className="muted small">{dateTime(n.created_at, lang)}</span>
                    {n.adjusted_at ? (
                      <span className="small ok-text">✓ {t('notify.adjusted')}</span>
                    ) : adj && (
                      <span>
                        <button
                          type="button"
                          className="btn small"
                          disabled={busy}
                          title={t('notify.adjustHelp', { markup: pct(adj.markup_pct, lang), price: money(n.old_price, cur, lang) })}
                          onClick={(e) => {
                            e.stopPropagation()
                            apply([adj])
                          }}
                        >
                          {t('notify.adjust')} → {pct(adj.markup_pct, lang)}
                        </button>
                      </span>
                    )}
                  </span>
                  <button
                    type="button"
                    className="btn small ghost bell-hide"
                    aria-label={t('notify.hide')}
                    title={t('notify.hide')}
                    onClick={(e) => {
                      e.stopPropagation()
                      hideNotifications([n.id])
                    }}
                  >
                    ✕
                  </button>
                </li>
              )
            })}
          </ul>
        </div>
      )}
    </div>
  )
}
