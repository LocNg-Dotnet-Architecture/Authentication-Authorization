import { useState } from 'react'
import { useAuth } from './auth/useAuth'

// Trang Access Denied - hiển thị khi user không thuộc group được phép
function AccessDeniedPage() {
  return (
    <div style={{ padding: '2rem', fontFamily: 'sans-serif', textAlign: 'center' }}>
      <h1 style={{ color: '#c00' }}>403 - Access Denied</h1>
      <p>Tài khoản của bạn không có quyền truy cập hệ thống này.</p>
      <p style={{ color: '#666', fontSize: '0.9rem' }}>
        Vui lòng liên hệ quản trị viên để được cấp quyền.
      </p>
      <a href="/" style={{ color: '#0066cc' }}>← Quay lại trang chủ</a>
    </div>
  )
}

function App() {
  // Render trang access-denied nếu BFF redirect về /access-denied
  if (window.location.pathname === '/access-denied') {
    return <AccessDeniedPage />
  }

  const { user, isLoading, isAuthenticated } = useAuth()
  const [weatherData, setWeatherData] = useState<unknown[] | null>(null)
  const [fetchError, setFetchError] = useState<string | null>(null)

  const handleLogin = () => {
    window.location.href = '/auth/login?returnUrl=/'
  }

  const getCsrfToken = () =>
    document.cookie
      .split('; ')
      .find(row => row.startsWith('XSRF-TOKEN='))
      ?.split('=')[1] ?? ''

  // Form POST thay vì fetch() để browser follow đúng OIDC logout redirect chain
  // (clear local cookie → Entra ID end-session → redirect về frontend)
  const handleLogout = () => {
    const form = document.createElement('form')
    form.method = 'POST'
    form.action = '/auth/logout'

    const csrfInput = document.createElement('input')
    csrfInput.type = 'hidden'
    csrfInput.name = '__RequestVerificationToken'
    csrfInput.value = getCsrfToken()

    form.appendChild(csrfInput)
    document.body.appendChild(form)
    form.submit()
  }

  const fetchWeather = async () => {
    setFetchError(null)
    try {
      const res = await fetch('/api/weatherforecast', {
        credentials: 'include',
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data = await res.json()
      setWeatherData(data)
    } catch (err) {
      setFetchError(String(err))
    }
  }

  if (isLoading) {
    return <div style={{ padding: '2rem' }}>Loading...</div>
  }

  return (
    <div style={{ padding: '2rem', fontFamily: 'sans-serif' }}>
      <h1>SSO with Entra ID (BFF Pattern)</h1>

      {!isAuthenticated ? (
        <div>
          <p>You are not logged in.</p>
          <button onClick={handleLogin}>Login with Microsoft</button>
        </div>
      ) : (
        <div>
          <p>
            Logged in as: <strong>{user?.name}</strong> ({user?.email})
          </p>
          <button onClick={handleLogout}>Logout</button>

          <hr />

          <h2>Protected API Data</h2>
          <button onClick={fetchWeather}>Fetch Weather (via BFF)</button>
          {fetchError && <p style={{ color: 'red' }}>Error: {fetchError}</p>}
          {weatherData && (
            <pre>{JSON.stringify(weatherData, null, 2)}</pre>
          )}
        </div>
      )}
    </div>
  )
}

export default App
