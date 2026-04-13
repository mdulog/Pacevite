import { tokenStore } from './api'

export interface ConversationMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface SseCallbacks {
  onDelta: (text: string) => void
  onToolStart: (tool: string, label: string) => void
  onToolEnd: () => void
  onDone: () => void
  onError: (message: string) => void
}

export async function streamChatMessage(
  message: string,
  history: ConversationMessage[],
  callbacks: SseCallbacks,
  signal: AbortSignal,
): Promise<void> {
  const token = tokenStore.get()

  const response = await fetch('/api/chat/message', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ message, history }),
    signal,
  })

  if (!response.ok) {
    callbacks.onError(`Request failed: ${response.status}`)
    return
  }

  const reader = response.body!.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''

    let currentEvent = ''
    for (const line of lines) {
      if (line.startsWith('event: ')) {
        currentEvent = line.slice(7).trim()
      } else if (line.startsWith('data: ')) {
        const data = JSON.parse(line.slice(6).trim())
        switch (currentEvent) {
          case 'delta':
            callbacks.onDelta(data.text)
            break
          case 'tool_start':
            callbacks.onToolStart(data.tool, data.label)
            break
          case 'tool_end':
            callbacks.onToolEnd()
            break
          case 'done':
            callbacks.onDone()
            return
          case 'error':
            callbacks.onError(data.message)
            return
        }
        currentEvent = ''
      }
    }
  }
}
