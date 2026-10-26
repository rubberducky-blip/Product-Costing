import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { computeItemCost, type CostContext, type ItemCost } from './calc'
import { supabase } from './supabase'
import type { Item, RecipeLine, Settings, Supplier } from './types'

type Table = 'app_settings' | 'suppliers' | 'items' | 'recipe_lines'

interface Data {
  loading: boolean
  error: string | null
  settings: Settings | null
  suppliers: Supplier[]
  items: Item[]
  lines: RecipeLine[]
  supplierMap: Map<string, Supplier>
  itemMap: Map<string, Item>
  linesByProduct: Map<string, RecipeLine[]>
  ctx: CostContext | null
  costs: Map<string, ItemCost>
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

export function DataProvider({ children }: { children: ReactNode }) {
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [settings, setSettings] = useState<Settings | null>(null)
  const [suppliers, setSuppliers] = useState<Supplier[]>([])
  const [items, setItems] = useState<Item[]>([])
  const [lines, setLines] = useState<RecipeLine[]>([])

  const reload = useCallback(async (tables: Table[] = ['app_settings', 'suppliers', 'items', 'recipe_lines']) => {
    try {
      await Promise.all(
        tables.map(async (t) => {
          if (t === 'app_settings') {
            const { data, error } = await supabase.from('app_settings').select('*').eq('id', 1).single()
            if (error) throw error
            setSettings(data as Settings)
          } else if (t === 'suppliers') setSuppliers(await fetchAll<Supplier>('suppliers', 'name'))
          else if (t === 'items') setItems(await fetchAll<Item>('items', 'code'))
          else setLines(await fetchAll<RecipeLine>('recipe_lines', 'position'))
        }),
      )
      setError(null)
    } catch (e) {
      setError((e as Error).message)
    } finally {
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
    for (const t of ['app_settings', 'suppliers', 'items', 'recipe_lines'] as Table[]) {
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

  const value = useMemo<Data>(() => {
    const supplierMap = new Map(suppliers.map((s) => [s.id, s]))
    const itemMap = new Map(items.map((i) => [i.id, i]))
    const linesByProduct = new Map<string, RecipeLine[]>()
    for (const l of lines) {
      const arr = linesByProduct.get(l.product_id!) ?? []
      arr.push(l)
      linesByProduct.set(l.product_id!, arr)
    }
    const ctx = settings ? { settings, suppliers: supplierMap, items: itemMap } : null
    const costs = new Map<string, ItemCost>()
    if (ctx) for (const i of items) costs.set(i.id, computeItemCost(i, linesByProduct.get(i.id) ?? [], ctx))
    return { loading, error, settings, suppliers, items, lines, supplierMap, itemMap, linesByProduct, ctx, costs, reload }
  }, [loading, error, settings, suppliers, items, lines, reload])

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useData() {
  const c = useContext(Ctx)
  if (!c) throw new Error('useData outside provider')
  return c
}
