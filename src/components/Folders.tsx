import { useState, type FormEvent, type HTMLAttributes, type ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useData } from '../lib/data'
import { arrange, childFolders, folderAndDescendants, folderOptions, folderPath, type Option } from '../lib/folders'
import { useI18n } from '../lib/i18n'
import { errorKey, supabase } from '../lib/supabase'
import type { Folder, FolderScope } from '../lib/types'
import { dragProps, placed, useArrangement, useArranger, useDropTarget, zones, type Dragged } from './Arrange'
import { Combobox } from './Combobox'
import { Banner, Field } from './ui'

/** The folder being browsed, kept in the URL (?f=<id>) so back/forward and links work. */
export function useFolderParam(): [string | null, (id: string | null) => void] {
  const [params, setParams] = useSearchParams()
  const set = (id: string | null) => setParams(id ? { f: id } : {})
  return [params.get('f'), set]
}

/** Options for a "folder" field: the top level first, then every folder of the list. */
export function useFolderFieldOptions(scope: FolderScope, exclude?: Set<string>): Option[] {
  const { t } = useI18n()
  const { folders } = useData()
  return [{ value: '', label: t('folder.root') }, ...folderOptions(folders, scope, exclude)]
}

/** Breadcrumbs of the folder being browsed; dropping on a crumb moves the dragged entry or folder there. */
export function Breadcrumbs({ rootLabel, folderId, onOpen, onDropInto }: {
  rootLabel: string
  folderId: string | null
  onOpen: (id: string | null) => void
  onDropInto?: (d: Dragged, target: string | null) => void
}) {
  const { folderMap } = useData()
  const path = folderPath(folderMap, folderId)
  if (!path.length) return <h1>{rootLabel}</h1>
  return (
    <h1 className="crumbs">
      <Crumb label={rootLabel} target={null} onOpen={onOpen} onDropInto={onDropInto} />
      {path.map((f, i) => (
        <span key={f.id}>
          <span className="muted"> / </span>
          {i === path.length - 1 ? <span>{f.name}</span> : <Crumb label={f.name} target={f.id} onOpen={onOpen} onDropInto={onDropInto} />}
        </span>
      ))}
    </h1>
  )
}

function Crumb({ label, target, onOpen, onDropInto }: {
  label: string
  target: string | null
  onOpen: (id: string | null) => void
  onDropInto?: (d: Dragged, target: string | null) => void
}) {
  const { side, handlers } = useDropTarget(() => Boolean(onDropInto), (d) => onDropInto?.(d, target))
  return (
    <button type="button" className={`crumb${side ? ' drop-into' : ''}`} onClick={() => onOpen(target)} {...handlers}>
      {label}
    </button>
  )
}

/**
 * State of a list page that browses folders: the open folder, its sub-folders and entries in display order,
 * counts per folder, and drag-and-drop actions.
 */
export function useFolderView<T extends { id: string }>(
  scope: FolderScope,
  entries: T[],
  folderOf: (e: T) => string | null,
  key: (e: T) => string,
  onError: (text: string) => void,
) {
  const { folders, folderMap } = useData()
  const [folderId, openFolder] = useFolderParam()
  const current = folderId ? folderMap.get(folderId) : undefined
  const folder = current?.scope === scope ? current : undefined
  const parentId = folder?.id ?? null
  const order = useArrangement(scope)
  const manual = order.mode(parentId) === 'manual'
  const known = new Set(folders.filter((f) => f.scope === scope).map((f) => f.id))
  const placeOf = (e: T) => {
    const f = folderOf(e)
    return f && known.has(f) ? f : null
  }

  const subfolders = arrange(childFolders(folders, scope, parentId), order.mode(parentId), (f) => order.pos(f.id), (f) => f.name)
  const here = arrange(entries.filter((e) => placeOf(e) === parentId), order.mode(parentId), (e) => order.pos(e.id), key)
  const count = (id: string) => {
    const ids = folderAndDescendants(folders, id)
    return entries.filter((e) => {
      const f = placeOf(e)
      return f !== null && ids.has(f)
    }).length
  }

  const { reorder, move } = useArranger(scope, onError)
  /** entries of a folder in display order, to append a moved entry after them */
  const entriesIn = (target: string | null, type: Dragged['type']) =>
    type === 'folder'
      ? arrange(childFolders(folders, scope, target), order.mode(target), (f) => order.pos(f.id), (f) => f.name).map((f) => f.id)
      : arrange(entries.filter((e) => placeOf(e) === target), order.mode(target), (e) => order.pos(e.id), key).map((e) => e.id)

  const moveInto = (d: Dragged, target: string | null) => move(d, target, entriesIn(target, d.type))
  const canMoveInto = (d: Dragged, target: string | null) =>
    d.type === 'folder'
      ? d.id !== target && !(target && folderAndDescendants(folders, d.id).has(target)) && folderMap.get(d.id)?.parent_id !== target
      : entries.some((e) => e.id === d.id && placeOf(e) !== target)

  const reorderFolders = (d: Dragged, target: string, side: 'before' | 'after') =>
    reorder(placed(subfolders.map((f) => f.id), d.id, target, side))
  const reorderEntries = (d: Dragged, target: string, side: 'before' | 'after') =>
    reorder(placed(here.map((e) => e.id), d.id, target, side))

  return {
    folder, parentId, openFolder, manual, subfolders, here, count, moveInto, canMoveInto, reorderFolders, reorderEntries,
  }
}

/** Folders shown as tiles above the table. Drop on a tile to move into it; on its edges to re-arrange (manual sort). */
export function FolderTiles({ view }: { view: ReturnType<typeof useFolderView<{ id: string }>> }) {
  if (!view.subfolders.length) return null
  return (
    <div className="folder-tiles">
      {view.subfolders.map((f) => <FolderTile key={f.id} folder={f} view={view} />)}
    </div>
  )
}

function FolderTile({ folder, view }: { folder: Folder; view: ReturnType<typeof useFolderView<{ id: string }>> }) {
  const { side, handlers } = useDropTarget(
    (d, s) => (s === 'into' ? view.canMoveInto(d, folder.id) : view.manual && d.type === 'folder' && d.id !== folder.id),
    (d, s) => (s === 'into' ? view.moveInto(d, folder.id) : view.reorderFolders(d, folder.id, s)),
    zones('x', 0.22, true),
  )
  return (
    <div
      className={`folder-tile${side ? ` drop-${side}` : ''}`}
      role="button"
      tabIndex={0}
      onClick={() => view.openFolder(folder.id)}
      onKeyDown={(e) => e.key === 'Enter' && view.openFolder(folder.id)}
      {...dragProps({ type: 'folder', id: folder.id })}
      {...handlers}
    >
      <span className="folder-icon" aria-hidden>📁</span>
      <span className="folder-name">{folder.name}</span>
      <span className="muted small">{view.count(folder.id)}</span>
    </div>
  )
}

/** A table row that can be dragged into a folder, or re-arranged when the folder is sorted manually. */
export function EntryRow({ id, view, draggable, className, children, ...rest }: {
  id: string
  view: ReturnType<typeof useFolderView<{ id: string }>>
  draggable: boolean
  className?: string
  children: ReactNode
} & HTMLAttributes<HTMLTableRowElement>) {
  const { side, handlers } = useDropTarget(
    (d) => draggable && view.manual && d.type === 'entry' && d.id !== id,
    (d, s) => s !== 'into' && view.reorderEntries(d, id, s),
    zones('y', 0.5, false),
  )
  return (
    <tr className={`${className ?? ''}${side ? ` drop-${side}` : ''}${draggable ? ' draggable' : ''}`} {...rest} {...dragProps({ type: 'entry', id }, draggable)} {...handlers}>
      {children}
    </tr>
  )
}

/** Create, rename, move or delete a folder. */
export function FolderDialog({ scope, folder, parentId, onClose }: {
  scope: FolderScope
  folder?: Folder
  parentId: string | null
  onClose: (deleted?: boolean) => void
}) {
  const { t } = useI18n()
  const { folders, items, suppliers, reload } = useData()
  const [name, setName] = useState(folder?.name ?? '')
  const [parent, setParent] = useState(folder ? folder.parent_id ?? '' : parentId ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const parentOptions = useFolderFieldOptions(scope, folder ? folderAndDescendants(folders, folder.id) : undefined)

  const save = async (e: FormEvent) => {
    e.preventDefault()
    e.stopPropagation()
    if (!name.trim()) return setError(t('err.required'))
    setBusy(true)
    const payload = { scope, name: name.trim(), parent_id: parent || null }
    const res = folder
      ? await supabase.from('folders').update(payload).eq('id', folder.id).eq('version', folder.version).select('id')
      : await supabase.from('folders').insert(payload).select('id')
    setBusy(false)
    if (res.error) return setError(t(errorKey(res.error)))
    if (!res.data?.length) return setError(t('err.conflict'))
    await reload(['folders'])
    onClose()
  }

  const remove = async () => {
    if (!folder) return
    const used =
      folders.some((f) => f.parent_id === folder.id) ||
      (scope === 'supplier' ? suppliers.some((s) => s.folder_id === folder.id) : items.some((i) => i.folder_id === folder.id))
    if (used) return setError(t('folder.notEmpty'))
    if (!confirm(t('common.confirmDelete', { name: folder.name }))) return
    setBusy(true)
    const { error } = await supabase.from('folders').delete().eq('id', folder.id)
    setBusy(false)
    if (error) return setError(error.code === '23503' ? t('folder.notEmpty') : t(errorKey(error)))
    await reload(['folders'])
    onClose(true)
  }

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true">
      <form className="modal" onSubmit={save}>
        <h2>{folder ? t('folder.edit') : t('folder.new')}</h2>
        {error && <Banner kind="error">{error}</Banner>}
        <div className="fields single">
          <Field label={`${t('folder.name')} *`}>
            <input autoFocus value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label={t('folder.parent')}>
            <Combobox value={parent} options={parentOptions} onChange={setParent} />
          </Field>
        </div>
        <div className="actions" style={{ marginTop: 16 }}>
          {folder && <button type="button" className="btn danger ghost" disabled={busy} onClick={remove} style={{ marginRight: 'auto' }}>{t('common.delete')}</button>}
          <button type="button" className="btn" onClick={() => onClose()}>{t('common.cancel')}</button>
          <button className="btn primary" disabled={busy}>{busy ? t('common.saving') : t('common.save')}</button>
        </div>
      </form>
    </div>
  )
}
