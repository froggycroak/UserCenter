<template>
  <div class="container tools-layout assistant-layout">
    <aside class="assistant-history" aria-label="会话列表">
      <p class="assistant-history-empty">对话历史未开通</p>
      <button class="btn btn-secondary assistant-new" type="button" @click="resetChat">新对话</button>
    </aside>
    <div class="assistant-chat">
      <div ref="threadEl" class="assistant-thread" aria-live="polite">
        <div v-if="!messages.length" class="assistant-empty">
          <p class="assistant-empty-title">有什么可以帮忙的？</p>
          <p class="assistant-empty-hint">我会尽力帮你想办法...</p>
        </div>
        <div v-else class="assistant-msgs">
          <div
            v-for="(item, index) in messages"
            :key="index"
            class="assistant-msg"
            :class="'is-' + item.role"
          >
            <div v-if="item.role === 'user'" class="assistant-msg-bubble">{{ item.content }}</div>
            <div v-else class="assistant-msg-md">
              <div v-if="item.tools?.length" class="assistant-tools">
                <span
                  v-for="tool in item.tools"
                  :key="tool.name"
                  class="assistant-tool"
                  :class="'is-' + tool.status"
                >{{ tool.label }}</span>
              </div>
              <span v-if="item.content">{{ item.content }}</span>
              <span v-else class="assistant-msg-pending">{{ item.pending || '正在思考…' }}</span>
              <span
                v-if="sending && index === messages.length - 1"
                class="assistant-caret"
                aria-hidden="true"
              />
            </div>
          </div>
        </div>
      </div>
      <p v-if="error" class="alert error assistant-error">{{ error }}</p>
      <form class="assistant-composer" @submit.prevent="send">
        <textarea
          ref="inputEl"
          v-model="draft"
          class="assistant-input"
          rows="1"
          placeholder="输入消息"
          :disabled="sending"
          @keydown="onKey"
          @input="autosize"
        />
        <button class="assistant-send" type="submit" :disabled="sending || !draft.trim()" aria-label="发送">
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
      </form>
      <p class="assistant-composer-hint">内容由模型生成，请核实关键信息。Enter 发送，Shift+Enter 换行。</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { clearTokens, getAccessToken, isStaticDemo, streamAssistantChat, type ChatMessage } from '../api'
import { openAuthDialog } from '../authDialog'

const messages = ref<ChatMessage[]>([])
const draft = ref('')
const sending = ref(false)
const error = ref('')
const threadEl = ref<HTMLElement | null>(null)
const inputEl = ref<HTMLTextAreaElement | null>(null)
let abort: AbortController | undefined

onMounted(() => inputEl.value?.focus())
onBeforeUnmount(() => abort?.abort())

function resetChat() {
  abort?.abort()
  abort = undefined
  sending.value = false
  error.value = ''
  messages.value = []
  draft.value = ''
  void nextTick(() => {
    autosize()
    inputEl.value?.focus()
  })
}

function onKey(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void send()
  }
}

function autosize() {
  const el = inputEl.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 160)}px`
}

async function scrollToEnd() {
  await nextTick()
  const el = threadEl.value
  if (el) el.scrollTop = el.scrollHeight
}

async function send() {
  const text = draft.value.trim()
  if (!text || sending.value) return
  if (!isStaticDemo() && !getAccessToken()) {
    openAuthDialog('/login', '/assistant')
    return
  }
  error.value = ''
  draft.value = ''
  void nextTick(autosize)

  messages.value.push({ role: 'user', content: text })
  const history = messages.value.map((item) => ({ ...item }))
  messages.value.push({ role: 'assistant', content: '' })
  const replyIndex = messages.value.length - 1
  sending.value = true
  abort = new AbortController()
  await scrollToEnd()

  try {
    await streamAssistantChat(
      history,
      (delta) => {
        messages.value[replyIndex].content += delta
        messages.value[replyIndex].pending = undefined
        void scrollToEnd()
      },
      abort.signal,
      (tool) => {
        const reply = messages.value[replyIndex]
        const chips = reply.tools ? [...reply.tools] : []
        const idx = chips.findIndex((item) => item.name === tool.name)
        if (idx >= 0) chips[idx] = tool
        else chips.push(tool)
        reply.tools = chips
        reply.pending = tool.status === 'start' ? `正在${tool.label}…` : reply.pending
        void scrollToEnd()
      }
    )
    if (!messages.value[replyIndex]?.content) {
      messages.value[replyIndex].content = '（无回复）'
    }
  } catch (e) {
    if ((e instanceof DOMException || e instanceof Error) && e.name === 'AbortError') return
    const msg = e instanceof Error ? e.message : '发送失败'
    if (msg.includes('未登录') || msg.includes('401')) {
      clearTokens()
      openAuthDialog('/login', '/assistant')
      return
    }
    error.value = msg
    if (!messages.value[replyIndex]?.content) messages.value.splice(replyIndex, 1)
  } finally {
    sending.value = false
    abort = undefined
    void nextTick(() => inputEl.value?.focus())
  }
}
</script>
