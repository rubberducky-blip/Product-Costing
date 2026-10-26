import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { useData } from '../lib/data'
import { dateTime, money } from '../lib/format'
import { useI18n } from '../lib/i18n'

/** Bell in the top bar listing price changes caused by other edits (newest first). */
export function NotificationBell() {
  const { t, lang } = useI18n()
  const { email } = useAuth()
  const { notifications, itemMap, settings, markSeen } = useData()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const nav = useNavigate()
  const cur = settings?.currency ?? '€'

  const list = notifications.filter((n) => itemMap.has(n.item_id)).slice(0, 50)
  const unread = notifications.filter((n) => itemMap.has(n.item_id) && !n.seen_by.includes(email)).map((n) => n.id)

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
            {unread.length > 0 && (
              <button type="button" className="btn small ghost" onClick={() => markSeen(unread)}>{t('notify.markAll')}</button>
            )}
          </div>
          {list.length === 0 && <p className="muted small">{t('notify.empty')}</p>}
          <ul>
            {list.map((n) => {
              const item = itemMap.get(n.item_id)!
              const up = n.new_price > n.old_price
              const seen = n.seen_by.includes(email)
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
                  </span>
                </li>
              )
            })}
          </ul>
        </div>
      )}
    </div>
  )
}
