import { useCallback, useEffect, useRef, useState } from 'react'

interface Versioned {
  version: number
  updated_by_email: string | null
}

/**
 * Form state for a record that other users may change at the same time.
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
  const [baseVersion, setBaseVersion] = useState<number | null>(record?.version ?? null)
  const [dirty, setDirty] = useState(false)
  const toFormRef = useRef(toForm)
  toFormRef.current = toForm
  const recordRef = useRef(record)
  recordRef.current = record

  const reset = useCallback(() => {
    setFormState(toFormRef.current(recordRef.current))
    setBaseVersion(recordRef.current?.version ?? null)
    setDirty(false)
  }, [])

  // Follow the live record while there are no local edits (also re-syncs right after a save).
  useEffect(() => {
    if (!dirty) reset()
  }, [sourceKey, dirty, reset])

  const setForm = useCallback((update: F | ((f: F) => F)) => {
    setFormState(update)
    setDirty(true)
  }, [])

  const staleBy =
    dirty && record && baseVersion !== null && record.version > baseVersion ? record.updated_by_email ?? '' : null

  // warn before closing the tab with unsaved edits
  useEffect(() => {
    if (!dirty) return
    const h = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', h)
    return () => window.removeEventListener('beforeunload', h)
  }, [dirty])

  return { form, setForm, dirty, setDirty, baseVersion, setBaseVersion, staleBy, reset }
}
