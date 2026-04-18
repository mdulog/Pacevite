import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Routes, Route } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { LoginPage } from '@/pages/LoginPage'
import { renderWithProviders } from '@/test/render'

function renderLoginPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<div>dashboard</div>} />
    </Routes>,
    { initialEntries: ['/login'] }
  )
}

describe('LoginPage', () => {
  it('navigates to /dashboard on successful login', async () => {
    renderLoginPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'runner@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'SecurePass1!')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => {
      expect(screen.getByText('dashboard')).toBeInTheDocument()
    })
  })

  it('shows error message on invalid credentials', async () => {
    server.use(
      http.post('http://localhost/api/auth/login', () =>
        new HttpResponse(null, { status: 401 })
      )
    )

    renderLoginPage()

    await userEvent.type(screen.getByLabelText(/email/i), 'runner@example.com')
    await userEvent.type(screen.getByLabelText(/password/i), 'WrongPassword!')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => {
      expect(screen.getByText('Invalid email or password.')).toBeInTheDocument()
    })
  })
})
