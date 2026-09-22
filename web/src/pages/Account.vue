<template>
  <div class="container tools-layout account-layout">
    <aside class="account-aside" aria-label="基本信息">
      <div class="account-avatar" aria-hidden="true">{{ avatarChar }}</div>
      <p class="account-name">{{ displayName }}</p>
      <p v-if="loadError" class="alert error">{{ loadError }}</p>
      <dl class="account-fields">
        <div class="account-field">
          <dt>账号</dt>
          <dd>{{ display(account) }}</dd>
        </div>
        <div v-for="item in profileRows" :key="item.key" class="account-field">
          <dt>{{ item.label }}</dt>
          <dd>{{ display(fields[item.key]) }}</dd>
        </div>
      </dl>
      <div class="account-actions">
        <button v-if="!loggedIn" class="btn btn-black" type="button" @click="openLogin">登录</button>
        <button v-else class="btn btn-black" type="button" @click="openEdit">修改信息</button>
        <button v-if="loggedIn" class="btn btn-secondary" type="button" @click="logout">退出登录</button>
      </div>
    </aside>
    <div class="tools-scroll">
      <section class="tools-section">
        <header class="section-header store-enter">
          <p class="section-eyebrow">ACCOUNT</p>
          <h2 class="section-title">用户中心</h2>
        </header>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { api, clearTokens, getAccessToken, type ProfileResp, type SchemaItem } from '../api'
import { openAuthDialog, onAuthChanged } from '../authDialog'

const account = ref('')
const schema = ref<SchemaItem[]>([])
const fields = reactive<Record<string, string>>({})
const loadError = ref('')
const loggedIn = computed(() => !!account.value)
const fallbackRows: SchemaItem[] = [
  { key: 'employeeId', label: '工号' },
  { key: 'phone', label: '手机号' },
  { key: 'email', label: '邮箱' },
  { key: 'orgName', label: '单位' }
]
const profileRows = computed(() => {
  const rows = schema.value.filter((item) => item.key !== 'nickname')
  return rows.length ? rows : fallbackRows
})

const displayName = computed(() => {
  if (!loggedIn.value) return '访客'
  return (fields.nickname || '').trim() || '—'
})

const avatarChar = computed(() => {
  const name = displayName.value
  if (!name || name === '—') return loggedIn.value ? '' : '访'
  return name.slice(0, 1).toUpperCase()
})

let stopAuthListen: (() => void) | undefined

onMounted(() => {
  void refresh()
  stopAuthListen = onAuthChanged(() => { void refresh() })
})

onUnmounted(() => stopAuthListen?.())

async function refresh() {
  loadError.value = ''
  account.value = ''
  schema.value = []
  for (const key of Object.keys(fields)) delete fields[key]
  if (!getAccessToken()) return
  try {
    const data = await api<ProfileResp>('/api/me/profile')
    account.value = data.account
    schema.value = data.schema
    for (const item of data.schema) {
      fields[item.key] = data.fields?.[item.key] ?? ''
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : '无法读取档案'
    if (loadError.value.includes('未登录') || loadError.value.includes('401')) {
      clearTokens()
      account.value = ''
      loadError.value = ''
    }
  }
}

function display(value: string | undefined) {
  const v = (value || '').trim()
  return v || '—'
}

function openLogin() {
  openAuthDialog('/login', '/account')
}

function openEdit() {
  openAuthDialog('/edit', '/account')
}

function logout() {
  clearTokens()
  account.value = ''
  schema.value = []
  for (const key of Object.keys(fields)) delete fields[key]
}
</script>
