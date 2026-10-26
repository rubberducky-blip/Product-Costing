import { useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useData } from '../lib/data'
import { folderAndDescendants, folderOptions, folderPath, type Option } from '../lib/folders'
import { useI18n } from '../lib/i18n'
import { errorKey, supabase } from '../lib/supabase'
import type { Folder, FolderScope } from '../lib/types'
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

export function Breadcrumbs({ rootLabel, folderId, extra, onOpen }: {
  rootLabel: string
  folderId: string | null
  /** a trailing crumb that is not a real folder (e.g. merchandise inside raw materials) */
  extra?: string
  onOpen: (id: string | null) => void
}) {
  const { folderMap } = useData()
  const path = folderPath(folderMap, folderId)
  if (!path.length && !extra) return <h1>{rootLabel}</h1>
  return (
    <h1 className="crumbs">
      <button type="button" className="crumb" onClick={() => onOpen(null)}>{rootLabel}</button>
      {path.map((f, i) => (
        <span key={f.id}>
          <span className="muted"> / </span>
          {i === path.length - 1 && !extra ? <span>{f.name}</span> : <button type="button" className="crumb" onClick={() => onOpen(f.id)}>{f.name}</button>}
        </span>
      ))}
      {extra && (
        <span>
          <span className="muted"> / </span>
          <span>{extra}</span>
        </span>
      )}
    </h1>
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
