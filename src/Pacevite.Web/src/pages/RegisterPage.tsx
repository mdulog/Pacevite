import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import { useAuth } from '@/hooks/useAuth'
import type { AuthResponse } from '@/lib/types'

type FieldErrors = { email?: string; password?: string }
type ApiErrorResponse = { status?: number; data?: { errors?: Record<string, string[]>; detail?: string; title?: string } }

export function RegisterPage() {
  const navigate = useNavigate()
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})

  const mutation = useMutation({
    mutationFn: async () => {
      const { data } = await apiClient.post<AuthResponse>('/auth/register', { email, password })
      return data
    },
    onSuccess: (data) => {
      login(data.userId, data.email, data.token)
      navigate('/dashboard')
    },
    onError: (err: unknown) => {
      const res = (err as { response?: ApiErrorResponse }).response
      setFieldErrors({})
      setError(null)

      if (res?.status === 409) {
        setFieldErrors({ email: 'This email is already registered. Try signing in instead.' })
      } else if (res?.status === 400 && res?.data?.errors) {
        const apiErrors = res.data.errors
        setFieldErrors({
          email: apiErrors['Email']?.[0],
          password: apiErrors['Password']?.[0],
        })
      } else if (!res) {
        setError('Unable to reach the server. Check your connection and try again.')
      } else {
        setError(res?.data?.detail ?? res?.data?.title ?? 'Registration failed. Please try again.')
      }
    },
  })

  function handleSubmit(e: { preventDefault(): void }) {
    e.preventDefault()
    setError(null)
    setFieldErrors({})
    mutation.mutate()
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-bg">
      <div className="w-full max-w-sm bg-surface rounded-lg shadow p-8 space-y-6">
        <h1 className="text-2xl font-semibold text-primary">Create your account</h1>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="email" className="block text-sm font-medium text-secondary mb-1">Email</label>
            <input
              id="email"
              type="email"
              required
              value={email}
              onChange={e => { setEmail(e.target.value); setFieldErrors(f => ({ ...f, email: undefined })) }}
              className={`w-full border rounded-md px-3 py-2 text-sm bg-surface text-primary focus:outline-none focus:ring-2 focus:ring-primary ${fieldErrors.email ? 'border-red-500' : 'border-border'}`}
            />
            {fieldErrors.email && <p className="mt-1 text-xs text-red-600">{fieldErrors.email}</p>}
          </div>

          <div>
            <label htmlFor="password" className="block text-sm font-medium text-secondary mb-1">
              Password <span className="text-muted font-normal">(min. 8 chars, requires special char)</span>
            </label>
            <input
              id="password"
              type="password"
              required
              minLength={8}
              value={password}
              onChange={e => { setPassword(e.target.value); setFieldErrors(f => ({ ...f, password: undefined })) }}
              className={`w-full border rounded-md px-3 py-2 text-sm bg-surface text-primary focus:outline-none focus:ring-2 focus:ring-primary ${fieldErrors.password ? 'border-red-500' : 'border-border'}`}
            />
            {fieldErrors.password && <p className="mt-1 text-xs text-red-600">{fieldErrors.password}</p>}
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}

          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full bg-action text-action-fg rounded-md px-4 py-2 text-sm font-medium hover:bg-action-hover disabled:opacity-50"
          >
            {mutation.isPending ? 'Creating account…' : 'Create account'}
          </button>
        </form>

        <p className="text-sm text-secondary text-center">
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-primary underline">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  )
}
