<template>
  <div class="container tools-layout">
    <aside class="tools-toc" aria-label="分类目录">
      <nav class="toc-nav">
        <a
          v-for="section in sections"
          :key="section.id"
          :href="'#section-' + section.id"
          class="toc-link"
          :class="{ 'is-active': activeId === section.id }"
          :aria-current="activeId === section.id ? 'true' : undefined"
          @click.prevent="scrollTo(section.id)"
        >
          <span class="toc-zh">{{ section.title }}</span>
          <span v-if="section.eyebrow" class="toc-en">{{ section.eyebrow }}</span>
        </a>
      </nav>
    </aside>
    <div ref="scrollRoot" class="tools-scroll">
      <section
        v-for="(section, sIndex) in sections"
        :id="'section-' + section.id"
        :key="section.id"
        class="tools-section"
      >
        <header class="section-header store-enter" :style="{ '--enter-delay': `${sIndex * 0.1}s` }">
          <p v-if="section.eyebrow" class="section-eyebrow">{{ section.eyebrow }}</p>
          <h2 class="section-title">{{ section.title }}</h2>
        </header>
        <div class="store-panel">
          <div class="tools-grid">
            <a
              v-for="(tool, index) in section.tools"
              :key="tool.abbr"
              :href="tool.url === '#' ? undefined : tool.url"
              class="tool-card store-enter"
              :class="{ 'is-unavailable': tool.url === '#' }"
              :style="{ '--enter-delay': `${(sIndex * 0.1 + index * 0.06).toFixed(2)}s` }"
              :target="tool.url === '#' ? undefined : '_blank'"
              :rel="tool.url === '#' ? undefined : 'noopener'"
              :aria-disabled="tool.url === '#' ? 'true' : undefined"
              @click="tool.url === '#' ? $event.preventDefault() : undefined"
            >
              <div class="tool-icon" :class="{ 'is-lg': tool.iconLg }">
                <img :src="tool.icon" :alt="tool.abbr" class="tool-icon-img" />
              </div>
              <div class="tool-info">
                <p class="tool-abbr">{{ tool.abbr }}</p>
                <p class="tool-name">{{ tool.name }}</p>
              </div>
            </a>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { toolSections } from '../tools-data'

const sections = toolSections
const activeId = ref(sections[0]?.id ?? '')
const scrollRoot = ref<HTMLElement | null>(null)
let lockFromClick = false
let unlockTimer: number | undefined

function sectionEl(id: string) {
  return document.getElementById(`section-${id}`)
}

function isNestedScroll(el: HTMLElement | null): el is HTMLElement {
  if (!el) return false
  const { overflowY } = getComputedStyle(el)
  return overflowY === 'auto' || overflowY === 'scroll'
}

function scrollTo(id: string) {
  const el = sectionEl(id)
  const root = scrollRoot.value
  if (!el) return
  activeId.value = id
  lockFromClick = true
  window.clearTimeout(unlockTimer)
  if (isNestedScroll(root)) {
    const top = el.getBoundingClientRect().top - root.getBoundingClientRect().top + root.scrollTop
    root.scrollTo({ top: Math.max(0, top), behavior: 'auto' })
  } else {
    el.scrollIntoView({ behavior: 'auto', block: 'start' })
  }
  unlockTimer = window.setTimeout(() => {
    lockFromClick = false
  }, 160)
}

function syncActive() {
  if (lockFromClick) return
  const nodes = sections
    .map((section) => sectionEl(section.id))
    .filter((node): node is HTMLElement => !!node)
  if (!nodes.length) return

  const root = scrollRoot.value
  const nested = isNestedScroll(root)
  const header = document.querySelector<HTMLElement>('.header')
  const probe = nested
    ? root.getBoundingClientRect().top + 8
    : (header?.getBoundingClientRect().bottom ?? 0) + 8

  let current = sections[0]?.id ?? ''
  for (let i = 0; i < nodes.length; i++) {
    if (nodes[i].getBoundingClientRect().top <= probe + 1) current = sections[i].id
  }

  if (nested && root.scrollTop + root.clientHeight >= root.scrollHeight - 2) {
    current = sections[sections.length - 1]?.id ?? current
  } else if (!nested) {
    const doc = document.documentElement
    if (window.scrollY + window.innerHeight >= doc.scrollHeight - 2) {
      current = sections[sections.length - 1]?.id ?? current
    }
  }
  activeId.value = current
}

onMounted(() => {
  const root = scrollRoot.value
  if (isNestedScroll(root)) {
    root.addEventListener('scroll', syncActive, { passive: true })
  } else {
    window.addEventListener('scroll', syncActive, { passive: true })
  }
})

onUnmounted(() => {
  window.clearTimeout(unlockTimer)
  scrollRoot.value?.removeEventListener('scroll', syncActive)
  window.removeEventListener('scroll', syncActive)
})
</script>
