<template>
  <div class="shell" :class="{ 'is-toolkit': isToolkit }">
    <header v-show="showChrome" class="header">
      <div class="container header-inner">
        <router-link to="/toolkit" class="header-brand" title="工具导航">
          <span class="header-brand-text">
            <h1 class="logo">{{ siteTitle }}</h1>
            <p class="tagline">{{ siteTagline }}</p>
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
        <p>{{ siteFooter }}</p>
      </div>
    </footer>
    <AuthDialog v-if="!isEmbedMode" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AuthDialog from './AuthDialog.vue'
import { isEmbed } from './embed'

const route = useRoute()
const isDialog = computed(() => route.path === '/login' || route.path === '/edit' || route.path === '/register')
const isEmbedMode = computed(() => isEmbed(route.query))
const siteTitle = (import.meta.env.VITE_SITE_TITLE || 'SSTD DIGITAL PORTAL').trim()
const siteTagline = (import.meta.env.VITE_SITE_TAGLINE || '华建集团中央研究院 · 数字化导航').trim()
const siteFooter = (import.meta.env.VITE_SITE_FOOTER || '© 2026 SSTD Digital').trim()

const isToolkit = computed(() =>
  route.path === '/toolkit'
  || route.path === '/'
  || route.path === '/account'
  || route.path === '/external'
  || route.path === '/support'
)
const showChrome = computed(() => !isEmbedMode.value && !isDialog.value)

const nav = [
  { to: '/toolkit', zh: '工具导航', en: 'Toolkit' },
  { to: '/external', zh: '外部资源', en: 'External' },
  { to: '/account', zh: '用户中心', en: 'Account' },
  { to: '/support', zh: '技术支持', en: 'Support' }
]
</script>