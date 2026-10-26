import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useAuth } from './auth'
import { computeItemCost, type CostContext, type ItemCost } from './calc'
import { supabase } from './supabase'
import type {
  Folder, FolderScope, Item, LaborEntry, ListSetting, ManualOrder, PriceNotification, RecipeLine, Settings, SortMode, Supplier,
} from './types'

type Table =
  | 'app_settings' | 'folders' | 'suppliers' | 'items' | 'recipe_lines' | 'labor_entries'
  | 'list_settings' | 'manual_order' | 'price_notifications'

const TABLES: Table[] = [
  'app_settings', 'folders', 'suppliers', 'items', 'recipe_lines', 'labor_entries', 'list_settings', 'manual_order', 'price_notifications',
]
const ORDER: Record<Exclude<Table, 'app_settings'>, string> = {
  folders: 'name', suppliers: 'name', items: 'code', recipe_lines: 'position', labor_entries: 'position',
  list_settings: 'scope', manual_order: 'position', price_notifications: 'created_at',
}

interface Data {
  loading: boolean
  error: string | null
  settings: Settings | null
  folders: Folder[]
  suppliers: Supplier[]
  items: Item[]
  lines: RecipeLine[]
  laborEntries: LaborEntry[]
  notifications: PriceNotification[] // newest first
  folderMap: Map<string, Folder>
  supplierMap: Map<string, Supplier>
  itemMap: Map<string, Item>
  linesByProduct: Map<string, RecipeLine[]>
  laborByProduct: Map<string, LaborEntry[]>
  ctx: CostContext | null
  costs: Map<string, ItemCost>
  /** sort mode of a folder, or of the top level of a list when folderId is null */
  sortMode: (scope: FolderScope, folderId: string | null) => SortMode
  /** manual position of an item, supplier or folder in a list (undefined = never arranged) */
  position: (scope: FolderScope, id: string) => number | undefined
  /** ids of the price notifications the current user has not seen, per item */
  unseen: Map<string, number[]>
  markSeen: (ids: number[]) => Promise<void>
  /** hides notifications for the current user (they also count as seen) */
  hideNotifications: (ids: number[]) => Promise<void>
  reload: (tables?: Table[]) => Promise<void>
}

const Ctx = createContext<Data | null>(null)

/** PostgREST returns at most 1000 rows per request; page through everything. */
async function fetchAll<T>(table: Table, order: string): Promise<T[]> {
  const page = 1000
  const out: T[] = []
  for (let from = 0; ; from += page) {
    const { data, error } = await supabase.from(table).select('*').order(order).range(from, from + page - 1)
    if (error) throw error
    out.push(...(data as T[]))
    if (!data || data.length < page) return out
  }
}

const round4 = (v: number) => Math.round(v * 1e4) / 1e4

export function DataProvider({ children }: { children: ReactNode }) {
  const { email } = useAuth()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [settings, setSettings] = useState<Settings | null>(null)
  const [folders, setFolders] = useState<Folder[]>([])
  const [suppliers, setSuppliers] = useState<Supplier[]>([])
  const [items, setItems] = useState<Item[]>([])
  const [lines, setLines] = useState<RecipeLine[]>([])
  const [laborEntries, setLaborEntries] = useState<LaborEntry[]>([])
  const [listSettings, setListSettings] = useState<ListSetting[]>([])
  const [manualOrder, setManualOrder] = useState<ManualOrder[]>([])
  const [notifications, setNotifications] = useState<PriceNotification[]>([])
  const inFlight = useRef(0)

  const reload = useCallback(async (tables: Table[] = TABLES) => {
    inFlight.current++
    try {
      // fetch everything first, then apply in one go so prices are never computed from half-updated data
      const results = await Promise.all(
        tables.map(async (t) => {
          if (t === 'app_settings') {
            const { data, error } = await supabase.from('app_settings').select('*').eq('id', 1).single()
            if (error) throw error
            return [t, data] as const
          }
          return [t, await fetchAll<unknown>(t, ORDER[t])] as const
        }),
      )
      for (const [t, data] of results) {
        if (t === 'app_settings') setSettings(data as Settings)
        else if (t === 'folders') setFolders(data as Folder[])
        else if (t === 'suppliers') setSuppliers(data as Supplier[])
        else if (t === 'items') setItems(data as Item[])
        else if (t === 'recipe_lines') setLines(data as RecipeLine[])
        else if (t === 'labor_entries') setLaborEntries(data as LaborEntry[])
        else if (t === 'list_settings') setListSettings(data as ListSetting[])
        else if (t === 'manual_order') setManualOrder(data as ManualOrder[])
        else setNotifications((data as PriceNotification[]).reverse())
      }
      setError(null)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      inFlight.current--
      setLoading(false)
    }
  }, [])

  // Live updates: when any user saves, refetch the affected table (debounced).
  const pending = useRef(new Set<Table>())
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  useEffect(() => {
    reload()
    const schedule = (t: Table) => {
      pending.current.add(t)
      clearTimeout(timer.current)
      timer.current = setTimeout(() => {
        const tables = [...pending.current]
        pending.current.clear()
        reload(tables)
      }, 250)
    }
    const channel = supabase.channel('db-changes')
    for (const t of TABLES) {
      channel.on('postgres_changes', { event: '*', schema: 'public', table: t }, () => schedule(t))
    }
    channel.subscribe((status) => {
      // after a reconnect we may have missed events
      if (status === 'SUBSCRIBED') schedule('items')
    })
    const onFocus = () => document.visibilityState === 'visible' && reload()
    document.addEventListener('visibilitychange', onFocus)
    return () => {
      clearTimeout(timer.current)
      supabase.removeChannel(channel)
      document.removeEventListener('visibilitychange', onFocus)
    }
  }, [reload])

  const markSeen = useCallback(async (ids: number[]) => {
    if (!ids.length || !email) return
    const set = new Set(ids)
    setNotifications((ns) => ns.map((n) => (set.has(n.id) && !n.seen_by.includes(email) ? { ...n, seen_by: [...n.seen_by, email] } : n)))
    await supabase.rpc('mark_notifications_seen', { p_ids: ids })
  }, [email])

  const hideNotifications = useCallback(async (ids: number[]) => {
    if (!ids.length || !email) return
    const set = new Set(ids)
    const add = (list: string[] | undefined) => (list ?? []).includes(email) ? list ?? [] : [...(list ?? []), email]
    setNotifications((ns) => ns.map((n) => (set.has(n.id) ? { ...n, seen_by: add(n.seen_by), hidden_by: add(n.hidden_by) } : n)))
    await supabase.rpc('hide_notifications', { p_ids: ids })
  }, [email])

  const value = useMemo<Data>(() => {
    const supplierMap = new Map(suppliers.map((s) => [s.id, s]))
    const itemMap = new Map(items.map((i) => [i.id, i]))
    const linesByProduct = new Map<string, RecipeLine[]>()
    for (const l of lines) {
      const arr = linesByProduct.get(l.product_id!) ?? []
      arr.push(l)
      linesByProduct.set(l.product_id!, arr)
    }
    const laborByProduct = new Map<string, LaborEntry[]>()
    for (const l of laborEntries) {
      const arr = laborByProduct.get(l.product_id!) ?? []
      arr.push(l)
      laborByProduct.set(l.product_id!, arr)
    }
    const folderMap = new Map(folders.map((f) => [f.id, f]))
    const ctx = settings ? { settings, suppliers: supplierMap, items: itemMap } : null
    const costs = new Map<string, ItemCost>()
    if (ctx) for (const i of items) costs.set(i.id, computeItemCost(i, linesByProduct.get(i.id) ?? [], ctx, laborByProduct.get(i.id)))

    const rootModes = new Map(listSettings.map((s) => [s.scope, s.sort_mode]))
    const sortMode = (scope: FolderScope, folderId: string | null): SortMode =>
      (folderId ? folderMap.get(folderId)?.sort_mode : rootModes.get(scope)) ?? 'code'
    const positions = new Map(manualOrder.map((o) => [`${o.scope}:${o.id}`, o.position]))
    const position = (scope: FolderScope, id: string) => positions.get(`${scope}:${id}`)

    const unseen = new Map<string, number[]>()
    for (const n of notifications) {
      if (email && !n.seen_by.includes(email) && !(n.hidden_by ?? []).includes(email)) unseen.set(n.item_id, [...(unseen.get(n.item_id) ?? []), n.id])
    }
    return {
      loading, error, settings, folders, suppliers, items, lines, laborEntries, notifications, folderMap, supplierMap, itemMap,
      linesByProduct, laborByProduct, ctx, costs, sortMode, position, unseen, markSeen, hideNotifications, reload,
    }
  }, [loading, error, settings, folders, suppliers, items, lines, laborEntries, listSettings, manualOrder, notifications, email, markSeen, hideNotifications, reload])

  // Report computed prices so the database can notice changes caused by other edits (labor rate, a material,
  // a supplier discount…). Only prices that changed since this tab last reported are sent, so a tab that has
  // not caught up yet never reports stale prices.
  const lastSent = useRef(new Map<string, number>())
  const { costs } = value
  useEffect(() => {
    if (loading || error || !settings) return
    const h = setTimeout(() => {
      if (inFlight.current > 0) return
      const changed: { id: string; price: number }[] = []
      for (const i of items) {
        if (i.kind === 'raw_material') continue
        const p = costs.get(i.id)?.price
        if (p == null || !Number.isFinite(p)) continue
        const price = round4(p)
        if (lastSent.current.get(i.id) !== price) changed.push({ id: i.id, price })
      }
      if (!changed.length) return
      for (const c of changed) lastSent.current.set(c.id, c.price)
      supabase.rpc('record_prices', { p_prices: changed }).then(({ error }) => {
        if (error) for (const c of changed) lastSent.current.delete(c.id)
      })
    }, 1500)
    return () => clearTimeout(h)
  }, [costs, items, loading, error, settings])

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useData() {
  const c = useContext(Ctx)
  if (!c) throw new Error('useData outside provider')
  return c
}
