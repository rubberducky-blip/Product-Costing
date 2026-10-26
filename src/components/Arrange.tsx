import { useEffect, useRef, useState, type DragEvent } from 'react'
import { useData } from '../lib/data'
import { folderAndDescendants, listFolderOf, type Arrangement } from '../lib/folders'
import { useI18n } from '../lib/i18n'
import { errorKey, supabase } from '../lib/supabase'
import type { Folder, FolderScope, SortMode } from '../lib/types'

/** What is being dragged. Kept in module state because dataTransfer cannot be read during dragover. */
export interface Dragged {
  type: 'entry' | 'folder'
  id: string
}
let dragged: Dragged | null = null

export function dragProps(d: Dragged, enabled = true) {
  if (!enabled) return {}
  return {
    draggable: true,
    onDragStart: (e: DragEvent) => {
      dragged = d
      e.dataTransfer.effectAllowed = 'move'
      e.dataTransfer.setData('text/plain', d.id)
      document.body.classList.add('dragging')
    },
    onDragEnd: () => {
      dragged = null
      document.body.classList.remove('dragging')
    },
  }
}

export type DropSide = 'before' | 'after' | 'into'

/**
 * Drop target handlers. `sideOf` decides from the pointer position whether the drop re-arranges
 * (before / after) or moves into the target; return null to refuse the drop.
 */
export function useDropTarget(
  accept: (d: Dragged, side: DropSide) => boolean,
  onDrop: (d: Dragged, side: DropSide) => void,
  sideOf: (e: DragEvent) => DropSide = () => 'into',
) {
  const [side, setSide] = useState<DropSide | null>(null)
  const handlers = {
    onDragOver: (e: DragEvent) => {
      const s = sideOf(e)
      if (!dragged || !accept(dragged, s)) return setSide(null)
      e.preventDefault()
      e.dataTransfer.dropEffect = 'move'
      setSide(s)
    },
    onDragLeave: () => setSide(null),
    onDrop: (e: DragEvent) => {
      const s = sideOf(e)
      setSide(null)
      if (!dragged || !accept(dragged, s)) return
      e.preventDefault()
      const d = dragged
      dragged = null
      document.body.classList.remove('dragging')
      onDrop(d, s)
    },
  }
  return { side, handlers }
}

/** Splits an element into before / into / after zones along one axis. */
export const zones = (axis: 'x' | 'y', edge: number, allowInto: boolean) => (e: DragEvent): DropSide => {
  const r = (e.currentTarget as HTMLElement).getBoundingClientRect()
  const f = axis === 'x' ? (e.clientX - r.left) / r.width : (e.clientY - r.top) / r.height
  if (!allowInto) return f < 0.5 ? 'before' : 'after'
  return f < edge ? 'before' : f > 1 - edge ? 'after' : 'into'
}

export function useArrangement(scope: FolderScope): Arrangement {
  const { sortMode, position } = useData()
  return { mode: (folderId) => sortMode(scope, folderId), pos: (id) => position(scope, id) }
}

/** Moving and re-arranging entries (items or suppliers) and folders of one list. */
export function useArranger(scope: FolderScope, onError: (text: string) => void) {
  const { t } = useI18n()
  const { folders, itemMap, reload } = useData()

  const check = async (p: PromiseLike<{ error: { message?: string; code?: string } | null }>) => {
    const { error } = await p
    if (error) onError(t(errorKey(error)))
    return !error
  }

  const reorder = async (ids: string[]) => {
    await check(supabase.rpc('set_manual_order', { p_scope: scope, p_ids: ids }))
    await reload(['manual_order'])
  }

  /** Moves an entry or folder into `target` (null = top level), placing it last in that folder. */
  const move = async (d: Dragged, target: string | null, lastInTarget: string[]) => {
    let ok: boolean
    if (d.type === 'folder') {
      if (target && folderAndDescendants(folders, d.id).has(target)) return
      ok = await check(supabase.from('folders').update({ parent_id: target }).eq('id', d.id))
    } else if (scope === 'supplier') {
      ok = await check(supabase.from('suppliers').update({ folder_id: target }).eq('id', d.id))
    } else {
      const item = itemMap.get(d.id)
      if (!item || listFolderOf(item, scope) === target) return
      const column = scope === 'raw_material' && item.kind === 'merchandise' ? 'material_folder_id' : 'folder_id'
      ok = await check(supabase.from('items').update({ [column]: target }).eq('id', d.id))
    }
    if (ok) await check(supabase.rpc('set_manual_order', { p_scope: scope, p_ids: [...lastInTarget.filter((x) => x !== d.id), d.id] }))
    await reload(['folders', 'suppliers', 'items', 'manual_order'])
  }

  return { reorder, move }
}

/** Puts `id` before or after `target` in `ids`. */
export function placed(ids: string[], id: string, target: string, side: 'before' | 'after'): string[] {
  const rest = ids.filter((x) => x !== id)
  const at = rest.indexOf(target) + (side === 'after' ? 1 : 0)
  return [...rest.slice(0, at), id, ...rest.slice(at)]
}

/** Gear menu on a list page: sort mode of the open folder (or list), and editing the open folder. */
export function SettingsMenu({ scope, folder, onEditFolder }: {
  scope: FolderScope
  folder: Folder | undefined
  onEditFolder: () => void
}) {
  const { t } = useI18n()
  const { sortMode, reload } = useData()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const mode = sortMode(scope, folder?.id ?? null)

  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [open])

  const change = async (m: SortMode) => {
    if (folder) await supabase.from('folders').update({ sort_mode: m }).eq('id', folder.id)
    else await supabase.from('list_settings').update({ sort_mode: m }).eq('scope', scope)
    await reload([folder ? 'folders' : 'list_settings'])
  }
  const modes: [SortMode, string][] = [['code', scope === 'supplier' ? t('sort.name') : t('sort.code')], ['manual', t('sort.manual')]]

  return (
    <div className="menu" ref={ref}>
      <button
        type="button"
        className="btn gear"
        aria-label={folder ? t('folder.settings') : t('folder.listSettings')}
        title={folder ? t('folder.settings') : t('folder.listSettings')}
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        ⚙
      </button>
      {open && (
        <div className="menu-panel" role="menu">
          <div className="menu-title">{folder ? t('folder.settings') : t('folder.listSettings')}</div>
          <div className="menu-label">{t('sort.title')}</div>
          {modes.map(([m, label]) => (
            <button key={m} type="button" role="menuitemradio" aria-checked={mode === m} className={mode === m ? 'checked' : ''} onClick={() => change(m)}>
              <span className="menu-check">{mode === m ? '●' : '○'}</span> {label}
            </button>
          ))}
          {folder && (
            <>
              <div className="menu-sep" />
              <button type="button" role="menuitem" onClick={() => { setOpen(false); onEditFolder() }}>
                <span className="menu-check">✎</span> {t('folder.edit')}…
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}
