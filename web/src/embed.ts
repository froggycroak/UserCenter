const SOURCE = 'udapp-user-center'

/** 本站门户登录 / 注册 / 改资料使用的 Casdoor client。对外请求不带 clientId 时仍默认 protomassbrowser。 */
export const SELF_CLIENT_ID = 'usercenter'

export type QueryLike = Record<string, unknown>

export type HostMessage =
  | { type: 'ready'; page: 'login' | 'edit' | 'register' }
  | { type: 'login'; accessToken: string; refreshToken: string; sub?: string; id?: string; name?: string; account?: string; clientId?: string }
  | { type: 'profile-saved' }
  | { type: 'password-changed' }
  | { type: 'close' }

function queryValue(query: QueryLike, key: string): string | undefined {
  const v = query[key]
  if (typeof v === 'string') return v
  if (Array.isArray(v) && typeof v[0] === 'string') return v[0]
  return undefined
}

export function isEmbed(query: object): boolean {
  const v = queryValue(query as QueryLike, 'embed')
  return v === '1' || v === 'true'
}

export function parentOrigin(query: object): string {
  const raw = queryValue(query as QueryLike, 'parentOrigin')
  if (raw && /^https?:\/\//i.test(raw)) {
    try {
      return new URL(raw).origin
    } catch {
      return '*'
    }
  }
  return '*'
}

export function queryClientId(query: object): string | undefined {
  const raw = queryValue(query as QueryLike, 'clientId') || queryValue(query as QueryLike, 'client_id') || ''
  const id = raw.trim()
  return id || undefined
}

export function withClientId(query: object, dest: Record<string, string> = {}): Record<string, string> {
  const id = queryClientId(query)
  if (id) dest.clientId = id
  return dest
}

/** 本站页面登录：已有 query 则沿用；非 embed 且未指定则用 usercenter。embed 未指定则不传（走 API 默认 protomassbrowser）。 */
export function resolveLoginClientId(query: object): string | undefined {
  const fromQuery = queryClientId(query)
  if (fromQuery) return fromQuery
  if (isEmbed(query)) return undefined
  return SELF_CLIENT_ID
}

export function withLoginClientId(query: object, dest: Record<string, string> = {}): Record<string, string> {
  const id = resolveLoginClientId(query)
  if (id) dest.clientId = id
  return dest
}

export function notifyHost(query: object, payload: HostMessage) {
  const msg = { source: SOURCE, ...payload }
  const target = parentOrigin(query)
  if (window.parent && window.parent !== window) {
    window.parent.postMessage(msg, target)
  }
  if (window.opener && !window.opener.closed) {
    window.opener.postMessage(msg, target)
  }
}

export function onHostToken(handler: (accessToken: string, refreshToken?: string) => void) {
  const listener = (event: MessageEvent) => {
    const data = event.data
    if (!data || data.source !== 'udapp-user-center-host' || data.type !== 'setToken') return
    if (typeof data.accessToken === 'string' && data.accessToken) {
      handler(data.accessToken, typeof data.refreshToken === 'string' ? data.refreshToken : '')
    }
  }
  window.addEventListener('message', listener)
  return () => window.removeEventListener('message', listener)
}
