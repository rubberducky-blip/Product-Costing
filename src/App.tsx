import { HashRouter, Navigate, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { AuthProvider, useAuth } from './lib/auth'
import { DataProvider } from './lib/data'
import { I18nProvider, useI18n } from './lib/i18n'
import { ItemEdit } from './pages/ItemEdit'
import { ItemsList } from './pages/ItemsList'
import { Login } from './pages/Login'
import { Settings } from './pages/Settings'
import { SupplierEdit } from './pages/SupplierEdit'
import { Suppliers } from './pages/Suppliers'

function Gate() {
  const { session, loading } = useAuth()
  const { t } = useI18n()
  if (loading) return <p className="muted center">{t('common.loading')}</p>
  if (!session) return <Login />
  return (
    <DataProvider>
      <HashRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<Navigate to="/list/product" replace />} />
            <Route path="list/:kind" element={<ItemsList />} />
            <Route path="item/new/:kind" element={<ItemEdit />} />
            <Route path="item/:id" element={<ItemEdit />} />
            <Route path="suppliers" element={<Suppliers />} />
            <Route path="supplier/new" element={<SupplierEdit />} />
            <Route path="supplier/:id" element={<SupplierEdit />} />
            <Route path="settings" element={<Settings />} />
            <Route path="*" element={<Navigate to="/list/product" replace />} />
          </Route>
        </Routes>
      </HashRouter>
    </DataProvider>
  )
}

export default function App() {
  return (
    <I18nProvider>
      <AuthProvider>
        <Gate />
      </AuthProvider>
    </I18nProvider>
  )
}
