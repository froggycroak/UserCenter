<template>
  <div class="login-page">
    <div class="login-box">
      <img
        class="login-logo"
        :src="logoSrc"
        width="720"
        height="180"
        alt="Digital Urban Renew"
        @error="onLogoError"
      />
      <p v-if="loadError" class="alert error">{{ loadError }}</p>
      <p v-if="saveMsg" class="alert" :class="saveOk ? 'ok' : 'error'">{{ saveMsg }}</p>

      <form class="login-form" @submit.prevent="saveProfile">
        <label class="login-field">
          <span class="login-field-name">账号</span>
          <input :value="account" readonly title="账号用于登录，不可在此修改" />
        </label>
        <label v-for="item in schema" :key="item.key" class="login-field">
          <span class="login-field-name">{{ item.label }}</span>
          <input :id="item.key" v-model="fields[item.key]" :placeholder="item.label" />
        </label>
        <button class="login-submit" type="submit" :disabled="saving">
          {{ saving ? '保存中…' : '确认' }}
        </button>
      </form>

      <p class="login-section">修改密码</p>
      <p v-if="pwdMsg" class="alert" :class="pwdOk ? 'ok' : 'error'">{{ pwdMsg }}</p>
      <form class="login-form" @submit.prevent="changePassword">
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="6" y="10.5" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.6" />
              <path d="M8.5 10.5V8.2a3.5 3.5 0 0 1 7 0v2.3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
          </span>
          <input v-model="oldPassword" type="password" placeholder="当前密码" autocomplete="current-password" />
        </label>
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="6" y="10.5" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.6" />
              <path d="M8.5 10.5V8.2a3.5 3.5 0 0 1 7 0v2.3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
          </span>
          <input v-model="newPassword" type="password" placeholder="新密码" autocomplete="new-password" />
        </label>
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="6" y="10.5" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.6" />
              <path d="M8.5 10.5V8.2a3.5 3.5 0 0 1 7 0v2.3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
          </span>
          <input v-model="confirmPassword" type="password" placeholder="确认新密码" autocomplete="new-password" />
        </label>
        <button class="login-submit" type="submit" :disabled="pwdSaving">
          {{ pwdSaving ? '提交中…' : '更新密码' }}
        </button>
        <button class="login-forgot login-cancel" type="button" @click="close">取消</button>
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, clearTokens, getAccessToken, setTokens, type ProfileResp, type SchemaItem } from '../api'
import { isEmbed, notifyHost, onHostToken } from '../embed'
import { openAuthDialog, onAuthChanged } from '../authDialog'
import logoLocal from '../assets/udapp-logo-001.png'

const LOGO_REMOTE = 'https://workhub.oss-cn-shanghai.aliyuncs.com/picture/general/logos/udapp-logo-001.png'

const route = useRoute()
const router = useRouter()
const embed = isEmbed(route.query)
const logoSrc = ref(LOGO_REMOTE)
const account = ref('')
const schema = ref<SchemaItem[]>([])
const fields = reactive<Record<string, string>>({})
const loadError = ref('')
const saveMsg = ref('')
const saveOk = ref(false)
const saving = ref(false)
const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const pwdMsg = ref('')
const pwdOk = ref(false)
const pwdSaving = ref(false)

let stopTokenListen: (() => void) | undefined
let stopAuthListen: (() => void) | undefined

onMounted(async () => {
  notifyHost(route.query, { type: 'ready', page: 'edit' })
  const q = route.query.accessToken
  if (typeof q === 'string' && q) {
    setTokens(q, typeof route.query.refreshToken === 'string' ? route.query.refreshToken : '')
    const next = { ...route.query }
    delete next.accessToken
    delete next.refreshToken
    await router.replace({ path: '/edit', query: next })
  }
  stopTokenListen = onHostToken((access, refresh) => {
    setTokens(access, refresh || '')
    void load()
  })
  if (!embed) stopAuthListen = onAuthChanged(() => { void load() })
  await load()
})

onUnmounted(() => {
  stopTokenListen?.()
  stopAuthListen?.()
})

async function load() {
  if (!getAccessToken()) {
    loadError.value = embed ? '等待调用方传入登录态…' : '未登录'
    if (!embed) openAuthDialog('/login', route.fullPath)
    return
  }
  loadError.value = ''
  try {
    const data = await api<ProfileResp>('/api/me/profile')
    account.value = data.account
    schema.value = data.schema
    for (const key of Object.keys(fields)) delete fields[key]
    for (const item of data.schema) {
      fields[item.key] = data.fields?.[item.key] ?? ''
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : '无法加载档案'
    if (loadError.value.includes('未登录') || loadError.value.includes('401')) {
      clearTokens()
      if (!embed) openAuthDialog('/login', route.fullPath)
    }
  }
}

async function saveProfile() {
  saveMsg.value = ''
  saving.value = true
  try {
    await api('/api/me/profile', {
      method: 'PUT',
      body: JSON.stringify({ fields: { ...fields } })
    })
    saveOk.value = true
    saveMsg.value = '资料已保存'
    notifyHost(route.query, { type: 'profile-saved' })
    if (!embed) await close()
  } catch (e) {
    saveOk.value = false
    saveMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    saving.value = false
  }
}

async function changePassword() {
  pwdMsg.value = ''
  if (!oldPassword.value || !newPassword.value) {
    pwdOk.value = false
    pwdMsg.value = '请输入当前密码和新密码'
    return
  }
  if (newPassword.value !== confirmPassword.value) {
    pwdOk.value = false
    pwdMsg.value = '两次输入的新密码不一致'
    return
  }
  pwdSaving.value = true
  try {
    await api('/api/me/password', {
      method: 'POST',
      body: JSON.stringify({
        oldPassword: oldPassword.value,
        newPassword: newPassword.value
      })
    })
    pwdOk.value = true
    pwdMsg.value = '密码已更新'
    oldPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    notifyHost(route.query, { type: 'password-changed' })
  } catch (e) {
    pwdOk.value = false
    pwdMsg.value = e instanceof Error ? e.message : '改密失败'
  } finally {
    pwdSaving.value = false
  }
}

async function close() {
  notifyHost(route.query, { type: 'close' })
  if (embed) return
  const raw = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : ''
  if (raw.startsWith('/') && !raw.startsWith('//')) {
    await router.replace(raw)
    return
  }
  await router.replace('/account')
}

function onLogoError() {
  if (logoSrc.value !== logoLocal) logoSrc.value = logoLocal
}
</script>
