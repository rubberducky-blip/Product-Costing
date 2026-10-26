import { useMemo, useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { Breadcrumbs, FolderDialog, useFolderParam } from '../components/Folders'
import { useData } from '../lib/data'
import { childFolders, folderAndDescendants, norm, pathText } from '../lib/folders'
import { money, pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import type { Folder, ItemKind } from '../lib/types'

const KINDS: ItemKind[] = ['product', 'merchandise', 'raw_material']
/** Virtual folder inside raw materials that holds the merchandise also used as raw material. */
const MERCH = 'merch'

export function ItemsList() {
  const { kind } = useParams()
  const { t, lang } = useI18n()
  const { items, folders, folderMap, costs, supplierMap, settings } = useData()
  const [filter, setFilter] = useState('')
  const [folderId, openFolder] = useFolderParam()
  const [dialog, setDialog] = useState<null | { folder?: Folder }>(null)
  const nav = useNavigate()
  const cur = settings?.currency ?? '€'
  const k = kind as ItemKind
  const inMerch = k === 'raw_material' && folderId === MERCH
  const current = folderId && !inMerch ? folderMap.get(folderId) : undefined
  const parentId = current && current.scope === k ? current.id : null

  // raw materials also list the merchandise that is used as raw material
  const merchMaterials = useMemo(() => items.filter((i) => k === 'raw_material' && i.kind === 'merchandise' && i.usable_as_material), [items, k])
  const own = useMemo(() => items.filter((i) => i.kind === k), [items, k])

  const subfolders = useMemo(() => (inMerch ? [] : childFolders(folders, k, parentId)), [folders, k, parentId, inMerch])
  const counts = useMemo(() => {
    const m = new Map<string, number>()
    for (const f of subfolders) {
      const ids = folderAndDescendants(folders, f.id)
      m.set(f.id, own.filter((i) => i.folder_id && ids.has(i.folder_id)).length)
    }
    return m
  }, [subfolders, folders, own])

  const f = norm(filter.trim())
  const rows = useMemo(() => {
    if (f) return [...own, ...merchMaterials].filter((i) => norm(i.code).includes(f) || norm(i.name).includes(f))
    if (inMerch) return merchMaterials
    const known = new Set(folders.map((x) => x.id))
    return own.filter((i) => (i.folder_id && known.has(i.folder_id) ? i.folder_id : null) === parentId)
  }, [f, own, merchMaterials, inMerch, folders, parentId])

  if (!KINDS.includes(kind as ItemKind)) return <Navigate to="/list/product" replace />
  const sells = k !== 'raw_material'
  const showMerchFolder = !f && k === 'raw_material' && !folderId && merchMaterials.length > 0
  const cols = 3 + (k !== 'product' ? 1 : 0) + (sells ? 2 : 0)
  const empty = rows.length === 0 && (f || (subfolders.length === 0 && !showMerchFolder))
  const showPath = Boolean(f) || inMerch

  return (
    <div>
      <div className="page-head">
        <Breadcrumbs rootLabel={t(`nav.${k}`)} folderId={parentId} extra={inMerch ? t('nav.merchandise') : undefined} onOpen={openFolder} />
        <input className="filter" type="search" placeholder={t('common.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        {!inMerch && <button type="button" className="btn" onClick={() => setDialog({})}>+ {t('folder.new')}</button>}
        <Link className="btn primary" to={`/item/new/${k}${parentId ? `?folder=${parentId}` : ''}`}>+ {t('common.new')}</Link>
      </div>
      {empty ? (
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
            {!f && subfolders.map((fo) => (
              <tr key={fo.id} className="clickable folder-row" onClick={() => openFolder(fo.id)}>
                <td colSpan={cols - 1}>
                  📁 <span className="strong">{fo.name}</span> <span className="muted small">({counts.get(fo.id) ?? 0})</span>
                </td>
                <td className="row-actions">
                  <button type="button" className="btn small ghost" aria-label={t('common.edit')} title={t('folder.edit')} onClick={(e) => { e.stopPropagation(); setDialog({ folder: fo }) }}>✎</button>
                </td>
              </tr>
            ))}
            {showMerchFolder && (
              <tr className="clickable folder-row" onClick={() => openFolder(MERCH)}>
                <td colSpan={cols}>
                  📦 <span className="strong">{t('nav.merchandise')}</span> <span className="muted small">({merchMaterials.length})</span>
                </td>
              </tr>
            )}
            {rows.map((i) => {
              const c = costs.get(i.id)
              const path = showPath ? pathText(folderMap, i.folder_id) : ''
              return (
                <tr key={i.id} onClick={() => nav(`/item/${i.id}`)} className="clickable">
                  <td>
                    <Link to={`/item/${i.id}`} onClick={(e) => e.stopPropagation()}>{i.code}</Link>
                    {i.kind === 'merchandise' && i.usable_as_material && (
                      <span className="badge" title={t('item.usableAsMaterial')}>{k === 'raw_material' ? t('kind.merchandise') : '🧵'}</span>
                    )}
                    {c && c.warnings.length > 0 && <span className="badge warn">!</span>}
                  </td>
                  <td>
                    {i.name}
                    {path && <div className="muted small">📁 {path}</div>}
                  </td>
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
      {dialog && (
        <FolderDialog
          scope={k}
          folder={dialog.folder}
          parentId={parentId}
          onClose={(deleted) => {
            if (deleted && dialog.folder?.id === parentId) openFolder(dialog.folder?.parent_id ?? null)
            setDialog(null)
          }}
        />
      )}
    </div>
  )
}
