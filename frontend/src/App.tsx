import { useState } from 'react'
import { useAuth } from './auth/useAuth'

function AccessDeniedPage() {
  return (
    <div style={styles.card}>
      <div style={{ fontSize: '3rem' }}>🚫</div>
      <h1 style={{ ...styles.title, color: '#e53e3e' }}>403 - Access Denied</h1>
      <p style={styles.subtitle}>Tài khoản của bạn không có quyền truy cập hệ thống này.</p>
      <p style={styles.hint}>Vui lòng liên hệ quản trị viên để được cấp quyền.</p>
      <a href="/" style={styles.link}>← Quay lại trang chủ</a>
    </div>
  )
}

function App() {
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
    return (
      <div style={styles.card}>
        <div style={styles.spinner} />
        <p style={styles.hint}>Đang tải...</p>
      </div>
    )
  }

  return (
    <div style={styles.card}>
      <h1 style={styles.title}>SSO with Entra ID</h1>
      <p style={styles.subtitle}>BFF Pattern</p>

      {!isAuthenticated ? (
        <div style={styles.section}>
          <p style={styles.text}>Bạn chưa đăng nhập.</p>
          <button style={styles.primaryButton} onClick={handleLogin}>
            🔐 Đăng nhập với Microsoft
          </button>
        </div>
      ) : (
        <div style={styles.section}>
          <div style={styles.userInfo}>
            <div style={styles.avatar}>{user?.name?.[0]?.toUpperCase() ?? '?'}</div>
            <div>
              <p style={styles.userName}>{user?.name}</p>
              <p style={styles.userEmail}>{user?.email}</p>
            </div>
          </div>

          <button style={styles.outlineButton} onClick={handleLogout}>
            Đăng xuất
          </button>

          <hr style={styles.divider} />

          <h2 style={styles.sectionTitle}>Protected API</h2>
          <button style={styles.primaryButton} onClick={fetchWeather}>
            Lấy dữ liệu thời tiết
          </button>

          {fetchError && (
            <p style={styles.error}>Lỗi: {fetchError}</p>
          )}

          {weatherData && (
            <pre style={styles.pre}>{JSON.stringify(weatherData, null, 2)}</pre>
          )}
        </div>
      )}
    </div>
  )
}

const styles: Record<string, React.CSSProperties> = {
  card: {
    width: '100%',
    maxWidth: '480px',
    margin: '0 auto',
    padding: '2.5rem 2rem',
    borderRadius: '16px',
    border: '1px solid rgba(255,255,255,0.1)',
    background: 'rgba(255,255,255,0.04)',
    backdropFilter: 'blur(8px)',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '1rem',
    textAlign: 'center',
  },
  title: {
    margin: 0,
    fontSize: '1.8rem',
    fontWeight: 700,
  },
  subtitle: {
    margin: 0,
    fontSize: '0.9rem',
    opacity: 0.5,
    letterSpacing: '0.05em',
    textTransform: 'uppercase',
  },
  hint: {
    margin: 0,
    fontSize: '0.9rem',
    opacity: 0.6,
  },
  section: {
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '1rem',
  },
  text: {
    margin: 0,
    opacity: 0.8,
  },
  primaryButton: {
    width: '100%',
    padding: '0.75em 1.5em',
    fontSize: '1rem',
    fontWeight: 600,
    background: '#5865f2',
    color: '#fff',
    border: 'none',
    borderRadius: '10px',
    cursor: 'pointer',
    transition: 'opacity 0.2s',
  },
  outlineButton: {
    width: '100%',
    padding: '0.65em 1.5em',
    fontSize: '0.95rem',
    fontWeight: 500,
    background: 'transparent',
    border: '1px solid rgba(255,255,255,0.2)',
    borderRadius: '10px',
    cursor: 'pointer',
    transition: 'border-color 0.2s',
  },
  divider: {
    width: '100%',
    border: 'none',
    borderTop: '1px solid rgba(255,255,255,0.1)',
    margin: '0.5rem 0',
  },
  sectionTitle: {
    margin: 0,
    fontSize: '1.1rem',
    fontWeight: 600,
  },
  userInfo: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.75rem',
    padding: '0.75rem 1rem',
    borderRadius: '10px',
    background: 'rgba(255,255,255,0.06)',
    width: '100%',
    textAlign: 'left',
    boxSizing: 'border-box',
  },
  avatar: {
    width: '40px',
    height: '40px',
    borderRadius: '50%',
    background: '#5865f2',
    color: '#fff',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: 700,
    fontSize: '1.1rem',
    flexShrink: 0,
  },
  userName: {
    margin: 0,
    fontWeight: 600,
    fontSize: '0.95rem',
  },
  userEmail: {
    margin: 0,
    fontSize: '0.8rem',
    opacity: 0.6,
  },
  error: {
    margin: 0,
    color: '#fc8181',
    fontSize: '0.9rem',
  },
  pre: {
    width: '100%',
    textAlign: 'left',
    background: 'rgba(0,0,0,0.3)',
    borderRadius: '8px',
    padding: '1rem',
    fontSize: '0.8rem',
    overflowX: 'auto',
    boxSizing: 'border-box',
  },
  link: {
    color: '#646cff',
    fontWeight: 500,
  },
  spinner: {
    width: '36px',
    height: '36px',
    border: '3px solid rgba(255,255,255,0.15)',
    borderTop: '3px solid #5865f2',
    borderRadius: '50%',
    animation: 'spin 0.8s linear infinite',
  },
}

export default App
