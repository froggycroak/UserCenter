<template>
  <div
    v-if="authDialog.open"
    class="modal-backdrop auth-dialog-backdrop"
    @click.self="closeAuthDialog"
  >
    <iframe
      class="auth-dialog-frame"
      :src="frameSrc"
      :title="title"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getAccessToken, getRefreshToken, setTokens } from './api'
import { authDialog, closeAuthDialog, notifyAuthChanged } from './authDialog'
import { SELF_CLIENT_ID } from './embed'

const router = useRouter()
const route = useRoute()

const title = computed(() => {
  if (authDialog.path === '/edit') return '修改信息'
  if (authDialog.path === '/register') return '注册'
  return '登录'
})

const frameSrc = computed(() => {
  const q = new URLSearchParams({
    embed: '1',
    parentOrigin: location.origin
  })
  if (authDialog.path !== '/edit') q.set('clientId', SELF_CLIENT_ID)
  const base = (import.meta.env.BASE_URL || '/').replace(/\/$/, '')
  return base + authDialog.path + '?' + q.toString()
})

function onMessage(event: MessageEvent) {
  if (!authDialog.open) return
  if (event.origin !== location.origin) return
  const data = event.data
  if (!data || data.source !== 'udapp-user-center') return

  if (data.type === 'ready' && data.page === 'edit') {
    const access = getAccessToken()
    if (!access || !event.source) return
    ;(event.source as Window).postMessage({
      source: 'udapp-user-center-host',
      type: 'setToken',
      accessToken: access,
      refreshToken: getRefreshToken()
    }, location.origin)
  }

  if (data.type === 'login') {
    setTokens(data.accessToken, data.refreshToken, undefined, data.clientId)
    const next = authDialog.returnUrl
    closeAuthDialog()
    notifyAuthChanged()
    if (next && next !== route.fullPath) void router.replace(next)
    return
  }

  if (data.type === 'profile-saved') {
    closeAuthDialog()
    notifyAuthChanged()
    return
  }
  if (data.type === 'close') {
    closeAuthDialog()
  }
}

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape' && authDialog.open) closeAuthDialog()
}

onMounted(() => {
  window.addEventListener('message', onMessage)
  window.addEventListener('keydown', onKey)
})
onUnmounted(() => {
  window.removeEventListener('message', onMessage)
  window.removeEventListener('keydown', onKey)
})
</script>
