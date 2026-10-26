import { useMemo, useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { useData } from '../lib/data'
import { money, pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import type { ItemKind } from '../lib/types'

const KINDS: ItemKind[] = ['product', 'merchandise', 'raw_material']

export function ItemsList() {
  const { kind } = useParams()
  const { t, lang } = useI18n()
  const { items, costs, supplierMap, settings } = useData()
  const [filter, setFilter] = useState('')
  const nav = useNavigate()
  const cur = settings?.currency ?? '€'

  const rows = useMemo(() => {
    const f = filter.trim().toLowerCase()
    return items
      .filter((i) => i.kind === kind)
      .filter((i) => !f || i.code.toLowerCase().includes(f) || i.name.toLowerCase().includes(f))
  }, [items, kind, filter])

  if (!KINDS.includes(kind as ItemKind)) return <Navigate to="/list/product" replace />
  const k = kind as ItemKind
  const sells = k !== 'raw_material'

  return (
    <div>
      <div className="page-head">
        <h1>{t(`nav.${k}`)}</h1>
        <input className="filter" type="search" placeholder={t('common.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        <Link className="btn primary" to={`/item/new/${k}`}>+ {t('common.new')}</Link>
      </div>
      <p className="muted small">{t(`kindHelp.${k}`)}</p>
      {rows.length === 0 ? (
        <p className="muted">{t('common.empty')}</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>{t('item.code')}</th>
              <th>{t('item.name')}</th>
              {k !== 'product' && <th className="hide-sm">{t('list.supplier')}</th>}
              <th className="num">{sells ? t('list.cost') : t('list.unitCost')}</th>
              {sells && <th className="num">{t('list.price')}</th>}
              {sells && <th className="num hide-sm">{t('list.margin')}</th>}
            </tr>
          </thead>
          <tbody>
            {rows.map((i) => {
              const c = costs.get(i.id)
              return (
                <tr key={i.id} onClick={() => nav(`/item/${i.id}`)} className="clickable">
                  <td>
                    <Link to={`/item/${i.id}`} onClick={(e) => e.stopPropagation()}>{i.code}</Link>
                    {i.kind === 'merchandise' && i.usable_as_material && <span className="badge" title={t('item.usableAsMaterial')}>🧵</span>}
                    {c && c.warnings.length > 0 && <span className="badge warn">!</span>}
                  </td>
                  <td>{i.name}</td>
                  {k !== 'product' && <td className="hide-sm">{i.supplier_id ? supplierMap.get(i.supplier_id)?.name : ''}</td>}
                  <td className="num">
                    {money(c?.cost, cur, lang, sells ? 2 : 4)}
                    {!sells && <span className="muted"> / {t(`unit.${i.unit}`)}</span>}
                  </td>
                  {sells && <td className="num strong">{money(c?.price, cur, lang)}</td>}
                  {sells && <td className="num hide-sm">{pct(c?.marginPct, lang)}</td>}
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
