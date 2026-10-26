import { useMemo, useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { SettingsMenu } from '../components/Arrange'
import { Breadcrumbs, EntryRow, FolderDialog, FolderTiles, useFolderView } from '../components/Folders'
import { Banner } from '../components/ui'
import { useData } from '../lib/data'
import { listFolderOf, norm, pathText } from '../lib/folders'
import { money, pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import type { Folder, Item, ItemKind } from '../lib/types'

const KINDS: ItemKind[] = ['product', 'merchandise', 'raw_material']

export function ItemsList() {
  const { kind } = useParams()
  const { t, lang } = useI18n()
  const { items, folderMap, costs, supplierMap, settings, unseen, markSeen } = useData()
  const [filter, setFilter] = useState('')
  const [dialog, setDialog] = useState<null | { folder?: Folder }>(null)
  const [error, setError] = useState<string | null>(null)
  const nav = useNavigate()
  const cur = settings?.currency ?? '€'
  const k = (KINDS.includes(kind as ItemKind) ? kind : 'product') as ItemKind

  // raw materials also list the merchandise that is used as raw material, in its own raw-materials folder
  const entries = useMemo(
    () => items.filter((i) => i.kind === k || (k === 'raw_material' && i.kind === 'merchandise' && i.usable_as_material)),
    [items, k],
  )
  const view = useFolderView<Item>(k, entries, (i) => listFolderOf(i, k), (i) => i.code, setError)

  const f = norm(filter.trim())
  const rows = f ? entries.filter((i) => norm(i.code).includes(f) || norm(i.name).includes(f)) : view.here

  if (!KINDS.includes(kind as ItemKind)) return <Navigate to="/list/product" replace />
  const sells = k !== 'raw_material'

  return (
    <div>
      <div className="page-head">
        <Breadcrumbs rootLabel={t(`nav.${k}`)} folderId={view.parentId} onOpen={view.openFolder} onDropInto={view.moveInto} />
        <SettingsMenu scope={k} folder={view.folder} onEditFolder={() => setDialog({ folder: view.folder })} />
        <input className="filter" type="search" placeholder={t('common.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        <button type="button" className="btn" onClick={() => setDialog({})}>+ {t('folder.new')}</button>
        <Link className="btn primary" to={`/item/new/${k}${view.parentId ? `?folder=${view.parentId}` : ''}`}>+ {t('common.new')}</Link>
      </div>
      {error && <Banner kind="error">{error}</Banner>}
      {!f && <FolderTiles view={view} />}
      {rows.length === 0 ? (
        (f || view.subfolders.length === 0) && <p className="muted">{t('common.empty')}</p>
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
              const path = f ? pathText(folderMap, listFolderOf(i, k)) : ''
              const fresh = sells ? unseen.get(i.id) : undefined
              return (
                <EntryRow
                  key={i.id}
                  id={i.id}
                  view={view}
                  draggable={!f}
                  className="clickable"
                  onClick={() => nav(`/item/${i.id}`)}
                  onMouseEnter={() => fresh && markSeen(fresh)}
                >
                  <td>
                    {fresh && <span className="price-dot" title={t('notify.changed')} />}
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
                </EntryRow>
              )
            })}
          </tbody>
        </table>
      )}
      {dialog && (
        <FolderDialog
          scope={k}
          folder={dialog.folder}
          parentId={view.parentId}
          onClose={(deleted) => {
            if (deleted && dialog.folder?.id === view.parentId) view.openFolder(dialog.folder?.parent_id ?? null)
            setDialog(null)
          }}
        />
      )}
    </div>
  )
}
