import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { LangSwitch } from '../components/Layout'
import { ConflictDialog, EditGuards } from '../components/EditGuards'
import { Banner, Field, NumInput, Section } from '../components/ui'
import { useAuth } from '../lib/auth'
import { useData } from '../lib/data'
import { dateTime, parseNum } from '../lib/format'
import { useI18n } from '../lib/i18n'
import { usePresence } from '../lib/presence'
import { errorKey, supabase } from '../lib/supabase'
import type { BackupMeta, RoundingMode, Settings as SettingsRow } from '../lib/types'
import { useVersionedForm } from '../lib/useVersionedForm'

interface SettingsForm {
  labor_rate_per_hour: string
  overhead_rate_per_hour: string
  default_markup_pct: string
  rounding_step: string
  rounding_mode: RoundingMode
  currency: string
  backup_retention_days: string
}

const toForm = (s: SettingsRow | undefined): SettingsForm => ({
  labor_rate_per_hour: String(s?.labor_rate_per_hour ?? 0),
  overhead_rate_per_hour: String(s?.overhead_rate_per_hour ?? 0),
  default_markup_pct: String(s?.default_markup_pct ?? 100),
  rounding_step: String(s?.rounding_step ?? 0.05),
  rounding_mode: s?.rounding_mode ?? 'nearest',
  currency: s?.currency ?? '€',
  backup_retention_days: String(s?.backup_retention_days ?? 30),
})

export function Settings() {
  const { t } = useI18n()
  const { email } = useAuth()
  const { settings, reload } = useData()
  const record = settings ?? undefined
  const { form, setForm, dirty, markSaved, baseVersion, staleBy, reset } = useVersionedForm(record, toForm, JSON.stringify(record))
  const others = usePresence('settings', email)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)
  const [conflict, setConflict] = useState(false)
  const set = <K extends keyof SettingsForm>(k: K, v: SettingsForm[K]) => setForm((f) => ({ ...f, [k]: v }))
  const cur = form.currency || '€'

  const save = async (expectedVersion: number | null) => {
    setBusy(true)
    setMsg(null)
    const { data, error } = await supabase
      .from('app_settings')
      .update({
        labor_rate_per_hour: parseNum(form.labor_rate_per_hour) ?? 0,
        overhead_rate_per_hour: parseNum(form.overhead_rate_per_hour) ?? 0,
        default_markup_pct: parseNum(form.default_markup_pct) ?? 0,
        rounding_step: parseNum(form.rounding_step) ?? 0,
        rounding_mode: form.rounding_mode,
        currency: form.currency.trim() || '€',
        backup_retention_days: Math.max(1, Math.round(parseNum(form.backup_retention_days) ?? 30)),
      })
      .eq('id', 1)
      .eq('version', expectedVersion!)
      .select()
      .maybeSingle()
    setBusy(false)
    if (error) return setMsg({ kind: 'error', text: t(errorKey(error)) })
    if (!data) return setConflict(true)
    await reload(['app_settings'])
    markSaved()
    setMsg({ kind: 'ok', text: t('common.saved') })
  }

  return (
    <div>
      <div className="page-head">
        <h1>{t('settings.title')}</h1>
        {dirty && <span className="badge warn">{t('common.unsaved')}</span>}
      </div>
      <EditGuards others={others} staleBy={staleBy} onReload={reset} />
      {msg && <Banner kind={msg.kind}>{msg.text}</Banner>}

      <form onSubmit={(e: FormEvent) => { e.preventDefault(); save(baseVersion) }}>
        <Section title={t('settings.costs')}>
          <div className="fields">
            <Field label={`${t('settings.laborRate')} (${cur})`} help={t('settings.laborRateHelp')}>
              <NumInput value={form.labor_rate_per_hour} onChange={(v) => set('labor_rate_per_hour', v)} />
            </Field>
            <Field label={`${t('settings.overheadRate')} (${cur})`} help={t('settings.overheadRateHelp')}>
              <NumInput value={form.overhead_rate_per_hour} onChange={(v) => set('overhead_rate_per_hour', v)} />
            </Field>
          </div>
        </Section>
        <Section title={t('settings.pricing')}>
          <div className="fields">
            <Field label={t('settings.defaultMarkup')}>
              <NumInput value={form.default_markup_pct} onChange={(v) => set('default_markup_pct', v)} />
            </Field>
            <Field label={`${t('settings.roundingStep')} (${cur})`}>
              <NumInput value={form.rounding_step} onChange={(v) => set('rounding_step', v)} />
            </Field>
            <Field label={t('settings.roundingMode')}>
              <select value={form.rounding_mode} onChange={(e) => set('rounding_mode', e.target.value as RoundingMode)}>
                {(['nearest', 'up', 'down'] as const).map((m) => <option key={m} value={m}>{t(`settings.round.${m}`)}</option>)}
              </select>
            </Field>
            <Field label={t('settings.currency')}>
              <input value={form.currency} maxLength={5} onChange={(e) => set('currency', e.target.value)} />
            </Field>
            <Field label={t('settings.retention')}>
              <NumInput value={form.backup_retention_days} onChange={(v) => set('backup_retention_days', v)} />
            </Field>
          </div>
        </Section>
        <div className="save-bar">
          <button className="btn primary" disabled={busy || !dirty}>{busy ? t('common.saving') : t('common.save')}</button>
            <button type="button" className="btn" disabled={busy || !dirty} onClick={() => { reset(); setMsg(null) }}>{t('common.discard')}</button>
        </div>
      </form>

      <Section title={t('settings.language')}>
        <LangSwitch />
      </Section>

      <Backups />

      {conflict && (
        <ConflictDialog
          who={settings?.updated_by_email ?? ''}
          onClose={() => setConflict(false)}
          onReload={async () => {
            setConflict(false)
            await reload(['app_settings'])
            markSaved()
          }}
          onOverwrite={async () => {
            setConflict(false)
            const { data } = await supabase.from('app_settings').select('version').eq('id', 1).single()
            if (data) save(data.version)
          }}
        />
      )}
    </div>
  )
}

function download(name: string, data: unknown) {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = name
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 1000)
}

const stamp = (d: string) => d.slice(0, 16).replace(/[:T]/g, '-')

function Backups() {
  const { t, lang } = useI18n()
  const { reload } = useData()
  const [list, setList] = useState<BackupMeta[]>([])
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  const load = useCallback(async () => {
    const { data, error } = await supabase
      .from('backups')
      .select('id, created_at, kind, created_by_email, size_bytes')
      .order('created_at', { ascending: false })
      .limit(200)
    if (error) setMsg({ kind: 'error', text: t(errorKey(error)) })
    else setList(data as BackupMeta[])
  }, [t])

  useEffect(() => {
    load()
  }, [load])

  const run = async (fn: () => Promise<{ error: { message?: string; code?: string } | null }>, okText?: string) => {
    setBusy(true)
    setMsg(null)
    const { error } = await fn()
    setBusy(false)
    if (error) setMsg({ kind: 'error', text: t(errorKey(error)) })
    else if (okText) setMsg({ kind: 'ok', text: okText })
    await load()
  }

  const backupNow = () => run(async () => supabase.rpc('create_backup', { p_kind: 'manual' }), t('settings.backupCreated'))

  const downloadBackup = async (b: BackupMeta) => {
    const { data, error } = await supabase.from('backups').select('data').eq('id', b.id).single()
    if (error) return setMsg({ kind: 'error', text: t(errorKey(error)) })
    download(`product-costing-backup-${stamp(b.created_at)}.json`, data.data)
  }

  const restore = (b: BackupMeta) => {
    if (!confirm(t('settings.confirmRestore'))) return
    run(async () => {
      const res = await supabase.rpc('restore_backup', { p_id: b.id })
      await reload()
      return res
    }, t('settings.restored'))
  }

  const restoreFile = async (file: File) => {
    let parsed: unknown
    try {
      parsed = JSON.parse(await file.text())
    } catch {
      return setMsg({ kind: 'error', text: t('err.invalidBackup') })
    }
    if (!confirm(t('settings.confirmRestore'))) return
    run(async () => {
      const res = await supabase.rpc('restore_backup_data', { p_data: parsed })
      await reload()
      return res
    }, t('settings.restored'))
  }

  const remove = (b: BackupMeta) => {
    if (!confirm(t('common.confirmDelete', { name: dateTime(b.created_at, lang) }))) return
    run(async () => supabase.from('backups').delete().eq('id', b.id))
  }

  return (
    <Section
      title={t('settings.backups')}
      actions={
        <div className="actions">
          <button className="btn" disabled={busy} onClick={backupNow}>{t('settings.backupNow')}</button>
          <button className="btn ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{t('settings.restoreFile')}</button>
          <input
            ref={fileRef}
            type="file"
            accept="application/json,.json"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0]
              e.target.value = ''
              if (f) restoreFile(f)
            }}
          />
        </div>
      }
    >
      <p className="muted small">{t('settings.backupsHelp')}</p>
      {msg && <Banner kind={msg.kind}>{msg.text}</Banner>}
      {list.length === 0 ? (
        <p className="muted">{t('settings.noBackups')}</p>
      ) : (
        <table className="table compact">
          <tbody>
            {list.map((b) => (
              <tr key={b.id}>
                <td>{dateTime(b.created_at, lang)}</td>
                <td><span className="badge">{t(`backup.${b.kind}`)}</span></td>
                <td className="muted hide-sm">{b.created_by_email ?? ''}</td>
                <td className="muted hide-sm num">{b.size_bytes ? `${Math.ceil(b.size_bytes / 1024)} KB` : ''}</td>
                <td className="row-actions">
                  <button className="btn small" onClick={() => downloadBackup(b)}>{t('settings.download')}</button>
                  <button className="btn small" disabled={busy} onClick={() => restore(b)}>{t('settings.restore')}</button>
                  <button className="btn small ghost danger" disabled={busy} onClick={() => remove(b)} aria-label={t('common.delete')}>✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Section>
  )
}
