import { describe, it, expect } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '@/test/render'
import { ThemeToggle } from '@/components/ThemeToggle'

describe('ThemeToggle', () => {
  it('renders the Dark label when theme is light', () => {
    renderWithProviders(<ThemeToggle />)

    expect(screen.getByRole('button', { name: /dark/i })).toBeInTheDocument()
  })

  it('renders the Light label when theme is dark', () => {
    localStorage.setItem('theme', 'dark')
    renderWithProviders(<ThemeToggle />)

    expect(screen.getByRole('button', { name: /light/i })).toBeInTheDocument()
    localStorage.removeItem('theme')
  })

  it('calls toggleTheme when clicked', async () => {
    const user = userEvent.setup()
    renderWithProviders(<ThemeToggle />)

    await user.click(screen.getByRole('button', { name: /dark/i }))

    // After toggle, label switches to Light
    expect(screen.getByRole('button', { name: /light/i })).toBeInTheDocument()
  })
})
