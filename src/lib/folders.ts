import type { Folder, FolderScope, Item, SortMode } from './types'

/** One row of a Combobox list. Headers are folder names shown for structure; they cannot be picked. */
export interface Option {
  value: string
  label: string
  /** extra text that is searched and shown dimmed, e.g. a name next to a code */
  sub?: string
  depth?: number
  header?: boolean
  /** folder path, shown while searching so a match can be placed */
  path?: string
}

export const norm = (s: string | null | undefined) =>
  (s ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')

const byName = (a: Folder, b: Folder) => a.name.localeCompare(b.name)

/** How a list is ordered: per folder sort mode and manual positions. */
export interface Arrangement {
  mode: (folderId: string | null) => SortMode
  pos: (id: string) => number | undefined
}

/**
 * Sorts by `key` (code / name) or, in manual mode, by manual position; entries never arranged go last, by key.
 * `numeric` makes "A2" come before "A10".
 */
export function arrange<T>(list: T[], mode: SortMode, pos: (t: T) => number | undefined, key: (t: T) => string): T[] {
  const byKey = (a: T, b: T) => key(a).localeCompare(key(b), undefined, { numeric: true })
  if (mode !== 'manual') return [...list].sort(byKey)
  return [...list].sort((a, b) => (pos(a) ?? Infinity) - (pos(b) ?? Infinity) || byKey(a, b))
}

/** The folder an item sits in within a list; merchandise used as raw material has its own raw-materials folder. */
export const listFolderOf = (item: Item, scope: FolderScope) =>
  scope === 'raw_material' && item.kind === 'merchandise' ? item.material_folder_id : item.folder_id

export function childFolders(folders: Folder[], scope: FolderScope, parentId: string | null): Folder[] {
  return folders.filter((f) => f.scope === scope && f.parent_id === parentId).sort(byName)
}

/** The folder and its ancestors, outermost first. */
export function folderPath(folderMap: Map<string, Folder>, id: string | null | undefined): Folder[] {
  const out: Folder[] = []
  const seen = new Set<string>()
  for (let f = id ? folderMap.get(id) : undefined; f && !seen.has(f.id); f = f.parent_id ? folderMap.get(f.parent_id) : undefined) {
    seen.add(f.id)
    out.unshift(f)
  }
  return out
}

export const pathText = (folderMap: Map<string, Folder>, id: string | null | undefined) =>
  folderPath(folderMap, id).map((f) => f.name).join(' / ')

/** The folder and every folder below it. */
export function folderAndDescendants(folders: Folder[], id: string): Set<string> {
  const out = new Set([id])
  let grew = true
  while (grew) {
    grew = false
    for (const f of folders) {
      if (f.parent_id && out.has(f.parent_id) && !out.has(f.id)) {
        out.add(f.id)
        grew = true
      }
    }
  }
  return out
}

interface Entry {
  value: string
  label: string
  sub?: string
  folderId: string | null
}

/**
 * Options laid out as a folder tree: sub-folders first (as headers), then the entries of each folder.
 * Folders without entries below them are left out. Entries in an unknown folder are shown at the top level.
 */
export function treeOptions(folders: Folder[], scope: FolderScope, entries: Entry[], order?: Arrangement): Option[] {
  const known = new Set(folders.filter((f) => f.scope === scope).map((f) => f.id))
  const byFolder = new Map<string | null, Entry[]>()
  for (const e of entries) {
    const k = e.folderId && known.has(e.folderId) ? e.folderId : null
    byFolder.set(k, [...(byFolder.get(k) ?? []), e])
  }
  const sorted = <T extends { id?: string; value?: string }>(list: T[], parentId: string | null, key: (t: T) => string) =>
    order ? arrange(list, order.mode(parentId), (t) => order.pos((t.id ?? t.value)!), key) : list
  const walk = (parentId: string | null, depth: number, path: string): Option[] => {
    const out: Option[] = []
    for (const f of sorted(childFolders(folders, scope, parentId), parentId, (f) => f.name)) {
      const sub = path ? `${path} / ${f.name}` : f.name
      const inner = walk(f.id, depth + 1, sub)
      if (inner.length) out.push({ value: `folder:${f.id}`, label: f.name, depth, header: true, path }, ...inner)
    }
    const own = sorted((byFolder.get(parentId) ?? []).sort((a, b) => a.label.localeCompare(b.label)), parentId, (e) => e.label)
    for (const e of own) out.push({ value: e.value, label: e.label, sub: e.sub, depth, path })
    return out
  }
  return walk(null, 0, '')
}

/** Every folder of a list as a pickable option, indented by depth. */
export function folderOptions(folders: Folder[], scope: FolderScope, exclude: Set<string> = new Set()): Option[] {
  const walk = (parentId: string | null, depth: number, path: string): Option[] =>
    childFolders(folders, scope, parentId)
      .filter((f) => !exclude.has(f.id))
      .flatMap((f) => [
        { value: f.id, label: `📁 ${f.name}`, depth, path },
        ...walk(f.id, depth + 1, path ? `${path} / ${f.name}` : f.name),
      ])
  return walk(null, 0, '')
}
