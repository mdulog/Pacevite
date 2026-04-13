import { useEffect, useRef, useState } from 'react'
import { ChatMessage } from './ChatMessage'
import { ChatToolStatus } from './ChatToolStatus'
import type { UseChatStreamResult } from '@/hooks/useChatStream'

interface Props {
  chat: UseChatStreamResult
}

export function ChatPanel({ chat }: Props) {
  const { messages, streamingText, toolStatus, isLoading, error, sendMessage, clearError } = chat
  const [input, setInput] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, streamingText, toolStatus])

  const submit = async () => {
    const text = input.trim()
    if (!text || isLoading) return
    setInput('')
    await sendMessage(text)
  }

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    await submit()
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void submit()
    }
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-200 bg-white rounded-t-2xl">
        <h2 className="text-sm font-semibold text-gray-900">Pacevite Assistant</h2>
        <p className="text-xs text-gray-500">Ask about your performance</p>
      </div>

      {/* Messages */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {messages.length === 0 && (
          <p className="text-center text-xs text-gray-400 mt-8">
            Ask me about your race history, trends, or how to improve.
          </p>
        )}

        {messages.map(msg => (
          <ChatMessage key={msg.id} message={msg} />
        ))}

        {toolStatus && <ChatToolStatus label={toolStatus} />}

        {streamingText && !toolStatus && (
          <div className="flex justify-start mb-3">
            <div className="w-7 h-7 rounded-full bg-blue-600 flex items-center justify-center text-white text-xs font-bold mr-2 flex-shrink-0 mt-1">
              P
            </div>
            <div className="max-w-[80%] bg-gray-100 rounded-2xl rounded-tl-sm px-4 py-2 text-sm text-gray-900">
              {streamingText}
              <span className="inline-block w-0.5 h-4 bg-gray-600 animate-pulse ml-0.5 align-text-bottom" />
            </div>
          </div>
        )}

        {error && (
          <div className="bg-red-50 border border-red-200 rounded-lg px-3 py-2 text-xs text-red-700 flex justify-between items-center mb-3">
            {error}
            <button onClick={clearError} className="ml-2 text-red-500 hover:text-red-700">✕</button>
          </div>
        )}

        <div ref={bottomRef} />
      </div>

      {/* Input */}
      <form onSubmit={handleSubmit} className="px-4 py-3 border-t border-gray-200 bg-white rounded-b-2xl">
        <div className="flex gap-2 items-end">
          <textarea
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask about your performance…"
            rows={1}
            disabled={isLoading}
            className="flex-1 resize-none rounded-xl border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
          />
          <button
            type="submit"
            disabled={!input.trim() || isLoading}
            className="w-8 h-8 flex items-center justify-center rounded-full bg-blue-600 text-white disabled:opacity-40 hover:bg-blue-700 transition-colors flex-shrink-0"
          >
            ↑
          </button>
        </div>
      </form>
    </div>
  )
}
