import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useData } from '../lib/data'
import { pct } from '../lib/format'
import { useI18n } from '../lib/i18n'

export function Suppliers() {
  const { t, lang } = useI18n()
  const { suppliers, items } = useData()
  const [filter, setFilter] = useState('')
  const nav = useNavigate()

  const counts = useMemo(() => {
    const m = new Map<string, number>()
    for (const i of items) if (i.supplier_id) m.set(i.supplier_id, (m.get(i.supplier_id) ?? 0) + 1)
    return m
  }, [items])

  const f = filter.trim().toLowerCase()
  const rows = suppliers.filter((s) => !f || s.name.toLowerCase().includes(f) || (s.country ?? '').toLowerCase().includes(f))

  return (
    <div>
      <div className="page-head">
        <h1>{t('nav.suppliers')}</h1>
        <input className="filter" type="search" placeholder={t('common.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        <Link className="btn primary" to="/supplier/new">+ {t('common.new')}</Link>
      </div>
      {rows.length === 0 ? (
        <p className="muted">{t('common.empty')}</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>{t('supplier.name')}</th>
              <th>{t('list.country')}</th>
              <th className="num">{t('list.discount')}</th>
              <th className="num">{t('list.shipping')}</th>
              <th className="num hide-sm">{t('list.items')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((s) => (
              <tr key={s.id} className="clickable" onClick={() => nav(`/supplier/${s.id}`)}>
                <td><Link to={`/supplier/${s.id}`} onClick={(e) => e.stopPropagation()}>{s.name}</Link></td>
                <td>{s.country}</td>
                <td className="num">{pct(s.default_discount_pct, lang)}</td>
                <td className="num">{pct(s.shipping_pct, lang)}</td>
                <td className="num hide-sm">{counts.get(s.id) ?? 0}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
