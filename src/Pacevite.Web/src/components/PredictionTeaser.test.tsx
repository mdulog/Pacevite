import { screen } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionTeaser } from './PredictionTeaser'
import { describe, it, expect } from 'vitest'

describe('PredictionTeaser', () => {
  it('renders predicted time and link when events exist', async () => {
    renderWithProviders(<PredictionTeaser />, {
      authenticated: true,
      initialEntries: ['/dashboard'],
    })
    expect(await screen.findByTestId('prediction-teaser')).toBeInTheDocument()
    expect(screen.getByTestId('teaser-link')).toHaveAttribute('href', '/predict')
  })

  it('renders nothing while loading', () => {
    renderWithProviders(<PredictionTeaser />, { authenticated: true })
    expect(screen.queryByTestId('prediction-teaser')).not.toBeInTheDocument()
  })
})
