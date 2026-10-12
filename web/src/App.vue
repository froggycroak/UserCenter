<template>
  <div class="shell" :class="{ 'is-dialog': isDialog, 'is-embed': isEmbedMode, 'is-toolkit': isToolkit, 'is-ops': isOps }">
    <header v-show="showChrome" class="header">
      <HeaderMapLineArt />
      <div class="container header-inner">
        <router-link to="/toolkit" class="header-brand" title="工具导航">
          <span class="header-brand-text">
            <h1 class="logo">Digital Urban Renew</h1>
            <p class="tagline">建筑与城市更新研究 · 数字化工具导航</p>
          </span>
        </router-link>
        <div class="header-bar">
          <nav class="header-nav" aria-label="主导航">
            <router-link
              v-for="item in nav"
              :key="item.to"
              :to="item.to"
              :data-nav="item.to"
            >
              <span class="nav-zh">{{ item.zh }}</span>
              <span class="nav-en">{{ item.en }}</span>
            </router-link>
          </nav>
        </div>
      </div>
    </header>
    <main class="main">
      <router-view />
    </main>
    <footer v-show="showChrome && !isDialog" class="footer">
      <div class="container">
        <p>© 2026 SSTD-Digital</p>
      </div>
    </footer>
    <AuthDialog v-if="!isEmbedMode" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AuthDialog from './AuthDialog.vue'
import HeaderMapLineArt from './components/HeaderMapLineArt.vue'
import { isEmbed } from './embed'

const route = useRoute()
const isDialog = computed(() => route.path === '/login' || route.path === '/edit' || route.path === '/register')
const isOps = computed(() => route.path === '/__ops' || route.path.startsWith('/__ops/') || route.path === '/ops' || route.path.startsWith('/ops/'))
const isEmbedMode = computed(() => isEmbed(route.query))
const isToolkit = computed(() =>
  route.path === '/toolkit'
  || route.path === '/'
  || route.path === '/account'
  || route.path === '/external'
  || route.path === '/guide'
  || route.path === '/support'
)
const showChrome = computed(() => !isEmbedMode.value && !isDialog.value && !isOps.value)

const nav = [
  { to: '/toolkit', zh: '工具导航', en: 'Toolkit' },
  { to: '/external', zh: '外部资源', en: 'External' },
  { to: '/account', zh: '用户中心', en: 'Account' },
  { to: '/support', zh: '技术支持', en: 'Support' }
]
</script>
