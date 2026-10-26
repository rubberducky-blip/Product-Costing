import type { Item, QtyUnit, RecipeLine, RoundingMode, Settings, Supplier } from './types'

const num = (v: number | string | null | undefined) => (v == null || v === '' ? 0 : Number(v))

export interface PurchaseCost {
  discountPct: number
  shippingPct: number
  net: number // purchase price after discount
  shipping: number // shipping on the net price
  total: number // net + shipping, for one purchase (purchase_qty units)
  unitCost: number // total / purchase_qty, per item.unit
}

/** Cost of a bought item (merchandise / raw material) including discount and shipping. */
export function purchaseCost(item: Item, supplier: Supplier | undefined): PurchaseCost {
  const discountPct = item.discount_pct ?? supplier?.default_discount_pct ?? 0
  const shippingPct = item.shipping_pct ?? supplier?.shipping_pct ?? 0
  const net = num(item.purchase_price) * (1 - num(discountPct) / 100)
  const shipping = net * (num(shippingPct) / 100)
  const total = net + shipping
  const qty = num(item.purchase_qty) > 0 ? num(item.purchase_qty) : 1
  return { discountPct: num(discountPct), shippingPct: num(shippingPct), net, shipping, total, unitCost: total / qty }
}

/**
 * Cost of one `target` unit of a material whose cost is known per `item.unit`.
 * Converting between pcs / cm / cm² needs the size of one piece; returns null when missing.
 */
export function costPerUnit(item: Item, unitCost: number, target: QtyUnit): number | null {
  if (target === item.unit) return unitCost
  const len = num(item.piece_length_cm)
  const area = num(item.piece_area_cm2)
  let perPiece: number
  if (item.unit === 'pcs') perPiece = unitCost
  else if (item.unit === 'cm') {
    if (!len) return null
    perPiece = unitCost * len
  } else {
    if (!area) return null
    perPiece = unitCost * area
  }
  if (target === 'pcs') return perPiece
  if (target === 'cm') return len ? perPiece / len : null
  return area ? perPiece / area : null
}

export function roundPrice(value: number, step: number, mode: RoundingMode): number {
  if (!step || step <= 0) return value
  const q = value / step
  // tiny epsilon avoids 1.15/0.05 = 22.999999 style artefacts
  const n = mode === 'up' ? Math.ceil(q - 1e-9) : mode === 'down' ? Math.floor(q + 1e-9) : Math.round(q + 1e-9)
  return Number((n * step).toFixed(6))
}

export interface MaterialLineCost {
  line: RecipeLine
  material: Item | undefined
  unitCost: number | null
  cost: number | null
}

export interface ItemCost {
  cost: number // full unit cost
  materials: number
  labor: number
  overhead: number
  purchase?: PurchaseCost
  lines: MaterialLineCost[]
  warnings: ('missing_material' | 'no_conversion')[]
  markupPct: number
  price: number // cost × (1 + markup), rounded
  rawPrice: number
  roundingStep: number
  profit: number
  marginPct: number // profit / price
}

export interface CostContext {
  settings: Settings
  suppliers: Map<string, Supplier>
  items: Map<string, Item>
}

/** Cost of one unit of a material (in its default unit). */
export function materialUnitCost(item: Item, ctx: CostContext): number {
  return purchaseCost(item, item.supplier_id ? ctx.suppliers.get(item.supplier_id) : undefined).unitCost
}

export function computeItemCost(item: Item, lines: RecipeLine[], ctx: CostContext): ItemCost {
  const s = ctx.settings
  let materials = 0
  let labor = 0
  let overhead = 0
  let purchase: PurchaseCost | undefined
  const lineCosts: MaterialLineCost[] = []
  const warnings = new Set<ItemCost['warnings'][number]>()

  if (item.kind === 'product') {
    for (const line of lines) {
      const material = ctx.items.get(line.material_id)
      if (!material) {
        warnings.add('missing_material')
        lineCosts.push({ line, material, unitCost: null, cost: null })
        continue
      }
      const unitCost = costPerUnit(material, materialUnitCost(material, ctx), line.unit)
      if (unitCost == null) warnings.add('no_conversion')
      const cost = unitCost == null ? null : unitCost * num(line.quantity)
      materials += cost ?? 0
      lineCosts.push({ line, material, unitCost, cost })
    }
    const hours = num(item.labor_minutes) / 60
    labor = hours * num(s.labor_rate_per_hour)
    overhead = hours * num(s.overhead_rate_per_hour)
  } else {
    purchase = purchaseCost(item, item.supplier_id ? ctx.suppliers.get(item.supplier_id) : undefined)
  }

  // Purchased items are costed (and priced) per item.unit, e.g. one piece out of a box of 10.
  const cost = item.kind === 'product' ? materials + labor + overhead : purchase!.unitCost
  const markupPct = num(item.markup_pct ?? s.default_markup_pct)
  const roundingStep = num(item.rounding_step ?? s.rounding_step)
  const rawPrice = cost * (1 + markupPct / 100)
  const price = roundPrice(rawPrice, roundingStep, s.rounding_mode)
  const profit = price - cost
  const marginPct = price > 0 ? (profit / price) * 100 : 0

  return {
    cost, materials, labor, overhead, purchase, lines: lineCosts, warnings: [...warnings],
    markupPct, price, rawPrice, roundingStep, profit, marginPct,
  }
}
