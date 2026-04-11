# Claude Chatbot Feature — Design Spec

**Date:** 2026-04-11  
**Status:** Approved  
**Author:** Mike D

---

## 1. Overview

Add a floating Claude-powered chat widget to the existing Pacevite React web app. The chatbot answers analytical and comparative questions about the authenticated user's fitness data (events, splits, personal bests) and can fetch external context (race results, training tips) from the internet when relevant.

The backend runs an agentic tool-use loop via the Anthropic SDK, streaming tokens back to the frontend via Server-Sent Events (SSE). The backend is fully stateless — the frontend owns conversation history.

---

## 2. Goals

- Embed a chat widget (floating button, bottom-right) into the existing React app
- Answer analytical questions: trends, comparisons, progression over time
- Support internet lookups: race result comparisons and training tip retrieval
- Use Claude tool calls to query the database on-demand (not upfront context injection)
- Stream responses token-by-token for a responsive chat UX
- Never expose the Anthropic API key or tool logic to the client

---

## 3. Non-Goals

- Persisting conversation history server-side (sessions, DB storage)
- Supporting multi-user shared conversations
- Answering questions outside the user's own data or publicly available fitness content
- Voice input / output
- Mobile-native implementation

---

## 4. Architecture

```
React ChatWidget
    │
    │  POST /chat/message  (SSE response)
    ▼
ChatEndpoint  (new, JWT-protected)
    │
    ▼
SendMessageHandler  (MediatR command handler)
    │  calls Anthropic SDK (streaming, agentic loop)
    ▼
Claude claude-sonnet-4-6
    │  tool_use blocks
    ▼
ChatToolExecutor  (dispatches tool calls by name)
    ├── GetEventsToolHandler         → AppDbContext
    ├── GetPersonalBestsToolHandler  → AppDbContext
    ├── ScrapeRaceResultsToolHandler → HttpClient
    └── FetchTrainingTipsToolHandler → HttpClient
```

**Key invariants:**
- `userId` is always extracted from the JWT — never accepted from the request body
- Tool handlers are bound to the authenticated `userId` at dispatch time; Claude cannot be prompted into querying another user's data
- The backend never stores conversation state; the frontend sends full history with every request

---

## 5. Backend Design

### 5.1 New Files

```
src/Pacevite.Api/
├── Features/
│   └── Chat/
│       ├── ChatEndpoints.cs
│       ├── SendMessageCommand.cs
│       └── SendMessageHandler.cs
├── Infrastructure/
│   └── Chat/
│       ├── IChatToolExecutor.cs
│       ├── ChatToolExecutor.cs
│       └── Tools/
│           ├── GetEventsToolHandler.cs
│           ├── GetPersonalBestsToolHandler.cs
│           ├── ScrapeRaceResultsToolHandler.cs
│           └── FetchTrainingTipsToolHandler.cs
```

### 5.2 Endpoint

```
POST /chat/message
Authorization: Bearer <jwt>
Content-Type: application/json
Response: text/event-stream
```

**Request body:**
```json
{
  "message": "Am I getting faster at Hyrox?",
  "history": [
    { "role": "user", "content": "..." },
    { "role": "assistant", "content": "..." }
  ]
}
```

**SSE event stream:**
```
event: delta
data: {"text": "Your fastest"}

event: tool_start
data: {"tool": "get_personal_bests", "label": "Looking up your personal bests..."}

event: tool_end
data: {}

event: done
data: {}

event: error
data: {"message": "Something went wrong. Please try again."}
```

### 5.3 Agentic Loop (SendMessageHandler)

1. Build Claude `messages[]` from incoming history + new user message
2. Open streaming call to Anthropic SDK with tool definitions
3. Stream `delta` events for text tokens
4. On `tool_use` block: emit `tool_start`, dispatch to `ChatToolExecutor`, emit `tool_end`
5. Append `tool_result` to messages, continue streaming
6. Repeat until `end_turn` stop reason
7. Emit `done`

### 5.4 Tool Definitions

| Tool | Inputs | Returns |
|---|---|---|
| `get_events` | `event_type?` (string), `from?` (ISO date), `to?` (ISO date) | JSON array of the user's events |
| `get_personal_bests` | _(none)_ | JSON array of PBs per event type |
| `scrape_race_results` | `race_name` (string), `year?` (int) | Scraped results text |
| `fetch_training_tips` | `query` (string) | Scraped article text |

### 5.5 Web Scraping

- Uses `HttpClient` with `HtmlAgilityPack` for HTML parsing
- No headless browser — targets publicly accessible race results pages and fitness articles
- Returns plain text excerpts; Claude synthesises the response
- Returns `"No results found"` when scrape yields no useful content

### 5.6 Configuration

```json
"Anthropic": {
  "ApiKey": "${ANTHROPIC_API_KEY}",
  "Model": "claude-sonnet-4-6",
  "MaxTokens": 1024
}
```

App fails fast at startup if `ANTHROPIC_API_KEY` is missing.

---

## 6. Frontend Design

### 6.1 New Files

```
src/Pacevite.Web/src/
├── components/
│   └── chat/
│       ├── ChatWidget.tsx
│       ├── ChatPanel.tsx
│       ├── ChatMessage.tsx
│       └── ChatToolStatus.tsx
├── hooks/
│   └── useChatStream.ts
└── lib/
    └── chatApi.ts
```

### 6.2 Component Responsibilities

| Component | Responsibility |
|---|---|
| `ChatWidget` | Floating 💬 button, bottom-right; toggles `ChatPanel` open/closed |
| `ChatPanel` | Renders conversation history, streaming message, tool status, text input |
| `ChatMessage` | Single message bubble — right-aligned (user), left-aligned with avatar (assistant); renders markdown |
| `ChatToolStatus` | Animated indicator shown between `tool_start` and `tool_end` events |
| `useChatStream` | Owns all state: `messages[]`, `streamingText`, `toolStatus`, `isLoading`; drives SSE stream lifecycle |
| `chatApi.ts` | `POST /chat/message` → returns `ReadableStream` |

### 6.3 Conversation History

- Stored in `useChatStream` React state as `ConversationMessage[]`
- Full history sent with every request — backend is stateless
- Cleared on page refresh (intentional — no session leakage)
- `localStorage` persistence is a future enhancement, requires no backend changes

---

## 7. Data Flow

```
User submits message
  → useChatStream appends to history[], POST /chat/message
  → SendMessageHandler builds Claude messages array
  → Anthropic SDK streams response
  → tool_use block → ChatToolExecutor → DB or HTTP
  → SSE: tool_start → tool_end
  → Claude streams text tokens
  → SSE: delta* → done
  → useChatStream appends assistant message to history[]
```

---

## 8. Security

| Control | Implementation |
|---|---|
| Authentication | JWT required on `/chat/message`; 401 without valid token |
| User data isolation | `userId` extracted from JWT claims in handler; tool handlers scoped to that `userId` |
| API key protection | `ANTHROPIC_API_KEY` server-side only; never sent to client |
| Input validation | `message` max length enforced via FluentValidation; `history` entries validated for role values |
| No sensitive data in responses | Generic error messages only; no stack traces or internal details in SSE error events |
| Injection prevention | DB tool handlers use parameterised EF Core queries |
| SSRF prevention | Web scrape handlers use an allowlist of approved domains (e.g. `results.athlinks.com`, `parkrun.org.uk`) |

---

## 9. Error Handling

| Scenario | Behaviour |
|---|---|
| Missing API key at startup | App throws `ArgumentNullException` — fails fast |
| Anthropic API error | Catches exception, streams `event: error` with generic message |
| Tool execution throws | `ChatToolExecutor` catches per-tool, returns structured error result to Claude |
| Scrape returns no content | Returns `"No results found"` as tool result; Claude handles gracefully |
| SSE connection drops (frontend) | `useChatStream` catches stream error, sets error state, renders retry prompt |

---

## 10. Testing

### Unit Tests (`tests/Pacevite.Api.Tests/Unit/Chat/`)

- `ChatToolExecutorTests` — correct handler dispatched per tool name; unknown tool returns structured error
- `GetEventsToolHandlerTests` — DB query scoped to `userId`; optional filters respected
- `ScrapeRaceResultsToolHandlerTests` — mocked `HttpClient`; HTML parsing extracts expected text

### Integration Tests (`tests/Pacevite.Api.Tests/Integration/`)

- `ChatEndpointsTests`:
  - Unauthenticated request returns 401
  - Authenticated request with mocked Anthropic SDK streams `delta` and `done` events
  - Tool calls are executed and results returned in stream

---

## 11. New Dependencies

| Package | Purpose |
|---|---|
| `Anthropic.SDK` (.NET) | Anthropic API client with streaming support |
| `HtmlAgilityPack` (.NET) | HTML parsing for web scrape handlers |
| `react-markdown` (npm) | Render Claude's markdown responses in chat bubbles |
