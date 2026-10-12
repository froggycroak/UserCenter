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
      <p v-if="error" class="alert" :class="isAccessDenied ? 'muted' : 'error'">{{ error }}</p>
      <form class="login-form" @submit.prevent="submit">
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <circle cx="12" cy="8" r="3.25" stroke="currentColor" stroke-width="1.6" />
              <path
                d="M5.5 18.5c1.4-2.8 3.7-4.2 6.5-4.2s5.1 1.4 6.5 4.2"
                stroke="currentColor"
                stroke-width="1.6"
                stroke-linecap="round"
              />
            </svg>
          </span>
          <input
            id="account"
            ref="accountInput"
            v-model="username"
            name="username"
            type="text"
            placeholder="账号"
            autocomplete="username"
            required
          />
        </label>
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="6" y="10.5" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.6" />
              <path
                d="M8.5 10.5V8.2a3.5 3.5 0 0 1 7 0v2.3"
                stroke="currentColor"
                stroke-width="1.6"
                stroke-linecap="round"
              />
            </svg>
          </span>
          <input
            id="password"
            v-model="password"
            :type="showPwd ? 'text' : 'password'"
            name="password"
            placeholder="密码"
            autocomplete="current-password"
            required
          />
          <button class="login-eye" type="button" :aria-label="showPwd ? '隐藏密码' : '显示密码'" @click="showPwd = !showPwd">
            <svg v-if="!showPwd" viewBox="0 0 24 24" fill="none">
              <path
                d="M4 12s3.2-5.5 8-5.5S20 12 20 12s-3.2 5.5-8 5.5S4 12 4 12Z"
                stroke="currentColor"
                stroke-width="1.6"
              />
              <circle cx="12" cy="12" r="2.2" stroke="currentColor" stroke-width="1.6" />
              <path d="M5 19 19 5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
            <svg v-else viewBox="0 0 24 24" fill="none">
              <path
                d="M4 12s3.2-5.5 8-5.5S20 12 20 12s-3.2 5.5-8 5.5S4 12 4 12Z"
                stroke="currentColor"
                stroke-width="1.6"
              />
              <circle cx="12" cy="12" r="2.2" stroke="currentColor" stroke-width="1.6" />
            </svg>
          </button>
        </label>
        <div class="login-meta">
          <label class="login-remember">
            <input v-model="remember" type="checkbox" />
            <span>下次自动登录</span>
          </label>
          <span class="login-links">
            <button class="login-forgot" type="button" @click="goRegister">注册</button>
          </span>
        </div>
        <button class="login-submit" type="submit" :disabled="loading">
          {{ loading ? '登录中…' : '登录' }}
        </button>
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, clearTokens, getAccessToken, setTokens } from '../api'
import { isEmbed, notifyHost, resolveLoginClientId, withLoginClientId } from '../embed'
import logoLocal from '../assets/udapp-logo-001.png'

const LOGO_REMOTE = 'https://workhub.oss-cn-shanghai.aliyuncs.com/picture/general/logos/udapp-logo-001.png'

const route = useRoute()
const router = useRouter()
const embed = isEmbed(route.query)
const logoSrc = ref(LOGO_REMOTE)
const username = ref('')
const password = ref('')
const remember = ref(true)
const showPwd = ref(false)
const loading = ref(false)
const error = ref('')
const ACCESS_DENIED_MSG = '您的账户未开通本应用，请联系管理员'
const isAccessDenied = computed(() => error.value === ACCESS_DENIED_MSG)
const accountInput = ref<HTMLInputElement | null>(null)

type LoginResp = {
  accessToken: string
  refreshToken: string
  sub?: string
  id?: string
  name?: string
  account?: string
  clientId?: string
  redirectTo?: string | null
}

onMounted(() => {
  notifyHost(route.query, { type: 'ready', page: 'login' })
  accountInput.value?.focus()
  if (getAccessToken() && !embed) {
    const rawReturn = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : ''
    if (rawReturn.startsWith('/') && !rawReturn.startsWith('//')) {
      void api('/api/auth/me')
        .then(() => router.replace(rawReturn))
        .catch(() => clearTokens())
    }
  }
})

async function submit() {
  error.value = ''
  loading.value = true
  try {
    const rawReturn = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : undefined
    const internalReturn = rawReturn && rawReturn.startsWith('/') && !rawReturn.startsWith('//') ? rawReturn : ''
    const returnUrl = rawReturn && /^https?:\/\//i.test(rawReturn) ? rawReturn : undefined
    const clientId = resolveLoginClientId(route.query)
    const data = await api<LoginResp>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({
        username: username.value,
        password: password.value,
        returnUrl,
        clientId
      })
    })
    setTokens(data.accessToken, data.refreshToken, remember.value, data.clientId)
    notifyHost(route.query, {
      type: 'login',
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      sub: data.sub,
      id: data.id,
      name: data.name,
      account: data.account ?? data.name,
      clientId: data.clientId
    })
    if (data.redirectTo) {
      window.location.assign(data.redirectTo)
      return
    }
    if (embed) return
    await router.replace(internalReturn || '/toolkit')
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登录失败'
  } finally {
    loading.value = false
  }
}

function goRegister() {
  const query = withLoginClientId(route.query)
  if (embed) query.embed = '1'
  const parent = typeof route.query.parentOrigin === 'string' ? route.query.parentOrigin : ''
  if (parent) query.parentOrigin = parent
  void router.push({ path: '/register', query })
}

function onLogoError() {
  if (logoSrc.value !== logoLocal) logoSrc.value = logoLocal
}
</script>
