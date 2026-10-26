export type ItemKind = 'product' | 'merchandise' | 'raw_material'
export type QtyUnit = 'pcs' | 'cm' | 'cm2'
export type RoundingMode = 'nearest' | 'up' | 'down'

export interface Settings {
  id: 1
  labor_rate_per_hour: number
  overhead_rate_per_hour: number
  default_markup_pct: number
  rounding_step: number
  rounding_mode: RoundingMode
  currency: string
  backup_retention_days: number
  version: number
  updated_at: string
  updated_by_email: string | null
}

export type FolderScope = ItemKind | 'supplier'

export interface Folder {
  id: string
  scope: FolderScope
  parent_id: string | null
  name: string
  version: number
  updated_at: string
  updated_by_email: string | null
}

export interface Supplier {
  id: string
  name: string
  folder_id: string | null
  country: string | null
  default_discount_pct: number
  shipping_pct: number
  email: string | null
  phone: string | null
  notes: string | null
  version: number
  updated_at: string
  updated_by_email: string | null
}

export interface Item {
  id: string
  code: string
  name: string
  kind: ItemKind
  usable_as_material: boolean
  folder_id: string | null
  supplier_id: string | null
  supplier_code: string | null
  purchase_price: number
  purchase_qty: number
  unit: QtyUnit
  piece_length_cm: number | null
  piece_area_cm2: number | null
  discount_pct: number | null
  shipping_pct: number | null
  markup_pct: number | null
  rounding_step: number | null
  notes: string | null
  version: number
  updated_at: string
  updated_by_email: string | null
}

export interface RecipeLine {
  id?: string
  product_id?: string
  material_id: string
  quantity: number
  unit: QtyUnit
  comment: string | null
  position?: number
}

export interface LaborEntry {
  id?: string
  product_id?: string
  seconds: number
  comment: string | null
  position?: number
}

export interface BackupMeta {
  id: number
  created_at: string
  kind: 'auto' | 'manual' | 'pre_restore'
  created_by_email: string | null
  size_bytes: number | null
}
