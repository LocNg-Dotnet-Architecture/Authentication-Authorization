import { useState } from 'react'

type Step = 'email' | 'otp' | 'password' | 'done'

interface State {
  step: Step
  continuationToken: string
  codeLength: number
  maskedEmail: string
  error: string | null
  loading: boolean
}

interface Props {
  initialEmail?: string  // pre-fill nếu user đã đăng nhập
  onCancel: () => void
  onDone?: () => void
}

export function ChangePasswordForm({ initialEmail, onCancel, onDone }: Props) {
  const [state, setState] = useState<State>({
    step: initialEmail ? 'email' : 'email',
    continuationToken: '',
    codeLength: 8,
    maskedEmail: '',
    error: null,
    loading: false,
  })

  const [email, setEmail]           = useState(initialEmail ?? '')
  const [otp, setOtp]               = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm]       = useState('')

  const setError = (error: string | null) =>
    setState(s => ({ ...s, error, loading: false }))

  const setLoading = () =>
    setState(s => ({ ...s, loading: true, error: null }))

  const startReset = async () => {
    setLoading()
    try {
      const res = await fetch('/auth/native/resetpassword/start', {
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

  const verifyOtp = async () => {
    setLoading()
    try {
      const res = await fetch('/auth/native/resetpassword/verify-otp', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ continuationToken: state.continuationToken, otp }),
      })
      const data = await res.json()
      if (!res.ok) {
        setError(data.description ?? data.error ?? 'Mã không đúng')
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

  const completeReset = async () => {
    if (newPassword !== confirm) { setError('Mật khẩu xác nhận không khớp.'); return }
    setLoading()
    try {
      const res = await fetch('/auth/native/resetpassword/complete', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          continuationToken: state.continuationToken,
          newPassword,
          confirmPassword: confirm,
        }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.description ?? data.error ?? 'Không thể đổi mật khẩu'); return }
      setState(s => ({ ...s, step: 'done', loading: false, error: null }))
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
      <h2 style={{ marginTop: 0, marginBottom: '1rem' }}>Đổi mật khẩu</h2>

      {state.step === 'email' && (
        <>
          <p style={{ marginTop: 0, color: '#555', fontSize: '0.9rem' }}>
            Nhập email của bạn để nhận mã xác minh.
          </p>
          <label>
            Email
            <input
              style={inputStyle}
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && email && !state.loading && startReset()}
              autoFocus={!initialEmail}
            />
          </label>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button onClick={startReset} disabled={state.loading || !email}>
              {state.loading ? 'Đang gửi…' : 'Gửi mã xác minh'}
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
            <button onClick={startReset} disabled={state.loading}
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
              value={newPassword}
              onChange={e => setNewPassword(e.target.value)}
              autoFocus
            />
          </label>
          <label>
            Xác nhận mật khẩu mới
            <input
              style={inputStyle}
              type="password"
              value={confirm}
              onChange={e => setConfirm(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && newPassword && confirm && !state.loading && completeReset()}
            />
          </label>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button onClick={completeReset} disabled={state.loading || !newPassword || !confirm}>
              {state.loading ? 'Đang đổi mật khẩu…' : 'Đổi mật khẩu'}
            </button>
            <button onClick={onCancel}>Hủy</button>
          </div>
        </>
      )}

      {state.step === 'done' && (
        <>
          <p style={{ color: 'green', fontWeight: 500 }}>
            ✓ Đổi mật khẩu thành công!
          </p>
          <button onClick={onDone ?? onCancel}>Đóng</button>
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
