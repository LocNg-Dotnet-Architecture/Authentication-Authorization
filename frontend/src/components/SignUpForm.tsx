import { useState } from 'react'

type Step = 'email' | 'otp' | 'password'

interface State {
  step: Step
  continuationToken: string
  codeLength: number
  maskedEmail: string
  error: string | null
  loading: boolean
}

export function SignUpForm({ onCancel }: { onCancel: () => void }) {
  const [state, setState] = useState<State>({
    step: 'email',
    continuationToken: '',
    codeLength: 8,
    maskedEmail: '',
    error: null,
    loading: false,
  })

  const [email, setEmail]       = useState('')
  const [otp, setOtp]           = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm]   = useState('')

  const setError = (error: string | null) =>
    setState(s => ({ ...s, error, loading: false }))

  const setLoading = () =>
    setState(s => ({ ...s, loading: true, error: null }))

  const startSignUp = async () => {
    setLoading()
    try {
      const res = await fetch('/auth/native/signup/start', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.description ?? data.error ?? 'Lỗi không xác định'); return }
      setState(s => ({
        ...s,
        step: 'otp',
        loading: false,
        error: null,
        continuationToken: data.continuationToken,
        codeLength: data.codeLength ?? 8,
        maskedEmail: data.maskedEmail ?? email,
      }))
    } catch {
      setError('Không thể kết nối đến server.')
    }
  }

  const resendOtp = async () => {
    setOtp('')
    await startSignUp()  // Restart from /start + /challenge with same email → new OTP sent
  }

  const verifyOtp = async () => {
    setLoading()
    try {
      const res = await fetch('/auth/native/signup/verify-otp', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ continuationToken: state.continuationToken, otp }),
      })
      const data = await res.json()
      if (!res.ok) {
        const msg = data.description ?? data.error ?? 'Mã không đúng'
        setError(msg + ' — Nhấn "Gửi lại mã" để nhận mã mới nếu mã đã hết hạn.')
        return
      }
      setState(s => ({
        ...s,
        step: 'password',
        loading: false,
        error: null,
        continuationToken: data.continuationToken,
      }))
    } catch {
      setError('Không thể kết nối đến server.')
    }
  }

  const completeSignUp = async () => {
    if (password !== confirm) { setError('Mật khẩu xác nhận không khớp.'); return }
    setLoading()
    try {
      const res = await fetch('/auth/native/signup/complete', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ continuationToken: state.continuationToken, password }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.description ?? data.error ?? 'Không thể tạo tài khoản'); return }
      // Session cookie đã được set bởi BFF - reload để useAuth() nhận trạng thái mới
      window.location.href = '/'
    } catch {
      setError('Không thể kết nối đến server.')
    }
  }

  const inputStyle: React.CSSProperties = {
    display: 'block',
    width: '100%',
    padding: '0.4rem 0.6rem',
    marginTop: '0.25rem',
    marginBottom: '0.75rem',
    boxSizing: 'border-box',
  }

  return (
    <div style={{
      border: '1px solid #ccc',
      padding: '1.5rem',
      maxWidth: 380,
      borderRadius: 6,
      fontFamily: 'sans-serif',
    }}>
      <h2 style={{ marginTop: 0, marginBottom: '1rem' }}>Đăng ký tài khoản</h2>

      {state.step === 'email' && (
        <>
          <label>
            Email
            <input
              style={inputStyle}
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && email && !state.loading && startSignUp()}
              autoFocus
            />
          </label>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button onClick={startSignUp} disabled={state.loading || !email}>
              {state.loading ? 'Đang gửi…' : 'Tiếp tục'}
            </button>
            <button onClick={onCancel}>Hủy</button>
          </div>
        </>
      )}

      {state.step === 'otp' && (
        <>
          <p style={{ marginTop: 0 }}>
            Mã xác minh ({state.codeLength} chữ số) đã gửi đến{' '}
            <strong>{state.maskedEmail}</strong>.
          </p>
          <label>
            Mã xác minh
            <input
              style={inputStyle}
              type="text"
              inputMode="numeric"
              maxLength={state.codeLength}
              value={otp}
              onChange={e => setOtp(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && otp && !state.loading && verifyOtp()}
              autoFocus
            />
          </label>
          <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
            <button onClick={verifyOtp} disabled={state.loading || !otp}>
              {state.loading ? 'Đang xác minh…' : 'Xác minh'}
            </button>
            <button onClick={resendOtp} disabled={state.loading}
              style={{ background: 'none', border: '1px solid #999', cursor: 'pointer' }}>
              Gửi lại mã
            </button>
            <button onClick={onCancel}>Hủy</button>
          </div>
          <p style={{ fontSize: '0.8rem', color: '#666', marginBottom: 0 }}>
            Mã có hiệu lực trong 10 phút. Kiểm tra cả thư mục Spam.
          </p>
        </>
      )}

      {state.step === 'password' && (
        <>
          <label>
            Mật khẩu mới
            <input
              style={inputStyle}
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              autoFocus
            />
          </label>
          <label>
            Xác nhận mật khẩu
            <input
              style={inputStyle}
              type="password"
              value={confirm}
              onChange={e => setConfirm(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && password && confirm && !state.loading && completeSignUp()}
            />
          </label>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button onClick={completeSignUp} disabled={state.loading || !password || !confirm}>
              {state.loading ? 'Đang tạo tài khoản…' : 'Tạo tài khoản'}
            </button>
            <button onClick={onCancel}>Hủy</button>
          </div>
        </>
      )}

      {state.error && (
        <p style={{ color: 'red', marginTop: '0.75rem', marginBottom: 0 }}>
          {state.error}
        </p>
      )}
    </div>
  )
}
