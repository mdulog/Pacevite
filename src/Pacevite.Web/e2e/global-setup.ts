import { execFileSync } from 'child_process'
import { createConnection } from 'net'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '../../..')
const DB_PORT = 5432

function isPortOpen(port: number): Promise<boolean> {
  return new Promise(resolve => {
    const socket = createConnection(port, 'localhost')
    socket.once('connect', () => { socket.destroy(); resolve(true) })
    socket.once('error', () => resolve(false))
  })
}

async function waitForPort(port: number, timeoutMs: number): Promise<void> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (await isPortOpen(port)) return
    await new Promise(r => setTimeout(r, 500))
  }
  throw new Error(`Timed out waiting for localhost:${port} after ${timeoutMs}ms`)
}

export default async function globalSetup() {
  if (await isPortOpen(DB_PORT)) {
    console.log('[e2e] Database already running.')
    return
  }

  console.log('[e2e] Starting database container...')
  execFileSync('docker', ['compose', 'up', '-d', 'db'], { cwd: REPO_ROOT, stdio: 'inherit' })

  await waitForPort(DB_PORT, 30_000)
  console.log('[e2e] Database ready.')
}
