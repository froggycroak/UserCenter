import { spawnSync } from 'node:child_process'
import { copyFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

/** 开发时保存 shared/data/tools.xlsx 后写回 tools-data.ts / toolsdata-external.ts。启动同步在 npm scripts 里。 */
function syncToolsPlugin(): Plugin {
  const script = join(__dirname, '..', 'shared', 'scripts', 'sync-tools.mjs')
  const xlsx = join(__dirname, '..', 'shared', 'data', 'tools.xlsx')

  const run = () => {
    const result = spawnSync(process.execPath, [script, '--target=ud'], {
      cwd: __dirname,
      stdio: 'inherit',
      env: { ...process.env, SYNC_TOOLS_QUIET: '1', TOOLS_TARGET: 'ud' }
    })
    if (result.error) throw result.error
    if (result.status !== 0) throw new Error('同步 shared/data/tools.xlsx 失败')
  }

  return {
    name: 'sync-tools-xlsx',
    configureServer(server) {
      server.watcher.add(xlsx)
      let timer: ReturnType<typeof setTimeout> | undefined
      const schedule = (file: string) => {
        if (!file.replace(/\\/g, '/').endsWith('/shared/data/tools.xlsx')) return
        clearTimeout(timer)
        timer = setTimeout(() => {
          try {
            run()
          } catch (error) {
            server.config.logger.error(error instanceof Error ? error.message : String(error))
          }
        }, 400)
      }
      server.watcher.on('change', schedule)
      server.watcher.on('add', schedule)
    }
  }
}

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
    plugins: [vue(), syncToolsPlugin(), ...(isPages ? [pagesSpaFallback(outDir)] : [])],
    base,
    // 共用图标来自 shared/public；本目录仍保留 header-map 等专属静态资源（Vite 会合并 publicDir）
    // 注意：Vite 只支持单一 publicDir，图标通过 web/public/images/icon Junction 指向 shared。
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
