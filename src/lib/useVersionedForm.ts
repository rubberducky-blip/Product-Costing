import { useCallback, useEffect, useRef, useState } from 'react'

interface Versioned {
  version: number
  updated_by_email: string | null
}

// row keys (e.g. recipe lines) only identify rows on screen and are not part of the values
const comparable = (form: unknown) => JSON.stringify(form, (k, v) => (k === 'key' ? undefined : v))

/**
 * Form state for a record that other users may change at the same time.
 * - The form is dirty only while its values differ from the ones it started with: editing a field
 *   and then putting the old value back counts as no change.
 * - While the form is clean it follows the live record (realtime updates flow in).
 * - Once the user edits, it freezes; if a newer version arrives, `staleBy` names who saved it.
 * `baseVersion` is sent with the save so the database can reject stale writes.
 */
export function useVersionedForm<R extends Versioned, F>(
  record: R | undefined,
  toForm: (r: R | undefined) => F,
  sourceKey: string,
) {
  const [form, setFormState] = useState<F>(() => toForm(record))
  const [initial, setInitial] = useState<F>(form)
  const [baseVersion, setBaseVersion] = useState<number | null>(record?.version ?? null)
  const dirty = comparable(form) !== comparable(initial)
  const toFormRef = useRef(toForm)
  toFormRef.current = toForm
  const recordRef = useRef(record)
  recordRef.current = record

  /** Back to the record's current values: discards edits (also used to load a newer version). */
  const reset = useCallback(() => {
    const f = toFormRef.current(recordRef.current)
    setFormState(f)
    setInitial(f)
    setBaseVersion(recordRef.current?.version ?? null)
  }, [])

  // Follow the live record while there are no local edits (also re-syncs right after a save).
  useEffect(() => {
    if (!dirty) reset()
  }, [sourceKey, dirty, reset])

  const setForm = useCallback((update: F | ((f: F) => F)) => setFormState(update), [])
  /** After a save: the current values become the starting point, so the form is clean again. */
  const markSaved = useCallback(() => setInitial(form), [form])

  const staleBy =
    dirty && record && baseVersion !== null && record.version > baseVersion ? record.updated_by_email ?? '' : null

  // warn before closing the tab with unsaved edits
  useEffect(() => {
    if (!dirty) return
    const h = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', h)
    return () => window.removeEventListener('beforeunload', h)
  }, [dirty])

  return { form, setForm, dirty, markSaved, baseVersion, setBaseVersion, staleBy, reset }
}
