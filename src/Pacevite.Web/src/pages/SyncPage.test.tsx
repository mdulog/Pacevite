import { describe, it, expect, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Routes, Route } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { SyncPage } from '@/pages/SyncPage'
import { renderWithProviders } from '@/test/render'

function renderSyncPage(initialEntry = '/sync') {
  return renderWithProviders(
    <Routes>
      <Route path="/sync" element={<SyncPage />} />
    </Routes>,
    { initialEntries: [initialEntry], authenticated: true }
  )
}

const connectedActivities = [
  {
    externalActivityId: 'strava-1',
    name: 'Sunday Long Run',
    eventDate: '2026-05-03',
    elapsedSecs: 5400,
    possibleDuplicate: false,
  },
  {
    externalActivityId: 'strava-2',
    name: 'Track Session',
    eventDate: '2026-05-05',
    elapsedSecs: 3000,
    possibleDuplicate: true,
  },
]

function mockConnected() {
  server.use(
    http.get('http://localhost/api/sync/strava/activities', () => HttpResponse.json(connectedActivities))
  )
}

describe('SyncPage', () => {
  it('shows a Connect Strava button when not connected', async () => {
    renderSyncPage()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /connect strava/i })).toBeInTheDocument()
    })
  })

  it('navigates the browser to the Strava authorize URL on connect', async () => {
    const hrefSetter = vi.fn()
    vi.stubGlobal('location', { ...window.location, set href(value: string) { hrefSetter(value) } })

    try {
      renderSyncPage()

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /connect strava/i })).toBeInTheDocument()
      })
      await userEvent.click(screen.getByRole('button', { name: /connect strava/i }))

      await waitFor(() => {
        expect(hrefSetter).toHaveBeenCalledWith('https://www.strava.com/oauth/authorize?client_id=test')
      })
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('lists activities once connected', async () => {
    mockConnected()
    renderSyncPage()

    await waitFor(() => {
      expect(screen.getByText('Sunday Long Run')).toBeInTheDocument()
    })
    expect(screen.getByText('Track Session')).toBeInTheDocument()
  })

  it('flags a possible duplicate activity', async () => {
    mockConnected()
    renderSyncPage()

    await waitFor(() => {
      expect(screen.getByText('Track Session')).toBeInTheDocument()
    })

    const row = screen.getByText('Track Session').closest('[data-testid="strava-activity-row"]')!
    expect(row).toHaveTextContent(/possible duplicate/i)
  })

  it('does not flag a non-duplicate activity', async () => {
    mockConnected()
    renderSyncPage()

    await waitFor(() => {
      expect(screen.getByText('Sunday Long Run')).toBeInTheDocument()
    })

    const row = screen.getByText('Sunday Long Run').closest('[data-testid="strava-activity-row"]')!
    expect(row).not.toHaveTextContent(/possible duplicate/i)
  })

  it('confirms an activity and removes it from the list', async () => {
    mockConnected()
    renderSyncPage()

    await waitFor(() => {
      expect(screen.getByText('Sunday Long Run')).toBeInTheDocument()
    })

    const row = screen.getByText('Sunday Long Run').closest('[data-testid="strava-activity-row"]') as HTMLElement
    await userEvent.click(within(row).getByRole('button', { name: /confirm/i }))

    await waitFor(() => {
      expect(screen.queryByText('Sunday Long Run')).not.toBeInTheDocument()
    })
  })

  it('shows a success banner when returning from Strava with connected=true', async () => {
    mockConnected()
    renderSyncPage('/sync?connected=true')

    await waitFor(() => {
      expect(screen.getByText(/strava connected/i)).toBeInTheDocument()
    })
  })

  it('shows an error banner when returning from Strava with connected=false', async () => {
    renderSyncPage('/sync?connected=false')

    await waitFor(() => {
      expect(screen.getByText(/couldn.t connect to strava/i)).toBeInTheDocument()
    })
  })
})
