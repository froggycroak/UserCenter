import { copyFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

function pagesSpaFallback(outDir: string): Plugin {
  return {
    name: 'pages-spa-fallback',
    closeBundle() {
      const abs = join(__dirname, outDir)
      copyFileSync(join(abs, 'index.html'), join(abs, '404.html'))
      writeFileSync(join(abs, '.nojekyll'), '')
    }
  }
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const isPages = mode === 'pages' || env.VITE_PAGES === '1' || process.env.VITE_PAGES === '1'
  const base = process.env.VITE_BASE || env.VITE_BASE || '/'
  const outDir = isPages ? 'dist' : '../server/wwwroot'

  return {
    plugins: [vue(), ...(isPages ? [pagesSpaFallback(outDir)] : [])],
    base,
    server: {
      port: 5173,
      proxy: {
        '/api': 'http://127.0.0.1:1010'
      }
    },
    preview: {
      port: 4173
    },
    build: {
      outDir,
      emptyOutDir: true
    }
  }
})
