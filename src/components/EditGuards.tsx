import { useI18n } from '../lib/i18n'
import { Banner } from './ui'

interface Props {
  others: string[]
  /** Newer version saved by someone else while this form has unsaved edits. */
  staleBy: string | null
  onReload: () => void
}

/** Warns about other users viewing the record and about newer saved versions. */
export function EditGuards({ others, staleBy, onReload }: Props) {
  const { t } = useI18n()
  return (
    <>
      {others.length > 0 && <Banner kind="info">👥 {t('presence.editing', { who: others.join(', ') })}</Banner>}
      {staleBy !== null && (
        <Banner kind="warn">
          {t('presence.changed', { who: staleBy || '?' })}{' '}
          <button type="button" className="btn small" onClick={onReload}>
            {t('presence.reload')}
          </button>
        </Banner>
      )}
    </>
  )
}

export function ConflictDialog({ who, deleted, onReload, onOverwrite, onClose }: {
  who: string
  deleted?: boolean
  onReload: () => void
  onOverwrite: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true">
      <div className="modal">
        <h2>{t('conflict.title')}</h2>
        <p>{deleted ? t('conflict.deleted') : t('conflict.body', { who: who || '?' })}</p>
        <div className="actions">
          <button className="btn" onClick={onClose}>{t('common.cancel')}</button>
          {!deleted && <button className="btn" onClick={onReload}>{t('conflict.reload')}</button>}
          {!deleted && <button className="btn danger" onClick={onOverwrite}>{t('conflict.overwrite')}</button>}
        </div>
      </div>
    </div>
  )
}
