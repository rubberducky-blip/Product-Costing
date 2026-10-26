import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { useData } from '../lib/data'
import { useI18n } from '../lib/i18n'
import { supabase } from '../lib/supabase'
import { SearchBar } from './SearchBar'
import { Banner } from './ui'

export function LangSwitch() {
  const { lang, setLang } = useI18n()
  return (
    <div className="lang-switch">
      {(['el', 'en'] as const).map((l) => (
        <button key={l} className={lang === l ? 'active' : ''} onClick={() => setLang(l)}>
          {l.toUpperCase()}
        </button>
      ))}
    </div>
  )
}

export function Layout() {
  const { t } = useI18n()
  const { email } = useAuth()
  const { loading, error } = useData()
  const links = [
    ['/list/product', 'nav.products', '🛠'],
    ['/list/merchandise', 'nav.merchandise', '📦'],
    ['/list/raw_material', 'nav.raw_material', '🧵'],
    ['/suppliers', 'nav.suppliers', '🚚'],
    ['/settings', 'nav.settings', '⚙'],
  ] as const

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <img src="./icon.svg" alt="" width={28} height={28} />
          <span>{t('app.title')}</span>
        </div>
        <SearchBar />
        <div className="topbar-right">
          <LangSwitch />
          <span className="user muted" title={email}>{email}</span>
          <button className="btn small ghost" onClick={() => supabase.auth.signOut()}>{t('nav.logout')}</button>
        </div>
      </header>
      <nav className="nav">
        {links.map(([to, key, icon]) => (
          <NavLink key={to} to={to} className={({ isActive }) => (isActive ? 'active' : '')}>
            <span className="nav-icon" aria-hidden>{icon}</span>
            <span className="nav-label">{t(key)}</span>
          </NavLink>
        ))}
      </nav>
      <main className="content">
        {error && <Banner kind="error">{error}</Banner>}
        {loading ? <p className="muted">{t('common.loading')}</p> : <Outlet />}
      </main>
    </div>
  )
}
