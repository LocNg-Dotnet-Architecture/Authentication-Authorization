import { useState, useEffect } from 'react'

interface User {
  name: string
  email: string
  identityProvider: string  // "google.com" | "local"
}

interface AuthState {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
}

const STORAGE_KEY = 'last_identity_provider'

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
        // Persist provider so we can show the right login button after logout
        localStorage.setItem(STORAGE_KEY, user.identityProvider)
        setState({ user, isLoading: false, isAuthenticated: true })
      })
      .catch(() => {
        setState({ user: null, isLoading: false, isAuthenticated: false })
      })
  }, [])

  return state
}

export function getLastIdentityProvider(): string {
  return localStorage.getItem(STORAGE_KEY) ?? 'local'
}
