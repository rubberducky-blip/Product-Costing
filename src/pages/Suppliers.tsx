import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Breadcrumbs, FolderDialog, useFolderParam } from '../components/Folders'
import { useData } from '../lib/data'
import { childFolders, folderAndDescendants, norm, pathText } from '../lib/folders'
import { pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import type { Folder } from '../lib/types'

export function Suppliers() {
  const { t, lang } = useI18n()
  const { suppliers, items, folders, folderMap } = useData()
  const [filter, setFilter] = useState('')
  const [folderId, openFolder] = useFolderParam()
  const [dialog, setDialog] = useState<null | { folder?: Folder }>(null)
  const nav = useNavigate()
  const current = folderId ? folderMap.get(folderId) : undefined
  const parentId = current?.scope === 'supplier' ? current.id : null

  const counts = useMemo(() => {
    const m = new Map<string, number>()
    for (const i of items) if (i.supplier_id) m.set(i.supplier_id, (m.get(i.supplier_id) ?? 0) + 1)
    return m
  }, [items])

  const subfolders = childFolders(folders, 'supplier', parentId)
  const folderCount = (id: string) => {
    const ids = folderAndDescendants(folders, id)
    return suppliers.filter((s) => s.folder_id && ids.has(s.folder_id)).length
  }
  const f = norm(filter.trim())
  const known = new Set(folders.map((x) => x.id))
  const rows = f
    ? suppliers.filter((s) => norm(s.name).includes(f) || norm(s.country).includes(f))
    : suppliers.filter((s) => (s.folder_id && known.has(s.folder_id) ? s.folder_id : null) === parentId)

  return (
    <div>
      <div className="page-head">
        <Breadcrumbs rootLabel={t('nav.suppliers')} folderId={parentId} onOpen={openFolder} />
        <input className="filter" type="search" placeholder={t('common.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        <button type="button" className="btn" onClick={() => setDialog({})}>+ {t('folder.new')}</button>
        <Link className="btn primary" to={`/supplier/new${parentId ? `?folder=${parentId}` : ''}`}>+ {t('common.new')}</Link>
      </div>
      {rows.length === 0 && (f || subfolders.length === 0) ? (
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
            {!f && subfolders.map((fo) => (
              <tr key={fo.id} className="clickable folder-row" onClick={() => openFolder(fo.id)}>
                <td colSpan={4}>
                  📁 <span className="strong">{fo.name}</span> <span className="muted small">({folderCount(fo.id)})</span>
                </td>
                <td className="row-actions">
                  <button type="button" className="btn small ghost" aria-label={t('common.edit')} title={t('folder.edit')} onClick={(e) => { e.stopPropagation(); setDialog({ folder: fo }) }}>✎</button>
                </td>
              </tr>
            ))}
            {rows.map((s) => (
              <tr key={s.id} className="clickable" onClick={() => nav(`/supplier/${s.id}`)}>
                <td>
                  <Link to={`/supplier/${s.id}`} onClick={(e) => e.stopPropagation()}>{s.name}</Link>
                  {f && s.folder_id && <div className="muted small">📁 {pathText(folderMap, s.folder_id)}</div>}
                </td>
                <td>{s.country}</td>
                <td className="num">{pct(s.default_discount_pct, lang)}</td>
                <td className="num">{pct(s.shipping_pct, lang)}</td>
                <td className="num hide-sm">{counts.get(s.id) ?? 0}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {dialog && (
        <FolderDialog
          scope="supplier"
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
