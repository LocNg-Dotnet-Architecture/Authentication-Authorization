import { useState, useEffect } from 'react'

interface User {
  name: string
  email: string
}

interface AuthState {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
}

export function useAuth(): AuthState {
  const [state, setState] = useState<AuthState>({
    user: null,
    isLoading: true,
    isAuthenticated: false,
  })

  useEffect(() => {
    fetch('/auth/me', { credentials: 'include' })
      .then(res => {
        if (res.ok) return res.json()
        throw new Error('Not authenticated')
      })
      .then((user: User) => {
        setState({ user, isLoading: false, isAuthenticated: true })
      })
      .catch(() => {
        setState({ user: null, isLoading: false, isAuthenticated: false })
      })
  }, [])

  return state
}
