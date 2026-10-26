import { useState, type FormEvent } from 'react'
import { LangSwitch } from '../components/Layout'
import { Banner } from '../components/ui'
import { useI18n } from '../lib/i18n'
import { errorKey, supabase, supabaseConfigured } from '../lib/supabase'

export function Login() {
  const { t } = useI18n()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    const { error } = await supabase.auth.signInWithPassword({ email: email.trim(), password })
    setBusy(false)
    if (error) setErr(t(errorKey(error)))
  }

  return (
    <div className="login-wrap">
      <form className="card login" onSubmit={submit}>
        <div className="login-head">
          <img src="./icon.svg" alt="" width={40} height={40} />
          <h1>{t('app.title')}</h1>
          <LangSwitch />
        </div>
        {!supabaseConfigured && <Banner kind="error">{t('login.notConfigured')}</Banner>}
        {err && <Banner kind="error">{err}</Banner>}
        <label className="field">
          <span className="field-label">{t('login.email')}</span>
          <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label className="field">
          <span className="field-label">{t('login.password')}</span>
          <input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <button className="btn primary" disabled={busy || !supabaseConfigured}>{t('login.submit')}</button>
      </form>
    </div>
  )
}
