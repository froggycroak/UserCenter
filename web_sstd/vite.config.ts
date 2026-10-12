import { spawnSync } from 'node:child_process'
import { copyFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

/** 开发时保存 shared/data/tools.xlsx 后写回 tools-data.ts / toolsdata-external.ts。 */
function syncToolsPlugin(): Plugin {
  const script = join(__dirname, '..', 'shared', 'scripts', 'sync-tools.mjs')
  const xlsx = join(__dirname, '..', 'shared', 'data', 'tools.xlsx')

  const run = () => {
    const result = spawnSync(process.execPath, [script, '--target=sstd'], {
      cwd: __dirname,
      stdio: 'inherit',
      env: { ...process.env, SYNC_TOOLS_QUIET: '1', TOOLS_TARGET: 'sstd' }
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

function spaFallback(outDir: string): Plugin {
  return {
    name: 'spa-fallback-404',
    closeBundle() {
      const abs = join(__dirname, outDir)
      copyFileSync(join(abs, 'index.html'), join(abs, '404.html'))
      writeFileSync(join(abs, '.nojekyll'), '')
    }
  }
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const base = process.env.VITE_BASE || env.VITE_BASE || '/'
  const apiTarget =
    process.env.VITE_DEV_API_TARGET || env.VITE_DEV_API_TARGET || 'http://127.0.0.1:1010'

  return {
    plugins: [vue(), syncToolsPlugin(), spaFallback('dist')],
    base,
    publicDir: join(__dirname, '..', 'shared', 'public'),
    server: {
      port: 5174,
      open: true,
      proxy: {
        '/api': apiTarget
      }
    },
    preview: {
      port: 4174
    },
    build: {
      outDir: 'dist',
      emptyOutDir: true
    }
  }
})
