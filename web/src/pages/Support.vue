<template>
  <div class="container tools-layout account-layout">
    <AccountAside return-path="/support" />
    <div class="support-main">
      <section class="tools-section support-section">
        <div class="layout-main-head">
          <header class="section-header">
            <p class="section-eyebrow">SUPPORT</p>
            <h2 class="section-title">技术支持</h2>
          </header>
        </div>
        <p class="support-lead">
          有任何疑问或建议，请联系胡工
          <a class="support-lead-mail" href="mailto:huqian.archi@outlook.com">huqian.archi@outlook.com</a>
        </p>

        <form class="support-form" @submit.prevent="submit">
          <div class="support-form-fill" aria-hidden="true" />
          <div class="support-form-foot">
            <p v-if="error" class="alert error">{{ error }}</p>
            <p v-if="okMessage" class="alert ok">{{ okMessage }}</p>

            <div class="support-tags" role="group" aria-label="产品">
              <button
                v-for="tag in productTags"
                :key="tag"
                type="button"
                class="support-tag"
                :class="{ 'is-active': productTag === tag }"
                @click="selectTag(tag)"
              >
                {{ tag }}
              </button>
            </div>

            <div class="assistant-composer support-composer">
              <textarea
                v-model="content"
                class="assistant-input"
                rows="1"
                maxlength="4000"
                placeholder="描述你的问题或建议"
              />
              <button
                class="assistant-send"
                type="submit"
                :disabled="busy || !content.trim()"
                aria-label="提交"
              >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                  <path
                    d="M12 19V5M12 5l-7 7M12 5l7 7"
                    stroke="currentColor"
                    stroke-width="2.2"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  />
                </svg>
              </button>
            </div>
          </div>
        </form>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { api, clearTokens, getAccessToken } from '../api'
import { openAuthDialog } from '../authDialog'
import AccountAside from '../components/AccountAside.vue'
import { toolSections } from '../tools-data'

const productTags = computed(() => {
  const seen = new Set<string>()
  const tags: string[] = []
  for (const section of toolSections) {
    if (section.external) continue
    for (const tool of section.tools) {
      if (seen.has(tool.abbr)) continue
      seen.add(tool.abbr)
      tags.push(tool.abbr)
    }
  }
  tags.push('其他')
  return tags
})

const productTag = ref('')
const content = ref('')
const busy = ref(false)
const error = ref('')
const okMessage = ref('')

function selectTag(tag: string) {
  productTag.value = tag
  error.value = ''
  okMessage.value = ''
}

async function submit() {
  error.value = ''
  okMessage.value = ''
  if (!getAccessToken()) {
    openAuthDialog('/login', '/support')
    return
  }
  if (!productTag.value) {
    error.value = '请选择产品标签'
    return
  }
  if (!content.value.trim()) {
    error.value = '请填写内容'
    return
  }

  busy.value = true
  try {
    await api<{ ok: boolean }>('/api/suggestions', {
      method: 'POST',
      body: JSON.stringify({
        productTag: productTag.value,
        productOther: '',
        content: content.value.trim()
      })
    })
    okMessage.value = '已提交，感谢你的建议。'
    productTag.value = ''
    content.value = ''
  } catch (e) {
    const message = e instanceof Error ? e.message : '提交失败'
    if (message.includes('未登录') || message.includes('401')) {
      clearTokens()
      openAuthDialog('/login', '/support')
      return
    }
    error.value = message
  } finally {
    busy.value = false
  }
}
</script>
