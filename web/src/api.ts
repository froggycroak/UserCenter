const ACCESS = 'uc.accessToken'
const REFRESH = 'uc.refreshToken'
const CLIENT = 'uc.clientId'
const REMEMBER = 'uc.remember'

export const STATIC_DEMO_HINT = '当前为静态演示站，登录与接口未接通。'

export function isStaticDemo(): boolean {
  return import.meta.env.VITE_STATIC_DEMO === '1'
}

export type SchemaItem = { key: string; label: string }

export type ProfileResp = {
  account: string
  id?: string
  sub?: string
  fields: Record<string, string>
  schema: SchemaItem[]
}

function read(key: string): string {
  return localStorage.getItem(key) || sessionStorage.getItem(key) || ''
}

export function getAccessToken(): string {
  return read(ACCESS)
}

export function getRefreshToken(): string {
  return read(REFRESH)
}

export function getClientId(): string {
  return read(CLIENT)
}

export function setTokens(access: string, refresh: string, remember?: boolean, clientId?: string) {
  const persist = remember ?? localStorage.getItem(REMEMBER) === '1'
  const nextClient = (clientId ?? getClientId()).trim()
  clearTokens()
  if (persist) localStorage.setItem(REMEMBER, '1')
  const store = persist ? localStorage : sessionStorage
  store.setItem(ACCESS, access)
  if (refresh) store.setItem(REFRESH, refresh)
  if (nextClient) store.setItem(CLIENT, nextClient)
}

export function clearTokens() {
  localStorage.removeItem(ACCESS)
  localStorage.removeItem(REFRESH)
  localStorage.removeItem(CLIENT)
  localStorage.removeItem(REMEMBER)
  sessionStorage.removeItem(ACCESS)
  sessionStorage.removeItem(REFRESH)
  sessionStorage.removeItem(CLIENT)
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  if (isStaticDemo()) {
    throw new Error(STATIC_DEMO_HINT)
  }

  const headers = new Headers(init.headers)
  if (!headers.has('Content-Type') && init.body) {
    headers.set('Content-Type', 'application/json')
  }
  const token = getAccessToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const res = await fetch(path, { ...init, headers })
  const text = await res.text()
  let data: unknown = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = { error: text }
    }
  }
  if (!res.ok) {
    const err = (data as { error?: string } | null)?.error || `请求失败 (${res.status})`
    throw new Error(err)
  }
  return data as T
}

export type ChatRole = 'user' | 'assistant'
export type ChatToolChip = { name: string; label: string; status: 'start' | 'done' }
export type ChatMessage = { role: ChatRole; content: string; tools?: ChatToolChip[]; pending?: string }

async function streamDemoReply(
  messages: ChatMessage[],
  onDelta: (text: string) => void,
  signal?: AbortSignal
): Promise<void> {
  const lastUser = [...messages].reverse().find((m) => m.role === 'user')
  const raw = (lastUser?.content || '').trim()
  const snippet = raw.slice(0, 40)
  const text =
    '这是 GitHub Pages 静态演示回复。' +
    (snippet ? `你刚才说：「${snippet}${raw.length > 40 ? '…' : ''}」。` : '') +
    '真实对话需要后端与模型服务，本站仅展示界面。'

  for (const ch of text) {
    if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
    onDelta(ch)
    await new Promise((r) => setTimeout(r, 12))
  }
}

export async function streamAssistantChat(
  messages: ChatMessage[],
  onDelta: (text: string) => void,
  signal?: AbortSignal,
  onTool?: (tool: ChatToolChip) => void
): Promise<void> {
  if (isStaticDemo()) {
    void onTool
    return streamDemoReply(messages, onDelta, signal)
  }

  const headers = new Headers({ 'Content-Type': 'application/json' })
  const token = getAccessToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const payload = messages.map(({ role, content }) => ({ role, content }))
  const res = await fetch('/api/assistant/chat', {
    method: 'POST',
    headers,
    body: JSON.stringify({ messages: payload }),
    signal
  })
  const ctype = (res.headers.get('content-type') || '').toLowerCase()
  if (!res.ok) {
    const text = await res.text()
    let err = `请求失败 (${res.status})`
    try {
      const data = JSON.parse(text) as { error?: string; detail?: string }
      err = data.error || data.detail || err
    } catch {
      if (text) err = text
    }
    throw new Error(err)
  }

  if (!res.body || (ctype.includes('application/json') && !ctype.includes('event-stream'))) {
    const data = (await res.json()) as {
      content?: string
      error?: string
      choices?: { message?: { content?: string } }[]
    }
    if (data.error) throw new Error(data.error)
    const content = data.choices?.[0]?.message?.content || data.content || ''
    if (content) onDelta(content)
    return
  }

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buf = ''
  const consume = (block: string) => {
    for (const line of block.split('\n')) {
      if (!line.startsWith('data:')) continue
      const data = line.slice(5).trim()
      if (!data || data === '[DONE]') continue
      let json: {
        error?: string
        uc?: { kind?: string; name?: string; label?: string; status?: string }
        choices?: { delta?: { content?: string } }[]
      }
      try {
        json = JSON.parse(data)
      } catch {
        continue
      }
      if (json.error) throw new Error(json.error)
      if (json.uc?.kind === 'tool' && json.uc.name) {
        onTool?.({
          name: json.uc.name,
          label: json.uc.label || json.uc.name,
          status: json.uc.status === 'done' ? 'done' : 'start'
        })
      }
      const delta = json.choices?.[0]?.delta?.content
      if (typeof delta === 'string' && delta) onDelta(delta)
    }
  }
  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buf += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n')
    let sep = buf.indexOf('\n\n')
    while (sep >= 0) {
      consume(buf.slice(0, sep))
      buf = buf.slice(sep + 2)
      sep = buf.indexOf('\n\n')
    }
  }
  if (buf.trim()) consume(buf)
}
