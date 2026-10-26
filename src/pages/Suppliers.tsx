import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { SettingsMenu } from '../components/Arrange'
import { Breadcrumbs, EntryRow, FolderDialog, FolderTiles, useFolderView } from '../components/Folders'
import { Banner } from '../components/ui'
import { useData } from '../lib/data'
import { norm, pathText } from '../lib/folders'
import { pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import type { Folder, Supplier } from '../lib/types'

export function Suppliers() {
  const { t, lang } = useI18n()
  const { suppliers, items, folderMap } = useData()
  const [filter, setFilter] = useState('')
  const [dialog, setDialog] = useState<null | { folder?: Folder }>(null)
  const [error, setError] = useState<string | null>(null)
  const nav = useNavigate()
  const view = useFolderView<Supplier>('supplier', suppliers, (s) => s.folder_id, (s) => s.name, setError)

  const counts = useMemo(() => {
    const m = new Map<string, number>()
    for (const i of items) if (i.supplier_id) m.set(i.supplier_id, (m.get(i.supplier_id) ?? 0) + 1)
    return m
  }, [items])

  const f = norm(filter.trim())
  const rows = f ? suppliers.filter((s) => norm(s.name).includes(f) || norm(s.country).includes(f)) : view.here

  return (
    <div>
      <div className="page-head">
        <Breadcrumbs rootLabel={t('nav.suppliers')} folderId={view.parentId} onOpen={view.openFolder} onDropInto={view.moveInto} />
        <SettingsMenu scope="supplier" folder={view.folder} onEditFolder={() => setDialog({ folder: view.folder })} />
        <input className="filter" type="search" placeholder={t('common.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        <button type="button" className="btn" onClick={() => setDialog({})}>+ {t('folder.new')}</button>
        <Link className="btn primary" to={`/supplier/new${view.parentId ? `?folder=${view.parentId}` : ''}`}>+ {t('common.new')}</Link>
      </div>
      {error && <Banner kind="error">{error}</Banner>}
      {!f && <FolderTiles view={view} />}
      {rows.length === 0 ? (
        (f || view.subfolders.length === 0) && <p className="muted">{t('common.empty')}</p>
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
              <EntryRow key={s.id} id={s.id} view={view} draggable={!f} className="clickable" onClick={() => nav(`/supplier/${s.id}`)}>
                <td>
                  <Link to={`/supplier/${s.id}`} onClick={(e) => e.stopPropagation()}>{s.name}</Link>
                  {f && s.folder_id && <div className="muted small">📁 {pathText(folderMap, s.folder_id)}</div>}
                </td>
                <td>{s.country}</td>
                <td className="num">{pct(s.default_discount_pct, lang)}</td>
                <td className="num">{pct(s.shipping_pct, lang)}</td>
                <td className="num hide-sm">{counts.get(s.id) ?? 0}</td>
              </EntryRow>
            ))}
          </tbody>
        </table>
      )}
      {dialog && (
        <FolderDialog
          scope="supplier"
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
