import { useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ConflictDialog, EditGuards } from '../components/EditGuards'
import { Banner, Field, NumInput, Row, Section } from '../components/ui'
import { useAuth } from '../lib/auth'
import { computeItemCost, costPerUnit, materialUnitCost } from '../lib/calc'
import { useData } from '../lib/data'
import { dateTime, money, parseNum, pct } from '../lib/format'
import { useI18n } from '../lib/i18n'
import { usePresence } from '../lib/presence'
import { errorKey, supabase } from '../lib/supabase'
import type { Item, ItemKind, QtyUnit, RecipeLine } from '../lib/types'
import { useVersionedForm } from '../lib/useVersionedForm'

const UNITS: QtyUnit[] = ['pcs', 'cm', 'cm2']
const KINDS: ItemKind[] = ['product', 'merchandise', 'raw_material']

interface LineForm {
  key: string
  material_id: string
  quantity: string
  unit: QtyUnit
  comment: string
}

interface ItemForm {
  code: string
  name: string
  kind: ItemKind
  usable_as_material: boolean
  supplier_id: string
  supplier_code: string
  purchase_price: string
  purchase_qty: string
  unit: QtyUnit
  piece_length_cm: string
  piece_area_cm2: string
  discount_pct: string
  shipping_pct: string
  labor_minutes: string
  markup_pct: string
  rounding_step: string
  notes: string
  lines: LineForm[]
}

const s = (v: number | null | undefined) => (v == null ? '' : String(v))
let keySeq = 0
const newKey = () => `l${++keySeq}`

function toForm(item: Item | undefined, lines: RecipeLine[], kind: ItemKind): ItemForm {
  return {
    code: item?.code ?? '',
    name: item?.name ?? '',
    kind: item?.kind ?? kind,
    usable_as_material: item?.usable_as_material ?? kind === 'raw_material',
    supplier_id: item?.supplier_id ?? '',
    supplier_code: item?.supplier_code ?? '',
    purchase_price: s(item?.purchase_price),
    purchase_qty: s(item?.purchase_qty ?? 1),
    unit: item?.unit ?? 'pcs',
    piece_length_cm: s(item?.piece_length_cm),
    piece_area_cm2: s(item?.piece_area_cm2),
    discount_pct: s(item?.discount_pct),
    shipping_pct: s(item?.shipping_pct),
    labor_minutes: s(item?.labor_minutes ?? 0),
    markup_pct: s(item?.markup_pct),
    rounding_step: s(item?.rounding_step),
    notes: item?.notes ?? '',
    lines: lines.map((l) => ({
      key: newKey(),
      material_id: l.material_id,
      quantity: s(l.quantity),
      unit: l.unit,
      comment: l.comment ?? '',
    })),
  }
}

function formToItem(f: ItemForm, base: Item | undefined): Item {
  return {
    id: base?.id ?? '',
    code: f.code.trim(),
    name: f.name.trim(),
    kind: f.kind,
    usable_as_material: f.kind === 'raw_material' ? true : f.kind === 'product' ? false : f.usable_as_material,
    supplier_id: f.kind === 'product' ? null : f.supplier_id || null,
    supplier_code: f.supplier_code.trim() || null,
    purchase_price: parseNum(f.purchase_price) ?? 0,
    purchase_qty: parseNum(f.purchase_qty) || 1,
    unit: f.unit,
    piece_length_cm: parseNum(f.piece_length_cm) || null,
    piece_area_cm2: parseNum(f.piece_area_cm2) || null,
    discount_pct: parseNum(f.discount_pct),
    shipping_pct: parseNum(f.shipping_pct),
    labor_minutes: parseNum(f.labor_minutes) ?? 0,
    markup_pct: parseNum(f.markup_pct),
    rounding_step: parseNum(f.rounding_step),
    notes: f.notes.trim() || null,
    version: base?.version ?? 1,
    updated_at: base?.updated_at ?? '',
    updated_by_email: base?.updated_by_email ?? null,
  }
}

function formToLines(f: ItemForm): RecipeLine[] {
  if (f.kind !== 'product') return []
  return f.lines
    .filter((l) => l.material_id && (parseNum(l.quantity) ?? 0) > 0)
    .map((l) => ({ material_id: l.material_id, quantity: parseNum(l.quantity)!, unit: l.unit, comment: l.comment.trim() || null }))
}

export function ItemEdit() {
  const { id, kind: newKind } = useParams()
  const isNew = !id
  const { t, lang } = useI18n()
  const { email } = useAuth()
  const nav = useNavigate()
  const { items, itemMap, suppliers, supplierMap, linesByProduct, settings, ctx, reload } = useData()
  const record = id ? itemMap.get(id) : undefined
  const recordLines = (id && linesByProduct.get(id)) || []
  const cur = settings?.currency ?? '€'

  const { form, setForm, dirty, setDirty, baseVersion, setBaseVersion, staleBy, reset } = useVersionedForm(
    record,
    (r) => toForm(r, (id && linesByProduct.get(id)) || [], (newKind as ItemKind) ?? 'product'),
    JSON.stringify([record, recordLines]),
  )
  const others = usePresence(id ? `item:${id}` : null, email)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)
  const [conflict, setConflict] = useState<null | { deleted: boolean }>(null)

  const set = <K extends keyof ItemForm>(k: K, v: ItemForm[K]) => setForm((f) => ({ ...f, [k]: v }))
  const setLine = (key: string, patch: Partial<LineForm>) =>
    setForm((f) => ({ ...f, lines: f.lines.map((l) => (l.key === key ? { ...l, ...patch } : l)) }))

  const draft = formToItem(form, record)
  const draftLines = formToLines(form)
  const cost = useMemo(() => (ctx ? computeItemCost(draft, draftLines, ctx) : null), [ctx, JSON.stringify(draft), JSON.stringify(draftLines)]) // eslint-disable-line react-hooks/exhaustive-deps

  const materials = useMemo(
    () => items.filter((i) => i.usable_as_material && i.kind !== 'product' && i.id !== id),
    [items, id],
  )
  const usedIn = useMemo(
    () => (id ? items.filter((p) => (linesByProduct.get(p.id) ?? []).some((l) => l.material_id === id)) : []),
    [items, linesByProduct, id],
  )
  const supplier = form.supplier_id ? supplierMap.get(form.supplier_id) : undefined

  if (!isNew && !record) {
    return (
      <div>
        <Banner kind="error">{t('err.notFound')}</Banner>
        <Link to="/list/product">{t('common.back')}</Link>
      </div>
    )
  }

  const save = async (expectedVersion: number | null) => {
    if (!form.code.trim()) {
      setMsg({ kind: 'error', text: t('err.required') })
      return
    }
    setBusy(true)
    setMsg(null)
    const { data, error } = await supabase.rpc('save_item', {
      p_item: { ...draft, id: record?.id ?? null },
      p_lines: draftLines,
      p_expected_version: expectedVersion,
    })
    setBusy(false)
    if (error) {
      const key = errorKey(error)
      if (key === 'err.conflict') setConflict({ deleted: false })
      else if (key === 'err.notFound') setConflict({ deleted: true })
      else setMsg({ kind: 'error', text: t(key) })
      return
    }
    const saved = data as Item
    setBaseVersion(saved.version)
    await reload(['items', 'recipe_lines'])
    if (isNew) nav(`/item/${saved.id}`, { replace: true })
    setDirty(false)
    setMsg({ kind: 'ok', text: t('common.saved') })
  }

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    save(baseVersion)
  }

  const remove = async () => {
    if (!record || !confirm(t('common.confirmDelete', { name: record.code }))) return
    const { data, error } = await supabase.from('items').delete().eq('id', record.id).eq('version', baseVersion!).select('id')
    if (error) return setMsg({ kind: 'error', text: t(errorKey(error)) })
    if (!data?.length) return setConflict({ deleted: false })
    await reload(['items', 'recipe_lines'])
    nav(`/list/${record.kind}`, { replace: true })
  }

  const back = () => {
    if (dirty && !confirm(t('common.leaveUnsaved'))) return
    nav(`/list/${record?.kind ?? form.kind}`)
  }

  const isProduct = form.kind === 'product'
  const sells = form.kind !== 'raw_material'
  const unitLabel = t(`unit.${form.unit}`)

  return (
    <form onSubmit={onSubmit}>
      <div className="page-head">
        <button type="button" className="btn ghost" onClick={back}>← {t('common.back')}</button>
        <h1>{isNew ? t('item.new', { kind: t(`kind.${form.kind}`) }) : `${record!.code}`}</h1>
        <span className="badge">{t(`kind.${form.kind}`)}</span>
        {dirty && <span className="badge warn">{t('common.unsaved')}</span>}
      </div>

      <EditGuards others={others} staleBy={staleBy} onReload={reset} />
      {msg && <Banner kind={msg.kind}>{msg.text}</Banner>}

      <div className="edit-grid">
        <div className="edit-main">
          <Section title={t('item.sectionGeneral')}>
            <div className="fields">
              <Field label={`${t('item.code')} *`}>
                <input value={form.code} required onChange={(e) => set('code', e.target.value)} />
              </Field>
              <Field label={t('item.name')}>
                <input value={form.name} onChange={(e) => set('name', e.target.value)} />
              </Field>
              <Field label={t('item.kind')} help={t(`kindHelp.${form.kind}`)}>
                <select value={form.kind} onChange={(e) => set('kind', e.target.value as ItemKind)}>
                  {KINDS.map((k) => <option key={k} value={k}>{t(`kind.${k}`)}</option>)}
                </select>
              </Field>
              {form.kind === 'merchandise' && (
                <label className="check">
                  <input type="checkbox" checked={form.usable_as_material} onChange={(e) => set('usable_as_material', e.target.checked)} />
                  {t('item.usableAsMaterial')}
                </label>
              )}
            </div>
          </Section>

          {!isProduct && (
            <Section title={t('item.sectionPurchase')}>
              <div className="fields">
                <Field label={t('item.supplier')}>
                  <select value={form.supplier_id} onChange={(e) => set('supplier_id', e.target.value)}>
                    <option value="">{t('item.noSupplier')}</option>
                    {suppliers.map((sup) => <option key={sup.id} value={sup.id}>{sup.name}</option>)}
                  </select>
                </Field>
                <Field label={t('item.supplierCode')}>
                  <input value={form.supplier_code} onChange={(e) => set('supplier_code', e.target.value)} />
                </Field>
                <Field label={`${t('item.purchasePrice')} (${cur})`}>
                  <NumInput value={form.purchase_price} onChange={(v) => set('purchase_price', v)} />
                </Field>
                <Field label={t('item.unit')}>
                  <select value={form.unit} onChange={(e) => set('unit', e.target.value as QtyUnit)}>
                    {UNITS.map((u) => <option key={u} value={u}>{t(`unitLong.${u}`)}</option>)}
                  </select>
                </Field>
                <Field label={`${t('item.purchaseQty')} (${unitLabel})`} help={t('item.purchaseQtyHelp')}>
                  <NumInput value={form.purchase_qty} onChange={(v) => set('purchase_qty', v)} />
                </Field>
                <Field label={t('item.discount')} help={t('item.customHelp', { value: pct(supplier?.default_discount_pct ?? 0, lang) })}>
                  <NumInput value={form.discount_pct} placeholder={s(supplier?.default_discount_pct ?? 0)} onChange={(v) => set('discount_pct', v)} />
                </Field>
                <Field label={t('item.shipping')} help={t('item.customHelp', { value: pct(supplier?.shipping_pct ?? 0, lang) })}>
                  <NumInput value={form.shipping_pct} placeholder={s(supplier?.shipping_pct ?? 0)} onChange={(v) => set('shipping_pct', v)} />
                </Field>
                {form.usable_as_material && (
                  <>
                    {form.unit !== 'cm' && (
                      <Field label={t('item.pieceLength')} help={t('item.pieceHelp')}>
                        <NumInput value={form.piece_length_cm} onChange={(v) => set('piece_length_cm', v)} />
                      </Field>
                    )}
                    {form.unit !== 'cm2' && (
                      <Field label={t('item.pieceArea')} help={t('item.pieceHelp')}>
                        <NumInput value={form.piece_area_cm2} onChange={(v) => set('piece_area_cm2', v)} />
                      </Field>
                    )}
                  </>
                )}
              </div>
            </Section>
          )}

          {isProduct && (
            <Section title={t('item.sectionRecipe')}>
              {form.lines.length === 0 && <p className="muted">{t('recipe.empty')}</p>}
              <div className="recipe">
                {form.lines.map((l) => {
                  const mat = itemMap.get(l.material_id)
                  const unitCost = mat && ctx ? costPerUnit(mat, materialUnitCost(mat, ctx), l.unit) : undefined
                  const lineCost = unitCost == null ? unitCost : unitCost * (parseNum(l.quantity) ?? 0)
                  return (
                    <div className="recipe-line" key={l.key}>
                      <select
                        className="rl-material"
                        value={l.material_id}
                        onChange={(e) => {
                          const m = itemMap.get(e.target.value)
                          setLine(l.key, { material_id: e.target.value, unit: m?.unit ?? l.unit })
                        }}
                      >
                        <option value="">{t('recipe.pick')}</option>
                        {mat && !materials.includes(mat) && <option value={mat.id}>{mat.code} — {mat.name}</option>}
                        {materials.map((m) => <option key={m.id} value={m.id}>{m.code}{m.name ? ` — ${m.name}` : ''}</option>)}
                      </select>
                      <span className="rl-qty">
                        <NumInput value={l.quantity} placeholder={t('recipe.quantity')} onChange={(v) => setLine(l.key, { quantity: v })} />
                      </span>
                      <select className="rl-unit" value={l.unit} onChange={(e) => setLine(l.key, { unit: e.target.value as QtyUnit })}>
                        {UNITS.map((u) => <option key={u} value={u}>{t(`unit.${u}`)}</option>)}
                      </select>
                      <input className="rl-comment" value={l.comment} placeholder={t('recipe.comment')} onChange={(e) => setLine(l.key, { comment: e.target.value })} />
                      <span className={`rl-cost num${unitCost === null ? ' warn-text' : ''}`} title={unitCost === null ? t('recipe.noConversion') : undefined}>
                        {unitCost === null ? '⚠' : money(lineCost, cur, lang, 3)}
                      </span>
                      <button type="button" className="btn small ghost rl-remove" aria-label={t('common.remove')} onClick={() => setForm((f) => ({ ...f, lines: f.lines.filter((x) => x.key !== l.key) }))}>✕</button>
                    </div>
                  )
                })}
              </div>
              <button type="button" className="btn" onClick={() => setForm((f) => ({ ...f, lines: [...f.lines, { key: newKey(), material_id: '', quantity: '', unit: 'pcs', comment: '' }] }))}>
                + {t('recipe.add')}
              </button>
              <div className="fields" style={{ marginTop: 16 }}>
                <Field label={t('item.labor')}>
                  <NumInput value={form.labor_minutes} onChange={(v) => set('labor_minutes', v)} />
                </Field>
              </div>
            </Section>
          )}

          {sells && (
            <Section title={t('item.sectionPricing')}>
              <div className="fields">
                <Field label={t('item.markup')} help={t('common.useDefault', { value: pct(settings?.default_markup_pct, lang) })}>
                  <NumInput value={form.markup_pct} placeholder={s(settings?.default_markup_pct)} onChange={(v) => set('markup_pct', v)} />
                </Field>
                <Field label={`${t('item.rounding')} (${cur})`} help={t('common.useDefault', { value: s(settings?.rounding_step) })}>
                  <NumInput value={form.rounding_step} placeholder={s(settings?.rounding_step)} onChange={(v) => set('rounding_step', v)} />
                </Field>
              </div>
            </Section>
          )}

          <Section title={t('common.notes')}>
            <textarea rows={3} value={form.notes} onChange={(e) => set('notes', e.target.value)} />
          </Section>

          {usedIn.length > 0 && (
            <Section title={t('item.usedIn')}>
              <ul className="links">
                {usedIn.map((p) => <li key={p.id}><Link to={`/item/${p.id}`}>{p.code}</Link> <span className="muted">{p.name}</span></li>)}
              </ul>
            </Section>
          )}
        </div>

        <aside className="edit-side">
          {cost && (
            <Section title={t('item.sectionCost')}>
              {cost.warnings.includes('missing_material') && <Banner kind="warn">{t('cost.warnMissing')}</Banner>}
              {cost.warnings.includes('no_conversion') && <Banner kind="warn">{t('cost.warnConversion')}</Banner>}
              {cost.purchase && (
                <>
                  <Row label={t('cost.listPrice')} value={money(draft.purchase_price, cur, lang)} />
                  <Row label={`${t('cost.discount')} (${pct(cost.purchase.discountPct, lang)})`} value={money(cost.purchase.net - draft.purchase_price, cur, lang)} muted />
                  <Row label={t('cost.net')} value={money(cost.purchase.net, cur, lang)} />
                  <Row label={`${t('cost.shipping')} (${pct(cost.purchase.shippingPct, lang)})`} value={money(cost.purchase.shipping, cur, lang)} muted />
                  <Row label={t('cost.purchaseTotal')} value={money(cost.purchase.total, cur, lang)} />
                  <Row label={t('cost.unitCost', { unit: unitLabel })} value={money(cost.purchase.unitCost, cur, lang, 4)} strong />
                </>
              )}
              {isProduct && (
                <>
                  <Row label={t('cost.materials')} value={money(cost.materials, cur, lang)} />
                  <Row label={`${t('cost.labor')} (${draft.labor_minutes}′ × ${money(settings?.labor_rate_per_hour, cur, lang)}/h)`} value={money(cost.labor, cur, lang)} />
                  <Row label={`${t('cost.overhead')} (${money(settings?.overhead_rate_per_hour, cur, lang)}/h)`} value={money(cost.overhead, cur, lang)} />
                  <Row label={t('cost.total')} value={money(cost.cost, cur, lang)} strong />
                </>
              )}
              {sells && (
                <div className="price-box">
                  <Row label={`${t('cost.markup')} (${pct(cost.markupPct, lang)})`} value={money(cost.rawPrice - cost.cost, cur, lang)} muted />
                  <Row label={t('cost.rawPrice')} value={money(cost.rawPrice, cur, lang, 3)} muted />
                  <Row label={t('cost.price')} value={money(cost.price, cur, lang)} strong />
                  <Row label={t('cost.profit')} value={money(cost.profit, cur, lang)} />
                  <Row label={t('cost.margin')} value={pct(cost.marginPct, lang)} />
                </div>
              )}
            </Section>
          )}
          <div className="save-bar">
            <button className="btn primary" disabled={busy}>{busy ? t('common.saving') : t('common.save')}</button>
            {!isNew && <button type="button" className="btn danger ghost" onClick={remove}>{t('common.delete')}</button>}
          </div>
          {record?.updated_at && (
            <p className="muted small">{t('common.lastChange', { when: dateTime(record.updated_at, lang), who: record.updated_by_email ?? '?' })}</p>
          )}
        </aside>
      </div>

      {conflict && (
        <ConflictDialog
          who={record?.updated_by_email ?? ''}
          deleted={conflict.deleted}
          onClose={() => setConflict(null)}
          onReload={async () => {
            setConflict(null)
            await reload(['items', 'recipe_lines'])
            setDirty(false)
          }}
          onOverwrite={async () => {
            setConflict(null)
            const { data } = await supabase.from('items').select('version').eq('id', record!.id).single()
            if (data) save(data.version)
          }}
        />
      )}
    </form>
  )
}
