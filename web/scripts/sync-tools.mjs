/**
 * 薄包装：转发到 shared/scripts/sync-tools.mjs（TOOLS_TARGET=ud）。
 */
import { spawnSync } from 'node:child_process'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const script = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'shared', 'scripts', 'sync-tools.mjs')
const result = spawnSync(process.execPath, [script, '--target=ud'], {
  cwd: join(dirname(fileURLToPath(import.meta.url)), '..'),
  stdio: 'inherit',
  env: { ...process.env, TOOLS_TARGET: 'ud' }
})
if (result.error) throw result.error
process.exit(result.status ?? 1)
