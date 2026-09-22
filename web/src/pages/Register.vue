<template>
  <div class="login-page login-page-register">
    <div class="login-box">
      <img
        class="login-logo"
        :src="logoSrc"
        width="720"
        height="180"
        alt="Digital Urban Renew"
        @error="onLogoError"
      />
      <p v-if="error" class="alert error">{{ error }}</p>
      <form class="login-form" @submit.prevent="submit">
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <path
                d="M4.5 8.25h15v2.6a2.15 2.15 0 0 1 0 4.3v2.6h-15v-2.6a2.15 2.15 0 0 1 0-4.3v-2.6Z"
                stroke="currentColor"
                stroke-width="1.6"
              />
              <path d="M9 8.25v11.15" stroke="currentColor" stroke-width="1.6" stroke-dasharray="1.8 1.8" />
            </svg>
          </span>
          <input
            v-model="inviteCode"
            name="inviteCode"
            type="text"
            placeholder="邀请码"
            autocomplete="off"
            required
          />
        </label>
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
            v-model="username"
            name="username"
            type="text"
            placeholder="账号"
            autocomplete="username"
            required
          />
          <span class="login-field-suffix">@arcplus.com.cn</span>
        </label>
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="6" y="10.5" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.6" />
              <path d="M8.5 10.5V8.2a3.5 3.5 0 0 1 7 0v2.3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
          </span>
          <input
            v-model="password"
            :type="showPwd ? 'text' : 'password'"
            name="password"
            placeholder="密码（至少 6 位）"
            autocomplete="new-password"
            required
          />
          <button class="login-eye" type="button" :aria-label="showPwd ? '隐藏密码' : '显示密码'" @click="showPwd = !showPwd">
            <svg v-if="!showPwd" viewBox="0 0 24 24" fill="none">
              <path d="M4 12s3.2-5.5 8-5.5S20 12 20 12s-3.2 5.5-8 5.5S4 12 4 12Z" stroke="currentColor" stroke-width="1.6" />
              <circle cx="12" cy="12" r="2.2" stroke="currentColor" stroke-width="1.6" />
              <path d="M5 19 19 5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
            <svg v-else viewBox="0 0 24 24" fill="none">
              <path d="M4 12s3.2-5.5 8-5.5S20 12 20 12s-3.2 5.5-8 5.5S4 12 4 12Z" stroke="currentColor" stroke-width="1.6" />
              <circle cx="12" cy="12" r="2.2" stroke="currentColor" stroke-width="1.6" />
            </svg>
          </button>
        </label>
        <label class="login-field">
          <span class="login-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="6" y="10.5" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.6" />
              <path d="M8.5 10.5V8.2a3.5 3.5 0 0 1 7 0v2.3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
            </svg>
          </span>
          <input
            v-model="confirm"
            :type="showPwd ? 'text' : 'password'"
            name="confirm"
            placeholder="确认密码"
            autocomplete="new-password"
            required
          />
        </label>
        <button class="login-submit" type="submit" :disabled="loading">
          {{ loading ? '提交中…' : '注册' }}
        </button>
        <button class="login-forgot login-cancel" type="button" @click="goLogin">返回登录</button>
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../api'
import { isEmbed, notifyHost, withLoginClientId } from '../embed'
import logoLocal from '../assets/udapp-logo-001.png'

const LOGO_REMOTE = 'https://workhub.oss-cn-shanghai.aliyuncs.com/picture/general/logos/udapp-logo-001.png'

const route = useRoute()
const router = useRouter()
const embed = isEmbed(route.query)
const logoSrc = ref(LOGO_REMOTE)
const username = ref('')
const password = ref('')
const confirm = ref('')
const inviteCode = ref('')
const showPwd = ref(false)
const loading = ref(false)
const error = ref('')

onMounted(() => {
  notifyHost(route.query, { type: 'ready', page: 'register' })
})

function loginQuery() {
  const query = withLoginClientId(route.query)
  if (embed) query.embed = '1'
  const parent = typeof route.query.parentOrigin === 'string' ? route.query.parentOrigin : ''
  if (parent) query.parentOrigin = parent
  return query
}

function goLogin() {
  void router.replace({ path: '/login', query: loginQuery() })
}

function accountPrefix() {
  const raw = username.value.trim()
  const at = raw.indexOf('@')
  return (at >= 0 ? raw.slice(0, at) : raw).trim()
}

async function submit() {
  error.value = ''
  if (!accountPrefix()) {
    error.value = '请输入账号'
    return
  }
  if (password.value !== confirm.value) {
    error.value = '两次输入的密码不一致'
    return
  }
  if (password.value.length < 6) {
    error.value = '密码至少 6 位'
    return
  }
  loading.value = true
  try {
    await api('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({
        username: accountPrefix(),
        password: password.value,
        inviteCode: inviteCode.value
      })
    })
    await router.replace({ path: '/login', query: loginQuery() })
  } catch (e) {
    error.value = e instanceof Error ? e.message : '注册失败'
  } finally {
    loading.value = false
  }
}

function onLogoError() {
  if (logoSrc.value !== logoLocal) logoSrc.value = logoLocal
}
</script>
