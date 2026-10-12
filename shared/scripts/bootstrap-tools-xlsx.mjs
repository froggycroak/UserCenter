/**
 * 从两前端的 tools-data.ts / toolsdata-external.ts 生成 shared/data/tools.xlsx（三表）。
 *
 *   内部-ud   ← web/src/tools-data.ts
 *   内部-sstd ← web_sstd/src/tools-data.ts（若不存在则跳过该表）
 *   外部      ← web/src/toolsdata-external.ts
 *
 * 用法：node shared/scripts/bootstrap-tools-xlsx.mjs [--force]
 */
import { existsSync, mkdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'

const scriptDir = dirname(fileURLToPath(import.meta.url))
const repoRoot = join(scriptDir, '..', '..')
const xlsxPath = join(scriptDir, '..', 'data', 'tools.xlsx')

const LEGACY_HEADERS = [
  '板块ID',
  '眉标',
  '板块标题',
  '简称',
  '名称',
  '简介',
  '链接类型',
  '链接',
  '端口',
  '图标',
  '大图标'
]

function unescapeTsString(raw) {
  return raw.replace(/\\'/g, "'").replace(/\\n/g, '\n')
}

function readQuoted(source, key) {
  const re = new RegExp(`${key}:\\s*'((?:\\\\'|[^'])*)'`)
  const match = source.match(re)
  return match ? unescapeTsString(match[1]) : ''
}

function readBool(source, key) {
  return new RegExp(`${key}:\\s*true`).test(source)
}

function urlToLegacy(source) {
  const intranet = source.match(/url:\s*intranetUrl\((\d+)(?:,\s*'((?:\\'|[^'])*)')?\)/)
  if (intranet) {
    const port = intranet[1]
    const path = intranet[2] ? unescapeTsString(intranet[2]).replace(/^\/+/, '') : ''
    return { linkType: '内网', url: path, port }
  }
  const external = source.match(/url:\s*'((?:\\'|[^'])*)'/)
  if (external) {
    const url = unescapeTsString(external[1])
    // 写死内网 IP 的 http → 拆成端口
    const m = url.match(/^https?:\/\/[\w.-]+:(\d{1,5})(\/.*)?$/i)
    if (m) {
      const path = m[2] && m[2] !== '/' ? m[2].replace(/^\//, '') : ''
      return { linkType: '内网', url: path, port: m[1] }
    }
    return { linkType: '外链', url, port: '' }
  }
  throw new Error(`无法解析 url：${source.slice(0, 80)}`)
}

function parseIcon(source) {
  const match = source.match(/icon:\s*assetUrl\('((?:\\'|[^'])*)'\)/)
  if (!match) throw new Error('无法解析 icon')
  return unescapeTsString(match[1])
}

function findArrayEnd(source, openIndex) {
  let depth = 0
  let quote = ''
  for (let i = openIndex; i < source.length; i++) {
    const ch = source[i]
    if (quote) {
      if (ch === '\\') {
        i++
        continue
      }
      if (ch === quote) quote = ''
      continue
    }
    if (ch === "'" || ch === '"' || ch === '`') {
      quote = ch
      continue
    }
    if (ch === '/' && source[i + 1] === '/') {
      while (i < source.length && source[i] !== '\n') i++
      continue
    }
    if (ch === '[') depth++
    else if (ch === ']') {
      depth--
      if (depth === 0) return i
    }
  }
  return -1
}

function extractArrayBody(source, exportName) {
  const marker = `export const ${exportName}: ToolSection[] = `
  const start = source.indexOf(marker)
  if (start < 0) throw new Error(`${exportName} 未找到`)
  const open = start + marker.length
  if (source[open] !== '[') throw new Error(`${exportName} 不是数组`)
  const close = findArrayEnd(source, open)
  if (close < 0) throw new Error(`${exportName} 数组未闭合`)
  return source.slice(open + 1, close)
}

function splitTopLevelObjects(body) {
  const chunks = []
  let depth = 0
  let quote = ''
  let start = -1
  for (let i = 0; i < body.length; i++) {
    const ch = body[i]
    if (quote) {
      if (ch === '\\') {
        i++
        continue
      }
      if (ch === quote) quote = ''
      continue
    }
    if (ch === "'" || ch === '"' || ch === '`') {
      quote = ch
      continue
    }
    if (ch === '/' && body[i + 1] === '/') {
      while (i < body.length && body[i] !== '\n') i++
      continue
    }
    if (ch === '{') {
      if (depth === 0) start = i
      depth++
    } else if (ch === '}') {
      depth--
      if (depth === 0 && start >= 0) {
        chunks.push(body.slice(start, i + 1))
        start = -1
      }
    }
  }
  return chunks
}

function extractToolsArray(sectionSource) {
  const idx = sectionSource.indexOf('tools:')
  if (idx < 0) throw new Error('section 缺少 tools')
  const open = sectionSource.indexOf('[', idx)
  const close = findArrayEnd(sectionSource, open)
  return splitTopLevelObjects(sectionSource.slice(open + 1, close))
}

function parseSections(file, exportName) {
  const source = readFileSync(file, 'utf8')
  const body = extractArrayBody(source, exportName)
  const rows = []
  for (const sectionSource of splitTopLevelObjects(body)) {
    const id = readQuoted(sectionSource, 'id')
    const title = readQuoted(sectionSource, 'title')
    const eyebrow = readQuoted(sectionSource, 'eyebrow')
    const tools = extractToolsArray(sectionSource)
    let first = true
    for (const toolSource of tools) {
      const { linkType, url, port } = urlToLegacy(toolSource)
      rows.push({
        sectionId: first ? id : '',
        eyebrow: first ? eyebrow : '',
        title: first ? title : '',
        abbr: readQuoted(toolSource, 'abbr'),
        name: readQuoted(toolSource, 'name'),
        description: readQuoted(toolSource, 'description'),
        linkType,
        url,
        port,
        icon: parseIcon(toolSource),
        iconLg: readBool(toolSource, 'iconLg') ? 'true' : ''
      })
      first = false
    }
  }
  return rows
}

function addLegacySheet(workbook, name, rows) {
  const ws = workbook.addWorksheet(name)
  ws.addRow(LEGACY_HEADERS)
  ws.getRow(1).font = { bold: true }
  for (const r of rows) {
    ws.addRow([
      r.sectionId,
      r.eyebrow,
      r.title,
      r.abbr,
      r.name,
      r.description,
      r.linkType,
      r.url,
      r.port,
      r.icon,
      r.iconLg
    ])
  }
  ws.views = [{ state: 'frozen', ySplit: 1 }]
  return rows.length
}

async function main() {
  const force = process.argv.includes('--force')
  if (existsSync(xlsxPath) && !force) {
    console.log(`[bootstrap-tools-xlsx] 已存在 ${xlsxPath}，跳过（覆盖请加 --force）`)
    return
  }

  const require = createRequire(join(repoRoot, 'web', 'package.json'))
  let ExcelJS
  try {
    ExcelJS = require('exceljs')
  } catch {
    console.error('[bootstrap-tools-xlsx] 缺少 exceljs，请先运行 web\\ensure_node_modules.bat')
    process.exitCode = 1
    return
  }
  ExcelJS = ExcelJS.Workbook ? ExcelJS : ExcelJS.default

  const udInternal = join(repoRoot, 'web', 'src', 'tools-data.ts')
  const udExternal = join(repoRoot, 'web', 'src', 'toolsdata-external.ts')
  const sstdInternal = join(repoRoot, 'web_sstd', 'src', 'tools-data.ts')

  if (!existsSync(udInternal) || !existsSync(udExternal)) {
    console.error('[bootstrap-tools-xlsx] 缺少 web/src/tools-data.ts 或 toolsdata-external.ts')
    process.exitCode = 1
    return
  }

  mkdirSync(dirname(xlsxPath), { recursive: true })
  const workbook = new ExcelJS.Workbook()

  const help = workbook.addWorksheet('说明')
  help.addRow(['shared/data/tools.xlsx — 两前端共用'])
  help.addRow(['「内部-ud」→ web（User Center :1010）'])
  help.addRow(['「内部-sstd」→ web_sstd（Research Portal :1088）'])
  help.addRow(['「外部」→ 两应用共用外部资源'])
  help.addRow(['链接类型：内网（填端口列）或 外链（填完整 http(s)）；占位填 # 且链接类型=外链'])

  const udRows = parseSections(udInternal, 'toolSections')
  const nUd = addLegacySheet(workbook, '内部-ud', udRows)

  let nSstd = 0
  if (existsSync(sstdInternal)) {
    const sstdRows = parseSections(sstdInternal, 'toolSections')
    nSstd = addLegacySheet(workbook, '内部-sstd', sstdRows)
  } else {
    addLegacySheet(workbook, '内部-sstd', [])
    console.warn('[bootstrap-tools-xlsx] web_sstd 尚未迁入，内部-sstd 为空表')
  }

  const extRows = parseSections(udExternal, 'externalToolSections')
  const nExt = addLegacySheet(workbook, '外部', extRows)

  await workbook.xlsx.writeFile(xlsxPath)
  console.log(
    `[bootstrap-tools-xlsx] 已写入 ${xlsxPath}（内部-ud ${nUd} 行；内部-sstd ${nSstd} 行；外部 ${nExt} 行）`
  )
}

main().catch((error) => {
  console.error(`[bootstrap-tools-xlsx] ${error instanceof Error ? error.message : error}`)
  process.exitCode = 1
})
