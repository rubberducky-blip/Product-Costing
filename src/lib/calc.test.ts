import { describe, expect, it } from 'vitest'
import { computeItemCost, costPerUnit, purchaseCost, roundPrice, type CostContext } from './calc'
import type { Item, Settings, Supplier } from './types'

const settings: Settings = {
  id: 1, labor_rate_per_hour: 10, overhead_rate_per_hour: 2, default_markup_pct: 100, rounding_step: 0.05,
  rounding_mode: 'nearest', currency: '€', backup_retention_days: 30, version: 1, updated_at: '', updated_by_email: null,
}
const supplier: Supplier = {
  id: 's1', name: 'Acme', country: 'DE', default_discount_pct: 10, shipping_pct: 14, email: null, phone: null,
  notes: null, version: 1, updated_at: '', updated_by_email: null,
}
const base: Item = {
  id: '', code: '', name: '', kind: 'raw_material', usable_as_material: true, supplier_id: 's1', supplier_code: null,
  purchase_price: 0, purchase_qty: 1, unit: 'pcs', piece_length_cm: null, piece_area_cm2: null, discount_pct: null,
  shipping_pct: null, labor_minutes: 0, markup_pct: null, rounding_step: null, notes: null, version: 1,
  updated_at: '', updated_by_email: null,
}
const leather: Item = { ...base, id: 'm1', code: 'LEATHER', purchase_price: 100, purchase_qty: 5000, unit: 'cm2' }
const ring: Item = { ...base, id: 'm2', code: 'RING', purchase_price: 2, purchase_qty: 10, discount_pct: 0, shipping_pct: 0 }
const ctx: CostContext = {
  settings,
  suppliers: new Map([['s1', supplier]]),
  items: new Map([[leather.id, leather], [ring.id, ring]]),
}

describe('purchaseCost', () => {
  it('applies supplier discount then shipping', () => {
    const p = purchaseCost({ ...base, purchase_price: 100 }, supplier)
    expect(p.net).toBeCloseTo(90)
    expect(p.total).toBeCloseTo(102.6)
  })
  it('item overrides beat supplier defaults, including 0', () => {
    const p = purchaseCost({ ...base, purchase_price: 100, discount_pct: 0, shipping_pct: 5 }, supplier)
    expect(p.total).toBeCloseTo(105)
  })
})

describe('costPerUnit', () => {
  it('converts via piece size and returns null without it', () => {
    const strap: Item = { ...base, unit: 'cm', piece_length_cm: 100 }
    expect(costPerUnit(strap, 0.02, 'pcs')).toBeCloseTo(2)
    expect(costPerUnit(strap, 0.02, 'cm2')).toBeNull()
    expect(costPerUnit({ ...base, unit: 'pcs', piece_area_cm2: 50 }, 5, 'cm2')).toBeCloseTo(0.1)
  })
})

describe('roundPrice', () => {
  it('rounds to step', () => {
    expect(roundPrice(12.32, 0.05, 'nearest')).toBe(12.3)
    expect(roundPrice(12.33, 0.05, 'nearest')).toBe(12.35)
    expect(roundPrice(12.31, 0.05, 'up')).toBe(12.35)
    expect(roundPrice(1.15, 0.05, 'up')).toBe(1.15)
    expect(roundPrice(12.34, 0.5, 'down')).toBe(12)
    expect(roundPrice(12.34, 0, 'nearest')).toBe(12.34)
  })
})

describe('computeItemCost', () => {
  it('sums materials, labor and overhead for products', () => {
    const product: Item = { ...base, id: 'p1', kind: 'product', usable_as_material: false, supplier_id: null, labor_minutes: 30 }
    const c = computeItemCost(product, [
      { material_id: 'm1', quantity: 200, unit: 'cm2', comment: null },
      { material_id: 'm2', quantity: 2, unit: 'pcs', comment: null },
    ], ctx)
    // leather: 100 × 0.9 × 1.14 / 5000 = 0.02052 per cm² → 4.104; rings 0.2 × 2 = 0.4
    expect(c.materials).toBeCloseTo(4.504)
    expect(c.labor).toBeCloseTo(5)
    expect(c.overhead).toBeCloseTo(1)
    expect(c.cost).toBeCloseTo(10.504)
    expect(c.price).toBe(21)
    expect(c.marginPct).toBeCloseTo(((21 - 10.504) / 21) * 100)
  })
  it('flags unconvertible units', () => {
    const product: Item = { ...base, kind: 'product', usable_as_material: false }
    const c = computeItemCost(product, [{ material_id: 'm1', quantity: 1, unit: 'cm', comment: null }], ctx)
    expect(c.warnings).toContain('no_conversion')
  })
  it('prices merchandise per unit with item markup and rounding', () => {
    const m: Item = { ...base, kind: 'merchandise', purchase_price: 30, purchase_qty: 3, markup_pct: 50, rounding_step: 1 }
    const c = computeItemCost(m, [], ctx)
    expect(c.cost).toBeCloseTo(10.26)
    expect(c.price).toBe(15)
  })
})
