import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Routes, Route, useLocation } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { AddEventPage } from '@/pages/AddEventPage'
import { renderWithProviders } from '@/test/render'

function EventDetailStub() {
  const location = useLocation()
  return <div>event detail page at {location.pathname}</div>
}

function renderAddEventPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/events/new" element={<AddEventPage />} />
      <Route path="/events/:id" element={<EventDetailStub />} />
    </Routes>,
    { initialEntries: ['/events/new'], authenticated: true }
  )
}

async function fillMinimumValidForm() {
  await userEvent.type(screen.getByLabelText(/event name/i), 'Local 10K')
  await userEvent.type(screen.getByLabelText(/^date$/i), '2026-05-01')
  await userEvent.type(screen.getByLabelText(/hours/i), '0')
  await userEvent.type(screen.getByLabelText(/minutes/i), '45')
  await userEvent.type(screen.getByLabelText(/seconds/i), '0')
}

describe('AddEventPage', () => {
  it('renders save button disabled when required fields are empty', () => {
    renderAddEventPage()

    expect(screen.getByRole('button', { name: /save event/i })).toBeDisabled()
  })

  it('enables save button once name, date, and a non-zero elapsed time are provided', async () => {
    renderAddEventPage()

    await fillMinimumValidForm()

    expect(screen.getByRole('button', { name: /save event/i })).toBeEnabled()
  })

  it('keeps save button disabled when elapsed time is all zeroes', async () => {
    renderAddEventPage()

    await userEvent.type(screen.getByLabelText(/event name/i), 'Local 10K')
    await userEvent.type(screen.getByLabelText(/^date$/i), '2026-05-01')

    expect(screen.getByRole('button', { name: /save event/i })).toBeDisabled()
  })

  it('navigates to the new event detail page on success', async () => {
    renderAddEventPage()

    await fillMinimumValidForm()
    await userEvent.click(screen.getByRole('button', { name: /save event/i }))

    await waitFor(() => {
      expect(screen.getByText(/event detail page at \/events\/event-201/i)).toBeInTheDocument()
    })
  })

  it('sends elapsed time converted to total seconds', async () => {
    let capturedBody: Record<string, unknown> | undefined
    server.use(
      http.post('http://localhost/api/events', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({ id: 'event-201' }, { status: 201 })
      })
    )

    renderAddEventPage()

    await userEvent.type(screen.getByLabelText(/event name/i), 'Local 10K')
    await userEvent.type(screen.getByLabelText(/^date$/i), '2026-05-01')
    await userEvent.type(screen.getByLabelText(/hours/i), '1')
    await userEvent.type(screen.getByLabelText(/minutes/i), '2')
    await userEvent.type(screen.getByLabelText(/seconds/i), '3')
    await userEvent.click(screen.getByRole('button', { name: /save event/i }))

    await waitFor(() => {
      expect(capturedBody?.elapsedSecs).toBe(3723)
    })
  })

  it('shows a conflict message when the event already exists', async () => {
    server.use(
      http.post('http://localhost/api/events', () =>
        new HttpResponse('An event named \'Local 10K\' already exists for 2026-05-01.', { status: 409 })
      )
    )

    renderAddEventPage()

    await fillMinimumValidForm()
    await userEvent.click(screen.getByRole('button', { name: /save event/i }))

    await waitFor(() => {
      expect(screen.getByText(/already exists/i)).toBeInTheDocument()
    })
  })

  it('shows a validation error message on 400', async () => {
    server.use(
      http.post('http://localhost/api/events', () =>
        new HttpResponse(null, { status: 400 })
      )
    )

    renderAddEventPage()

    await fillMinimumValidForm()
    await userEvent.click(screen.getByRole('button', { name: /save event/i }))

    await waitFor(() => {
      expect(screen.getByText(/couldn.t save event/i)).toBeInTheDocument()
    })
  })
})
