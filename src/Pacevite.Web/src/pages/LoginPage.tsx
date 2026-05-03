import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import { useAuth } from '@/hooks/useAuth'
import type { AuthResponse } from '@/lib/types'

export function LoginPage() {
  const navigate = useNavigate()
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await apiClient.post<AuthResponse>('/auth/login', { email, password })
      return data
    },
    onSuccess: (data) => {
      login(data.userId, data.email, data.token)
      navigate('/dashboard')
    },
  })

  function handleSubmit(e: { preventDefault(): void }) {
    e.preventDefault()
    mutation.mutate()
  }

  return (
    <main className="min-h-screen flex items-center justify-center bg-bg">
      <div className="w-full max-w-sm bg-surface rounded-lg shadow p-8 space-y-6">
        <h1 className="text-2xl font-semibold text-primary">Sign in to Pacevite</h1>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="email" className="block text-sm font-medium text-secondary mb-1">Email</label>
            <input
              id="email"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={e => setEmail(e.target.value)}
              className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </div>

          <div>
            <label htmlFor="password" className="block text-sm font-medium text-secondary mb-1">Password</label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={e => setPassword(e.target.value)}
              className="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface text-primary focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </div>

          {mutation.isError && (
            <p className="text-sm text-red-600">Invalid email or password.</p>
          )}

          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full bg-action text-action-fg rounded-md px-4 py-2 text-sm font-medium hover:bg-action-hover disabled:opacity-50"
          >
            {mutation.isPending ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        <p className="text-sm text-secondary text-center">
          No account?{' '}
          <Link to="/register" className="font-medium text-primary underline">
            Register
          </Link>
        </p>
      </div>
    </main>
  )
}
