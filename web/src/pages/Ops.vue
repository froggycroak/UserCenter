<template>
  <div class="ops-page">
    <div class="ops-box" :class="{ 'ops-box-wide': status === 'ok' }">
      <p class="section-eyebrow">OPS</p>
      <h1 class="ops-title">运维门禁</h1>
      <p class="ops-lead">内部入口 · 不对终端用户分发</p>

      <p v-if="status === 'checking'" class="ops-hint">正在校验管理员身份…</p>

      <p v-else-if="status === 'denied'" class="alert error">
        已登录但不是管理员。请把下面的 <code>sub</code> 原样写入白名单后刷新。
      </p>
      <p v-else-if="status === 'ok'" class="alert ok">管理员身份已确认。</p>
      <p v-else-if="error" class="alert error">{{ error }}</p>

      <dl v-if="me" class="ops-fields">
        <div class="ops-field">
          <dt>账号</dt>
          <dd>{{ me.account || me.name || '—' }}</dd>
        </div>
        <div class="ops-field">
          <dt>sub</dt>
          <dd><code>{{ me.sub }}</code></dd>
        </div>
      </dl>

      <p v-if="status === 'denied'" class="ops-hint">
        在 <code>AdminSubs</code> 中加入上述 UUID。也兼容 ProtoMass 的 <code>Auth.AdminSubs</code> 写法。改完配置无需重启。
      </p>

      <div v-if="status === 'ok'" class="ops-invites">
        <h2 class="ops-subtitle">应用角色名单</h2>
        <p class="ops-hint">
          按 <code>clientId</code> 限制谁能登录该应用。角色名对照 Casdoor JWT
          <code>roles</code>，多个用逗号分隔。空名单表示不限制。管理员也不豁免。
        </p>
        <p v-if="appsError" class="alert error">{{ appsError }}</p>
        <p v-if="appsOk" class="alert ok">{{ appsOk }}</p>
        <table class="ops-table">
          <thead>
            <tr>
              <th>clientId</th>
              <th>allowedRoles</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="clientApps.length === 0">
              <td colspan="2" class="ops-empty">还没有可配置的应用</td>
            </tr>
            <tr v-for="row in clientApps" :key="row.clientId">
              <td><code>{{ row.clientId }}</code></td>
              <td>
                <input
                  v-model="row.rolesText"
                  class="ops-roles-input"
                  type="text"
                  placeholder="空则不限制，例如 af"
                  :disabled="appsBusy"
                />
              </td>
            </tr>
          </tbody>
        </table>
        <div class="ops-actions">
          <label class="ops-count ops-llm-pick">
            添加
            <input v-model="newClientId" type="text" placeholder="clientId" :disabled="appsBusy" />
          </label>
          <button class="btn btn-secondary" type="button" :disabled="appsBusy" @click="addClientApp">加入列表</button>
          <button class="btn btn-black" type="button" :disabled="appsBusy" @click="saveClientApps">保存</button>
          <button class="btn btn-secondary" type="button" :disabled="appsBusy" @click="loadClientApps">刷新</button>
        </div>
      </div>

      <div v-if="status === 'ok'" class="ops-invites">
        <h2 class="ops-subtitle">智能助手模型</h2>
        <p class="ops-hint">
          名单来自 Chat Hub 的
          <code>GET /v1/models</code>，规范 id 为
          <code>厂商/模型名</code>。密钥在网关，这里只选上游。
          <a v-if="llmGuideUrl" :href="llmGuideUrl" target="_blank" rel="noopener">打开 /guide</a>
        </p>
        <p v-if="llmError" class="alert error">{{ llmError }}</p>
        <p v-if="llmOk" class="alert ok">{{ llmOk }}</p>
        <div class="ops-actions">
          <label class="ops-count ops-llm-pick">
            厂商
            <select v-model="llmVendor" :disabled="llmBusy || llmVendors.length === 0" @change="onVendorChange">
              <option v-if="llmVendors.length === 0" value="">暂无名单</option>
              <option v-for="vendor in llmVendors" :key="vendor" :value="vendor">{{ vendor }}</option>
            </select>
          </label>
          <label class="ops-count ops-llm-pick">
            模型
            <select v-model="llmModel" :disabled="llmBusy || llmModelsForVendor.length === 0">
              <option v-if="llmModelsForVendor.length === 0" value="">请先选厂商</option>
              <option v-for="row in llmModelsForVendor" :key="row.id" :value="row.id">{{ modelShortName(row.id) }}</option>
            </select>
          </label>
          <button class="btn btn-black" type="button" :disabled="llmBusy || !llmModel" @click="saveLlm">保存</button>
          <button class="btn btn-secondary" type="button" :disabled="llmBusy" @click="loadLlm">刷新名单</button>
        </div>
        <p class="ops-hint">
          当前：<code>{{ llmSaved || '—' }}</code>
          <span v-if="llmHubDefault"> · 网关默认：<code>{{ llmHubDefault }}</code></span>
        </p>
      </div>

      <div v-if="status === 'ok'" class="ops-invites">
        <h2 class="ops-subtitle">邀请码</h2>
        <p v-if="inviteError" class="alert error">{{ inviteError }}</p>
        <div class="ops-actions">
          <label class="ops-count">
            生成
            <input v-model.number="issueCount" type="number" min="1" max="50" />
            张
          </label>
          <button class="btn btn-black" type="button" :disabled="inviteBusy" @click="issue">生成</button>
          <button class="btn btn-secondary" type="button" :disabled="inviteBusy" @click="loadInvites">刷新</button>
          <button
            class="btn btn-secondary"
            type="button"
            :disabled="selectedCount === 0"
            @click="exportTxt"
          >
            导出已选{{ selectedCount ? ` (${selectedCount})` : '' }}
          </button>
        </div>
        <table class="ops-table">
          <thead>
            <tr>
              <th class="ops-check">
                <input
                  type="checkbox"
                  :checked="allSelected"
                  :indeterminate="someSelected"
                  :disabled="invites.length === 0"
                  @change="toggleAll"
                />
              </th>
              <th>邀请码</th>
              <th>生成时间</th>
              <th>状态</th>
              <th>使用者</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="invites.length === 0">
              <td colspan="6" class="ops-empty">还没有邀请码</td>
            </tr>
            <tr v-for="row in invites" :key="row.code">
              <td class="ops-check">
                <input v-model="selected[row.code]" type="checkbox" />
              </td>
              <td><code>{{ row.display || row.code }}</code></td>
              <td class="ops-time">{{ formatTime(row.createdAt) }}</td>
              <td>{{ statusLabel(row.status) }}</td>
              <td>{{ row.usedByAccount || row.usedBySub || '—' }}</td>
              <td>
                <button
                  v-if="row.status === 'unused'"
                  class="btn btn-secondary"
                  type="button"
                  :disabled="inviteBusy"
                  @click="revoke(row.code)"
                >
                  作废
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="status === 'ok'" class="ops-invites">
        <h2 class="ops-subtitle">用户建议</h2>
        <p v-if="suggestionError" class="alert error">{{ suggestionError }}</p>
        <div class="ops-actions">
          <button class="btn btn-secondary" type="button" :disabled="suggestionBusy" @click="loadSuggestions">
            刷新
          </button>
        </div>
        <table class="ops-table">
          <thead>
            <tr>
              <th>时间</th>
              <th>账号</th>
              <th>产品</th>
              <th>内容</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="suggestions.length === 0">
              <td colspan="4" class="ops-empty">还没有建议</td>
            </tr>
            <tr v-for="row in suggestions" :key="row.id">
              <td class="ops-time">{{ formatTime(row.createdAt) }}</td>
              <td>{{ row.account || row.sub || '—' }}</td>
              <td>{{ productLabel(row) }}</td>
              <td class="ops-suggestion-content">{{ row.content }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="ops-actions">
        <button v-if="status !== 'checking'" class="btn btn-black" type="button" @click="refresh">重新校验</button>
        <button class="btn btn-secondary" type="button" @click="relogin">重新登录</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { clearTokens, getAccessToken, isStaticDemo, STATIC_DEMO_HINT } from '../api'
import { openAuthDialog, onAuthChanged } from '../authDialog'

type OpsMe = {
  sub?: string
  id?: string
  account?: string
  name?: string
  isAdmin?: boolean
  error?: string
}

type InviteRow = {
  code: string
  display?: string
  status: string
  createdAt?: string
  usedBySub?: string
  usedByAccount?: string
}

type SuggestionRow = {
  id: string
  sub?: string
  account?: string
  productTag?: string
  productOther?: string
  content?: string
  createdAt?: string
}

type LlmModelRow = {
  id: string
  ownedBy?: string
}

type ClientAppRow = {
  clientId: string
  rolesText: string
}

const route = useRoute()
const opsHome = () => (route.path.startsWith('/__ops') ? '/__ops' : '/ops')
const status = ref<'checking' | 'ok' | 'denied' | 'error'>('checking')
const error = ref('')
const me = ref<OpsMe | null>(null)
const invites = ref<InviteRow[]>([])
const suggestions = ref<SuggestionRow[]>([])
const selected = reactive<Record<string, boolean>>({})
const inviteError = ref('')
const inviteBusy = ref(false)
const suggestionError = ref('')
const suggestionBusy = ref(false)
const issueCount = ref(10)
const llmBusy = ref(false)
const llmError = ref('')
const llmOk = ref('')
const llmGuideUrl = ref('')
const llmHubDefault = ref('')
const llmSaved = ref('')
const llmVendor = ref('')
const llmModel = ref('')
const llmModels = ref<LlmModelRow[]>([])
const appsBusy = ref(false)
const appsError = ref('')
const appsOk = ref('')
const newClientId = ref('')
const clientApps = ref<ClientAppRow[]>([])
const llmVendors = computed(() => {
  const seen = new Set<string>()
  const list: string[] = []
  for (const row of llmModels.value) {
    const vendor = row.ownedBy || vendorOf(row.id)
    if (!vendor || seen.has(vendor)) continue
    seen.add(vendor)
    list.push(vendor)
  }
  return list
})
const llmModelsForVendor = computed(() =>
  llmModels.value.filter((row) => (row.ownedBy || vendorOf(row.id)) === llmVendor.value)
)
const selectedCount = computed(() => invites.value.filter((row) => selected[row.code]).length)
const allSelected = computed(() => invites.value.length > 0 && selectedCount.value === invites.value.length)
const someSelected = computed(() => selectedCount.value > 0 && selectedCount.value < invites.value.length)

let stopAuthListen: (() => void) | undefined

onMounted(() => {
  void refresh()
  stopAuthListen = onAuthChanged(() => { void refresh() })
})

onUnmounted(() => stopAuthListen?.())

function authHeaders(): HeadersInit {
  const token = getAccessToken()
  return { Authorization: `Bearer ${token}`, Accept: 'application/json' }
}

async function readJson(res: Response) {
  const text = await res.text()
  if (!text) return {}
  try {
    return JSON.parse(text) as Record<string, unknown>
  } catch {
    return { error: text }
  }
}

function statusLabel(value: string) {
  if (value === 'used') return '已使用'
  if (value === 'revoked') return '已作废'
  return '未使用'
}

function formatTime(value?: string) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function productLabel(row: SuggestionRow) {
  const tag = (row.productTag || '').trim()
  const other = (row.productOther || '').trim()
  if (tag === '其他' && other) return `其他 · ${other}`
  return tag || '—'
}

function fileStamp() {
  const date = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-${pad(date.getHours())}${pad(date.getMinutes())}`
}

function toggleAll(event: Event) {
  const on = (event.target as HTMLInputElement).checked
  for (const key of Object.keys(selected)) delete selected[key]
  if (on) {
    for (const row of invites.value) selected[row.code] = true
  }
}

function exportTxt() {
  const rows = invites.value.filter((row) => selected[row.code])
  if (rows.length === 0) {
    inviteError.value = '请先勾选要导出的邀请码'
    return
  }
  const text = rows.map((row) => row.display || row.code).join('\r\n') + '\r\n'
  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `invite-codes-${fileStamp()}.txt`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

async function refresh() {
  error.value = ''
  status.value = 'checking'
  me.value = null
  if (isStaticDemo()) {
    status.value = 'error'
    error.value = STATIC_DEMO_HINT
    return
  }
  const token = getAccessToken()
  if (!token) {
    status.value = 'error'
    error.value = '未登录'
    openAuthDialog('/login', opsHome())
    return
  }
  try {
    const res = await fetch('/api/ops/me', { headers: authHeaders(), cache: 'no-store' })
    const data = (await readJson(res)) as OpsMe
    if (res.status === 401) {
      clearTokens()
      status.value = 'error'
      error.value = '未登录'
      openAuthDialog('/login', opsHome())
      return
    }
    me.value = data
    if (res.status === 403 || data.isAdmin === false) {
      status.value = 'denied'
      return
    }
    if (!res.ok) {
      status.value = 'error'
      error.value = data.error || `请求失败 (${res.status})`
      return
    }
    status.value = 'ok'
    await Promise.all([loadInvites(), loadSuggestions(), loadLlm(), loadClientApps()])
  } catch (e) {
    status.value = 'error'
    error.value = e instanceof Error ? e.message : '无法校验管理员身份'
  }
}

function vendorOf(id: string) {
  const slash = id.indexOf('/')
  return slash > 0 ? id.slice(0, slash) : id
}

function modelShortName(id: string) {
  const slash = id.indexOf('/')
  return slash >= 0 && slash < id.length - 1 ? id.slice(slash + 1) : id
}

function applyLlmSelection(model: string) {
  const wanted = model.trim()
  llmSaved.value = wanted
  const match = llmModels.value.find((row) => row.id === wanted)
  const vendor = match?.ownedBy || vendorOf(wanted) || llmVendors.value[0] || ''
  if (vendor && !llmVendors.value.includes(vendor) && wanted) {
    llmModels.value = [{ id: wanted, ownedBy: vendor }, ...llmModels.value]
  }
  llmVendor.value = vendor
  const models = llmModels.value.filter((row) => (row.ownedBy || vendorOf(row.id)) === vendor)
  if (wanted && models.some((row) => row.id === wanted)) {
    llmModel.value = wanted
  } else {
    llmModel.value = models[0]?.id || wanted
  }
}

function onVendorChange() {
  const first = llmModelsForVendor.value[0]
  llmModel.value = first?.id || ''
}

async function loadLlm() {
  llmError.value = ''
  llmOk.value = ''
  llmBusy.value = true
  try {
    const res = await fetch('/api/ops/llm', { headers: authHeaders(), cache: 'no-store' })
    const data = await readJson(res)
    if (!res.ok) {
      llmError.value = String(data.error || `无法读取模型配置 (${res.status})`)
      return
    }
    llmGuideUrl.value = String(data.guideUrl || '')
    llmHubDefault.value = String(data.hubDefault || '')
    const rows = Array.isArray(data.models) ? (data.models as LlmModelRow[]) : []
    llmModels.value = rows
      .map((row) => ({
        id: String(row.id || '').trim(),
        ownedBy: String(row.ownedBy || vendorOf(String(row.id || ''))).trim()
      }))
      .filter((row) => row.id)
    const current = String(data.model || '')
    if (current && !llmModels.value.some((row) => row.id === current)) {
      llmModels.value.unshift({ id: current, ownedBy: vendorOf(current) || 'other' })
    }
    applyLlmSelection(current)
    if (data.error) llmError.value = String(data.error)
  } finally {
    llmBusy.value = false
  }
}

async function saveLlm() {
  llmBusy.value = true
  llmError.value = ''
  llmOk.value = ''
  try {
    const res = await fetch('/api/ops/llm', {
      method: 'POST',
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ model: llmModel.value })
    })
    const data = await readJson(res)
    if (!res.ok) {
      llmError.value = String(data.error || '保存失败')
      return
    }
    applyLlmSelection(String(data.model || llmModel.value))
    llmOk.value = `已保存 ${llmSaved.value}，无需重启`
  } finally {
    llmBusy.value = false
  }
}

function parseRoles(text: string) {
  return text
    .split(/[,;，、\s]+/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
}

function addClientApp() {
  appsError.value = ''
  appsOk.value = ''
  const id = newClientId.value.trim()
  if (!id) {
    appsError.value = '请输入 clientId'
    return
  }
  if (!/^[A-Za-z0-9_-]+$/.test(id)) {
    appsError.value = 'clientId 只能包含字母、数字、连字符和下划线'
    return
  }
  if (clientApps.value.some((row) => row.clientId.toLowerCase() === id.toLowerCase())) {
    appsError.value = `${id} 已在列表中`
    return
  }
  clientApps.value.push({ clientId: id, rolesText: '' })
  newClientId.value = ''
}

async function loadClientApps() {
  appsError.value = ''
  appsOk.value = ''
  appsBusy.value = true
  try {
    const res = await fetch('/api/ops/client-apps', { headers: authHeaders(), cache: 'no-store' })
    const data = await readJson(res)
    if (!res.ok) {
      appsError.value = String(data.error || `无法读取角色名单 (${res.status})`)
      return
    }
    const rows = Array.isArray(data.apps) ? data.apps as { clientId?: string; allowedRoles?: string[] }[] : []
    clientApps.value = rows
      .map((row) => ({
        clientId: String(row.clientId || '').trim(),
        rolesText: Array.isArray(row.allowedRoles) ? row.allowedRoles.join(', ') : ''
      }))
      .filter((row) => row.clientId)
  } finally {
    appsBusy.value = false
  }
}

async function saveClientApps() {
  appsBusy.value = true
  appsError.value = ''
  appsOk.value = ''
  try {
    const res = await fetch('/api/ops/client-apps', {
      method: 'POST',
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({
        apps: clientApps.value.map((row) => ({
          clientId: row.clientId,
          allowedRoles: parseRoles(row.rolesText)
        }))
      })
    })
    const data = await readJson(res)
    if (!res.ok) {
      appsError.value = String(data.error || '保存失败')
      return
    }
    const rows = Array.isArray(data.apps) ? data.apps as { clientId?: string; allowedRoles?: string[] }[] : []
    if (rows.length > 0) {
      clientApps.value = rows.map((row) => ({
        clientId: String(row.clientId || '').trim(),
        rolesText: Array.isArray(row.allowedRoles) ? row.allowedRoles.join(', ') : ''
      })).filter((row) => row.clientId)
    }
    appsOk.value = '已保存角色名单，无需重启'
  } finally {
    appsBusy.value = false
  }
}

async function loadInvites() {
  inviteError.value = ''
  const res = await fetch('/api/ops/invites', { headers: authHeaders(), cache: 'no-store' })
  const data = await readJson(res)
  if (!res.ok) {
    inviteError.value = String(data.error || `无法读取邀请码 (${res.status})`)
    return
  }
  invites.value = Array.isArray(data.invites) ? (data.invites as InviteRow[]) : []
  for (const key of Object.keys(selected)) {
    if (!invites.value.some((row) => row.code === key)) delete selected[key]
  }
}

async function loadSuggestions() {
  suggestionError.value = ''
  suggestionBusy.value = true
  try {
    const res = await fetch('/api/ops/suggestions', { headers: authHeaders(), cache: 'no-store' })
    const data = await readJson(res)
    if (!res.ok) {
      suggestionError.value = String(data.error || `无法读取建议 (${res.status})`)
      return
    }
    suggestions.value = Array.isArray(data.suggestions) ? (data.suggestions as SuggestionRow[]) : []
  } finally {
    suggestionBusy.value = false
  }
}

async function issue() {
  inviteBusy.value = true
  inviteError.value = ''
  try {
    const res = await fetch('/api/ops/invites', {
      method: 'POST',
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ count: issueCount.value || 10 })
    })
    const data = await readJson(res)
    if (!res.ok) {
      inviteError.value = String(data.error || '生成失败')
      return
    }
    await loadInvites()
  } finally {
    inviteBusy.value = false
  }
}

async function revoke(code: string) {
  inviteBusy.value = true
  inviteError.value = ''
  try {
    const res = await fetch(`/api/ops/invites/${encodeURIComponent(code)}/revoke`, {
      method: 'POST',
      headers: authHeaders()
    })
    const data = await readJson(res)
    if (!res.ok) {
      inviteError.value = String(data.error || '作废失败')
      return
    }
    await loadInvites()
  } finally {
    inviteBusy.value = false
  }
}

function relogin() {
  clearTokens()
  openAuthDialog('/login', opsHome())
}
</script>
