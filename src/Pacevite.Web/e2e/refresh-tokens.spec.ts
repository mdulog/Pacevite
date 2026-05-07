import { test, expect } from '@playwright/test'
import { uniqueEmail, registerViaApi, loginViaUi, TEST_PASSWORD } from './helpers'

test('login sets httpOnly refreshToken cookie with correct attributes', async ({ page, context }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  const cookies = await context.cookies()
  const refreshCookie = cookies.find(c => c.name === 'refreshToken')

  expect(refreshCookie, 'refreshToken cookie must be present after login').toBeDefined()
  expect(refreshCookie!.httpOnly, 'must be HttpOnly').toBe(true)
  expect(refreshCookie!.sameSite, 'must be Strict').toBe('Strict')
  expect(refreshCookie!.path, 'must be scoped to /api/auth').toBe('/api/auth')
  expect(refreshCookie!.expires, 'must have a future expiry (~7 days)').toBeGreaterThan(Date.now() / 1000)
})

test('logout clears the refreshToken cookie and redirects to login', async ({ page, context }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  // Confirm cookie is present before logout
  let cookies = await context.cookies()
  expect(cookies.find(c => c.name === 'refreshToken')).toBeDefined()

  // Click the sign-out button
  await page.getByRole('button', { name: /sign out/i }).click()
  await page.waitForURL('/login')

  // Cookie must be gone after logout
  cookies = await context.cookies()
  expect(cookies.find(c => c.name === 'refreshToken')).toBeUndefined()
})

test('logout fires POST /api/auth/logout', async ({ page }) => {
  const email = uniqueEmail()
  await registerViaApi(email)
  await loginViaUi(page, email)

  const [logoutRequest] = await Promise.all([
    page.waitForRequest(req => req.url().includes('/api/auth/logout') && req.method() === 'POST'),
    page.getByRole('button', { name: /sign out/i }).click(),
  ])

  const response = await logoutRequest.response()
  expect(response!.status()).toBe(204)
})

test('register also sets refreshToken cookie', async ({ page, context }) => {
  const email = uniqueEmail()

  await page.goto('/register')
  await page.fill('input[type="email"]', email)
  await page.fill('input[type="password"]', TEST_PASSWORD)
  await page.click('button[type="submit"]')
  await page.waitForURL('/dashboard')

  const cookies = await context.cookies()
  const refreshCookie = cookies.find(c => c.name === 'refreshToken')
  expect(refreshCookie, 'refreshToken cookie must be set on register').toBeDefined()
  expect(refreshCookie!.httpOnly).toBe(true)
})
