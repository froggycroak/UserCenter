/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly BASE_URL: string
  readonly VITE_STATIC_DEMO?: string
  readonly VITE_BASE?: string
  readonly VITE_TOOLKIT_HOST?: string
  readonly VITE_SITE_TITLE?: string
  readonly VITE_SITE_TAGLINE?: string
  readonly VITE_SITE_FOOTER?: string
  readonly VITE_SUPPORT_NAME?: string
  readonly VITE_SUPPORT_EMAIL?: string
  readonly VITE_DEV_API_TARGET?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<object, object, unknown>
  export default component
}

declare module '*.png' {
  const src: string
  export default src
}
