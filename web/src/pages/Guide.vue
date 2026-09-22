<template>
  <div class="container guide-page">
    <header class="section-header">
      <p class="section-eyebrow">GUIDE</p>
      <h2 class="section-title">接入说明</h2>
      <p class="guide-lead">
        给其它网页应用：用本服务的登录 / 注册 / 资料弹窗，不要再打开 Casdoor 原生页。
      </p>
      <div class="guide-actions">
        <a class="btn btn-black" :href="demoHref" target="_blank" rel="noopener">打开联调演示</a>
        <button class="btn btn-secondary" type="button" @click="copyOrigin">复制本服务 Origin</button>
      </div>
      <p v-if="copied" class="guide-copied" role="status">已复制 {{ origin }}</p>
    </header>

    <nav class="guide-toc" aria-label="本页目录">
      <a href="#urls">打开哪些页面</a>
      <a href="#iframe">iframe 接法</a>
      <a href="#messages">postMessage</a>
      <a href="#after-token">拿到 Token 之后</a>
      <a href="#whitelist">白名单</a>
    </nav>

    <section id="urls" class="guide-section">
      <h3>1. 打开哪些页面</h3>
      <table class="guide-table">
        <thead>
          <tr>
            <th>用途</th>
            <th>地址</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>登录</td>
            <td><code>{{ origin }}/login?embed=1&amp;parentOrigin=&lt;你的 origin&gt;</code>（可选 <code>&amp;clientId=stockase</code>）</td>
          </tr>
          <tr>
            <td>注册</td>
            <td><code>{{ origin }}/register?embed=1&amp;parentOrigin=&lt;你的 origin&gt;</code>（本站注册走 <code>usercenter</code>）</td>
          </tr>
          <tr>
            <td>资料 / 改密</td>
            <td><code>{{ origin }}/edit?embed=1&amp;parentOrigin=&lt;你的 origin&gt;</code>（须已有 accessToken）</td>
          </tr>
        </tbody>
      </table>
      <p class="guide-note">
        <code>parentOrigin</code> 写成你网站的 origin（不要带路径），用于 <code>postMessage</code> 校验。
        推荐 iframe + 遮罩；也可用 <code>window.open</code>。
        本站未登录时也会弹出同一套登录窗，不再整页跳到 <code>/login</code>。
        不传 <code>clientId</code> 时默认 <code>protomassbrowser</code>（给其它应用）。
        本站门户的登录 / 注册 / 改资料使用 <code>usercenter</code>。
        传入值须在服务端 <code>AllowedClientIds</code> 内。
      </p>
    </section>

    <section id="iframe" class="guide-section">
      <h3>2. 推荐接法（iframe）</h3>
      <ol class="guide-list">
        <li>自己做半透明遮罩，里面放 iframe，<code>src</code> 为上一节地址。</li>
        <li>
          监听 <code>message</code>，只处理
          <code>event.origin === '{{ origin }}'</code>
          且 <code>data.source === 'udapp-user-center'</code>。
        </li>
        <li>登录成功：保存 <code>accessToken</code> / <code>refreshToken</code>，关掉 iframe。</li>
        <li>打开资料页：等收到 <code>ready</code>（<code>page === 'edit'</code>）后再把 token 注入。</li>
        <li>收到 <code>close</code> / <code>profile-saved</code> 后关掉 iframe。</li>
      </ol>
      <pre class="guide-code"><code>{{ sampleCode }}</code></pre>
    </section>

    <section id="messages" class="guide-section">
      <h3>3. postMessage 约定</h3>
      <p class="guide-note">宿主 → 弹窗（仅 <code>/edit</code>）：</p>
      <pre class="guide-code"><code>{{ setTokenCode }}</code></pre>
      <p class="guide-note">弹窗 → 宿主：</p>
      <table class="guide-table">
        <thead>
          <tr>
            <th><code>type</code></th>
            <th>含义</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>ready</code></td>
            <td>iframe 已就绪。<code>page</code> 为 <code>login</code> / <code>register</code> / <code>edit</code>。编辑页此时再 <code>setToken</code>。</td>
          </tr>
          <tr>
            <td><code>login</code></td>
            <td>登录成功，带 <code>accessToken</code>、<code>refreshToken</code>、<code>sub</code>、<code>id</code>、<code>name</code>、<code>account</code>、<code>clientId</code>。</td>
          </tr>
          <tr>
            <td><code>profile-saved</code></td>
            <td>资料已写入用户中心库。</td>
          </tr>
          <tr>
            <td><code>password-changed</code></td>
            <td>密码已在 Casdoor 更新。</td>
          </tr>
          <tr>
            <td><code>close</code></td>
            <td>用户取消，关掉即可。</td>
          </tr>
        </tbody>
      </table>
      <p class="guide-note">
        不要把 token 长期放在 URL 上。<code>/edit?accessToken=</code> 仅应急，弹窗读完会从地址栏去掉。
        注册成功不会发 <code>login</code>，弹窗内会回到登录页。
      </p>
    </section>

    <section id="after-token" class="guide-section">
      <h3>4. 拿到 Token 之后</h3>
      <ul class="guide-list">
        <li>
          调本服务档案接口，带 <code>Authorization: Bearer &lt;accessToken&gt;</code>：
          <code>GET /api/me/profile</code>、<code>GET /api/auth/me</code>。
        </li>
        <li>调你们自己的后端：若已按 Casdoor JWKS 验签，继续带这张 JWT；用户 Id 用 <strong>sub</strong>。</li>
        <li>过期：<code>POST /api/auth/refresh</code>，body <code>{ "refreshToken": "..." }</code>；若登录时用了非默认 clientId，刷新须带同一个 <code>clientId</code>。</li>
        <li>退出：丢掉本地 token；需要时再打开 <code>/login</code>。</li>
      </ul>
      <p class="guide-note">
        JWT <code>aud</code> / <code>azp</code> 等于登录时使用的 <code>clientId</code>。
        不传时默认 <code>protomassbrowser</code>；本站注册与改资料为 <code>usercenter</code>。
        后端若白名单 audience，请包含对应值。
      </p>
    </section>

    <section id="whitelist" class="guide-section">
      <h3>5. 上线前白名单</h3>
      <ul class="guide-list">
        <li>iframe 调弹窗、弹窗自己调 <code>/api</code>，一般不必跨域。</li>
        <li>
          若页面要直接 <code>fetch('{{ origin }}/api/...')</code>，把你的 origin 加进服务端
          <code>CorsOrigins</code>。
        </li>
        <li>
          整页跳转 <code>/login?returnUrl=</code>（不如 iframe）时，<code>returnUrl</code> 须在
          <code>ReturnUrlAllowList</code> 内；成功后带一次性 <code>code</code>，再
          <code>POST /api/auth/exchange</code> 换 token。
        </li>
        <li>
          自定义 Casdoor 应用（如 <code>clientId=stockase</code>）须加入服务端
          <code>AllowedClientIds</code>；未知值会返回 400。
        </li>
      </ul>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'

const origin = typeof location !== 'undefined' ? location.origin : 'http://localhost:1010'
const demoHref = `${origin}/embed-demo.html`
const copied = ref(false)

const sampleCode = computed(() => `const UC = '${origin}'

function openUserCenter(path) {
  const iframe = document.createElement('iframe')
  iframe.src = \`\${UC}\${path}?embed=1&parentOrigin=\${encodeURIComponent(location.origin)}\`
  // 将 iframe 放入你自己的遮罩…
  window.addEventListener('message', onMsg)
}

function onMsg(event) {
  if (event.origin !== UC) return
  const data = event.data
  if (!data || data.source !== 'udapp-user-center') return

  if (data.type === 'ready' && data.page === 'edit') {
    event.source.postMessage({
      source: 'udapp-user-center-host',
      type: 'setToken',
      accessToken: sessionStorage.getItem('accessToken'),
      refreshToken: sessionStorage.getItem('refreshToken')
    }, UC)
  }
  if (data.type === 'login') {
    sessionStorage.setItem('accessToken', data.accessToken)
    sessionStorage.setItem('refreshToken', data.refreshToken)
    // 关掉遮罩
  }
  if (data.type === 'close' || data.type === 'profile-saved') {
    // 关掉遮罩
  }
}`)

const setTokenCode = computed(() => `iframe.contentWindow.postMessage({
  source: 'udapp-user-center-host',
  type: 'setToken',
  accessToken,
  refreshToken
}, '${origin}')`)

async function copyOrigin() {
  try {
    await navigator.clipboard.writeText(origin)
    copied.value = true
    window.setTimeout(() => { copied.value = false }, 2000)
  } catch {
    copied.value = false
  }
}
</script>
