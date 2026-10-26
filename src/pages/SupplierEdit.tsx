import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Combobox } from '../components/Combobox'
import { ConflictDialog, EditGuards } from '../components/EditGuards'
import { useFolderFieldOptions } from '../components/Folders'
import { Banner, Field, NumInput, Section } from '../components/ui'
import { useAuth } from '../lib/auth'
import { useData } from '../lib/data'
import { dateTime, money, parseNum } from '../lib/format'
import { useI18n } from '../lib/i18n'
import { usePresence } from '../lib/presence'
import { errorKey, supabase } from '../lib/supabase'
import type { Supplier } from '../lib/types'
import { useVersionedForm } from '../lib/useVersionedForm'

interface SupplierForm {
  name: string
  folder_id: string
  country: string
  default_discount_pct: string
  shipping_pct: string
  email: string
  phone: string
  notes: string
}

const toForm = (s: Supplier | undefined, folderId: string): SupplierForm => ({
  name: s?.name ?? '',
  folder_id: s ? s.folder_id ?? '' : folderId,
  country: s?.country ?? '',
  default_discount_pct: String(s?.default_discount_pct ?? 0),
  shipping_pct: String(s?.shipping_pct ?? 0),
  email: s?.email ?? '',
  phone: s?.phone ?? '',
  notes: s?.notes ?? '',
})

export function SupplierEdit() {
  const { id } = useParams()
  const isNew = !id
  const { t, lang } = useI18n()
  const { email } = useAuth()
  const nav = useNavigate()
  const { supplierMap, items, costs, settings, reload } = useData()
  const record = id ? supplierMap.get(id) : undefined
  const [params] = useSearchParams()
  const { form, setForm, dirty, setDirty, baseVersion, staleBy, reset } = useVersionedForm(
    record,
    (r) => toForm(r, params.get('folder') ?? ''),
    JSON.stringify(record),
  )
  const folderOpts = useFolderFieldOptions('supplier')
  const others = usePresence(id ? `supplier:${id}` : null, email)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)
  const [conflict, setConflict] = useState<null | { deleted: boolean }>(null)
  const set = (k: keyof SupplierForm, v: string) => setForm((f) => ({ ...f, [k]: v }))

  if (!isNew && !record) {
    return (
      <div>
        <Banner kind="error">{t('err.notFound')}</Banner>
        <Link to="/suppliers">{t('common.back')}</Link>
      </div>
    )
  }

  const save = async (expectedVersion: number | null) => {
    if (!form.name.trim()) return setMsg({ kind: 'error', text: t('err.required') })
    const payload = {
      name: form.name.trim(),
      folder_id: form.folder_id || null,
      country: form.country.trim() || null,
      default_discount_pct: parseNum(form.default_discount_pct) ?? 0,
      shipping_pct: parseNum(form.shipping_pct) ?? 0,
      email: form.email.trim() || null,
      phone: form.phone.trim() || null,
      notes: form.notes.trim() || null,
    }
    setBusy(true)
    setMsg(null)
    const res = isNew
      ? await supabase.from('suppliers').insert(payload).select().single()
      : await supabase.from('suppliers').update(payload).eq('id', id!).eq('version', expectedVersion!).select().maybeSingle()
    setBusy(false)
    if (res.error) return setMsg({ kind: 'error', text: t(errorKey(res.error)) })
    if (!res.data) {
      const { data: exists } = await supabase.from('suppliers').select('id').eq('id', id!).maybeSingle()
      return setConflict({ deleted: !exists })
    }
    await reload(['suppliers'])
    if (isNew) nav(`/supplier/${(res.data as Supplier).id}`, { replace: true })
    setDirty(false)
    setMsg({ kind: 'ok', text: t('common.saved') })
  }

  const remove = async () => {
    if (!record || !confirm(t('common.confirmDelete', { name: record.name }))) return
    const { data, error } = await supabase.from('suppliers').delete().eq('id', record.id).eq('version', baseVersion!).select('id')
    if (error) return setMsg({ kind: 'error', text: t(errorKey(error)) })
    if (!data?.length) return setConflict({ deleted: false })
    await reload(['suppliers', 'items'])
    nav(`/suppliers${record.folder_id ? `?f=${record.folder_id}` : ''}`, { replace: true })
  }

  const backFolder = record ? record.folder_id : form.folder_id
  const backTo = `/suppliers${backFolder ? `?f=${backFolder}` : ''}`
  const supplied = id ? items.filter((i) => i.supplier_id === id) : []
  const cur = settings?.currency ?? '€'

  return (
    <form onSubmit={(e: FormEvent) => { e.preventDefault(); save(baseVersion) }}>
      <div className="page-head">
        <button type="button" className="btn ghost" onClick={() => (!dirty || confirm(t('common.leaveUnsaved'))) && nav(backTo)}>
          ← {t('common.back')}
        </button>
        <h1>{isNew ? t('supplier.new') : record!.name}</h1>
        {dirty && <span className="badge warn">{t('common.unsaved')}</span>}
      </div>
      <EditGuards others={others} staleBy={staleBy} onReload={reset} />
      {msg && <Banner kind={msg.kind}>{msg.text}</Banner>}

      <div className="edit-grid">
        <div className="edit-main">
          <Section title={t('item.sectionGeneral')}>
            <div className="fields">
              <Field label={`${t('supplier.name')} *`}>
                <input required value={form.name} onChange={(e) => set('name', e.target.value)} />
              </Field>
              <Field label={t('folder.field')}>
                <Combobox value={form.folder_id} options={folderOpts} onChange={(v) => set('folder_id', v)} />
              </Field>
              <Field label={t('supplier.country')}>
                <input value={form.country} onChange={(e) => set('country', e.target.value)} />
              </Field>
              <Field label={t('supplier.discount')}>
                <NumInput value={form.default_discount_pct} onChange={(v) => set('default_discount_pct', v)} />
              </Field>
              <Field label={t('supplier.shipping')} help={t('supplier.shippingHelp')}>
                <NumInput value={form.shipping_pct} onChange={(v) => set('shipping_pct', v)} />
              </Field>
              <Field label={t('supplier.email')}>
                <input type="email" value={form.email} onChange={(e) => set('email', e.target.value)} />
              </Field>
              <Field label={t('supplier.phone')}>
                <input type="tel" value={form.phone} onChange={(e) => set('phone', e.target.value)} />
              </Field>
              <Field label={t('common.notes')} wide>
                <textarea rows={3} value={form.notes} onChange={(e) => set('notes', e.target.value)} />
              </Field>
            </div>
          </Section>
          {supplied.length > 0 && (
            <Section title={t('supplier.items')}>
              <table className="table compact">
                <tbody>
                  {supplied.map((i) => (
                    <tr key={i.id} className="clickable" onClick={() => nav(`/item/${i.id}`)}>
                      <td><Link to={`/item/${i.id}`}>{i.code}</Link></td>
                      <td>{i.name}</td>
                      <td className="muted hide-sm">{t(`kind.${i.kind}`)}</td>
                      <td className="num">{money(costs.get(i.id)?.cost, cur, lang, 4)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>
          )}
        </div>
        <aside className="edit-side">
          <div className="save-bar">
            <button className="btn primary" disabled={busy}>{busy ? t('common.saving') : t('common.save')}</button>
            {!isNew && <button type="button" className="btn danger ghost" onClick={remove}>{t('common.delete')}</button>}
          </div>
          {record && (
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
            await reload(['suppliers'])
            setDirty(false)
          }}
          onOverwrite={async () => {
            setConflict(null)
            const { data } = await supabase.from('suppliers').select('version').eq('id', id!).single()
            if (data) save(data.version)
          }}
        />
      )}
    </form>
  )
}
