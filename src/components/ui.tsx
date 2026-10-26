import type { ReactNode, SelectHTMLAttributes } from 'react'

export function Field({ label, help, children, wide }: { label: string; help?: ReactNode; children: ReactNode; wide?: boolean }) {
  return (
    <label className={`field${wide ? ' wide' : ''}`}>
      <span className="field-label">{label}</span>
      {children}
      {help && <span className="field-help">{help}</span>}
    </label>
  )
}

/** Numeric input kept as a string so users can type "12," or "" freely; accepts comma decimals. */
export function NumInput({ value, onChange, placeholder, required }: {
  value: string
  onChange: (v: string) => void
  placeholder?: string
  required?: boolean
}) {
  return (
    <input
      type="text"
      inputMode="decimal"
      value={value}
      placeholder={placeholder}
      required={required}
      onChange={(e) => onChange(e.target.value.replace(/[^\d.,-]/g, ''))}
    />
  )
}

export function Select(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} />
}

export function Section({ title, children, actions }: { title: string; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="card">
      <div className="card-head">
        <h2>{title}</h2>
        {actions}
      </div>
      {children}
    </section>
  )
}

export function Row({ label, value, strong, muted }: { label: ReactNode; value: ReactNode; strong?: boolean; muted?: boolean }) {
  return (
    <div className={`kv${strong ? ' strong' : ''}${muted ? ' muted' : ''}`}>
      <span>{label}</span>
      <span>{value}</span>
    </div>
  )
}

export function Banner({ kind, children }: { kind: 'info' | 'warn' | 'error' | 'ok'; children: ReactNode }) {
  return <div className={`banner ${kind}`}>{children}</div>
}
