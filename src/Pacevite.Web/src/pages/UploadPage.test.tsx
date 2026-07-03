import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Routes, Route, useLocation } from 'react-router-dom'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { UploadPage } from '@/pages/UploadPage'
import { renderWithProviders } from '@/test/render'

function DashboardStub() {
  const { state } = useLocation()
  return <div>dashboard uploadedCount={state?.uploadedCount ?? 0}</div>
}

function renderUploadPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/upload" element={<UploadPage />} />
      <Route path="/dashboard" element={<DashboardStub />} />
    </Routes>,
    { initialEntries: ['/upload'], authenticated: true }
  )
}

describe('UploadPage', () => {
  it('renders with upload button disabled when no file is selected', () => {
    renderUploadPage()

    expect(screen.getByRole('button', { name: /upload/i })).toBeDisabled()
  })

  it('mentions GPX as a supported format', () => {
    renderUploadPage()

    expect(screen.getAllByText(/gpx/i).length).toBeGreaterThan(0)
  })

  it('accepts .gpx files in the file picker', () => {
    renderUploadPage()

    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    expect(input.accept).toContain('.gpx')
  })

  it('shows selected filename when a GPX file is chosen', async () => {
    renderUploadPage()

    const file = new File(['<gpx></gpx>'], 'morning-run.gpx', { type: 'application/octet-stream' })
    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, file)

    expect(screen.getByText('morning-run.gpx')).toBeInTheDocument()
  })

  it('shows selected filename when file is chosen', async () => {
    renderUploadPage()

    const file = new File(['MARATHON,Berlin,2024-09-29,FINISHED,14400'], 'events.csv', { type: 'text/csv' })
    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, file)

    expect(screen.getByText('events.csv')).toBeInTheDocument()
  })

  it('enables upload button after file is selected', async () => {
    renderUploadPage()

    const file = new File(['[]'], 'events.json', { type: 'application/json' })
    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, file)

    expect(screen.getByRole('button', { name: /upload/i })).toBeEnabled()
  })

  it('navigates to dashboard with uploadedCount on success', async () => {
    renderUploadPage()

    const file = new File(['MARATHON,Berlin,2024-09-29,FINISHED,14400'], 'events.csv', { type: 'text/csv' })
    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, file)
    await userEvent.click(screen.getByRole('button', { name: /upload/i }))

    await waitFor(() => {
      expect(screen.getByText(/dashboard uploadedCount=1/i)).toBeInTheDocument()
    })
  })

  it('shows error message when upload fails', async () => {
    server.use(
      http.post('http://localhost/api/events/upload', () =>
        new HttpResponse(null, { status: 400 })
      )
    )

    renderUploadPage()

    const file = new File(['bad data'], 'events.csv', { type: 'text/csv' })
    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, file)
    await userEvent.click(screen.getByRole('button', { name: /upload/i }))

    await waitFor(() => {
      expect(screen.getByText('Upload failed. Check your file format and try again.')).toBeInTheDocument()
    })
  })

  it('clears error when a new file is selected', async () => {
    server.use(
      http.post('http://localhost/api/events/upload', () =>
        new HttpResponse(null, { status: 400 })
      )
    )

    renderUploadPage()

    const input = document.querySelector<HTMLInputElement>('input[type="file"]')!
    await userEvent.upload(input, new File(['bad'], 'bad.csv', { type: 'text/csv' }))
    await userEvent.click(screen.getByRole('button', { name: /upload/i }))

    await waitFor(() => {
      expect(screen.getByText('Upload failed. Check your file format and try again.')).toBeInTheDocument()
    })

    await userEvent.upload(input, new File(['good'], 'good.csv', { type: 'text/csv' }))

    expect(screen.queryByText('Upload failed. Check your file format and try again.')).not.toBeInTheDocument()
  })
})
