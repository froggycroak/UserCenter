/**
 * 读取 shared/data/tools.xlsx，按 TOOLS_TARGET 写回对应前端的
 * tools-data.ts / toolsdata-external.ts。
 *
 * 三表结构：
 *   - 「内部-ud」  → web/src/tools-data.ts
 *   - 「内部-sstd」→ web_sstd/src/tools-data.ts
 *   - 「外部」    → 两应用的 toolsdata-external.ts（内容一致）
 *
 * 兼容旧版：「分组」+「工具」，或「内部工具」+「外部资源」（仅 TOOLS_TARGET=ud）。
 *
 * url：http(s) 或 # 为字面量；纯端口如 5174 或 5174/docs 表示内网 intranetUrl。
 *
 * 用法：
 *   TOOLS_TARGET=ud   node shared/scripts/sync-tools.mjs
 *   TOOLS_TARGET=sstd node shared/scripts/sync-tools.mjs
 *   node shared/scripts/sync-tools.mjs --target=sstd
 */
import { existsSync, readFileSync, realpathSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { setTimeout as delay } from 'node:timers/promises'

const scriptDir = dirname(fileURLToPath(import.meta.url))
const repoRoot = join(scriptDir, '..', '..')
const sharedRoot = join(scriptDir, '..')
const xlsxPath = join(sharedRoot, 'data', 'tools.xlsx')
const iconRoot = join(sharedRoot, 'public')

const TARGETS = {
  ud: {
    appRoot: join(repoRoot, 'web'),
    internalSheet: '内部-ud',
    label: 'ud'
  },
  sstd: {
    appRoot: join(repoRoot, 'web_sstd'),
    internalSheet: '内部-sstd',
    label: 'sstd'
  }
}

const SHEET_EXTERNAL = '外部'
const SHEET_GROUPS = '分组'
const SHEET_TOOLS = '工具'
const SHEET_INTERNAL_LEGACY = '内部工具'
const SHEET_EXTERNAL_LEGACY = '外部资源'

const HEADER_MAP = {
  section_id: ['section_id', '分组id', '章节id', 'section id', '板块id'],
  section_eyebrow: ['section_eyebrow', '英文小标题', 'eyebrow', '眉标'],
  section_title: ['section_title', '分组标题', '章节标题', 'title', '板块标题'],
  category: ['category', '分类', '类型', '归属'],
  abbr: ['abbr', '缩写', '简称'],
  name: ['name', '名称', '中文名'],
  description: ['description', '描述', '说明', '简介'],
  url: ['url', '链接', '地址'],
  icon: ['icon', '图标', '图标路径'],
  iconLg: ['iconlg', '大图标', 'icon_lg'],
  linkType: ['linktype', '链接类型'],
  port: ['port', '端口']
}

const LEGACY_HEADERS = {
  sectionId: '板块ID',
  eyebrow: '眉标',
  title: '板块标题',
  abbr: '简称',
  name: '名称',
  description: '简介',
  linkType: '链接类型',
  url: '链接',
  port: '端口',
  icon: '图标',
  iconLg: '大图标'
}

class ToolsSyncError extends Error {
  constructor(message) {
    super(message)
    this.name = 'ToolsSyncError'
  }
}

function resolveTarget() {
  const arg = process.argv.find((a) => a.startsWith('--target='))
  const raw = (arg ? arg.slice('--target='.length) : process.env.TOOLS_TARGET || 'ud')
    .trim()
    .toLowerCase()
  if (!TARGETS[raw]) {
    throw new ToolsSyncError(`TOOLS_TARGET 须为 ud 或 sstd，收到「${raw}」`)
  }
  return TARGETS[raw]
}

function normalizeHeader(h) {
  return String(h ?? '')
    .trim()
    .toLowerCase()
    .replace(/[\s_-]+/g, '')
}

function cellText(value) {
  if (value == null) return ''
  if (typeof value === 'number') return Number.isInteger(value) ? String(value) : String(value)
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (value instanceof Date) return value.toISOString()
  if (typeof value === 'string') return value.trim()
  if (typeof value === 'object') {
    if (Array.isArray(value.richText)) return value.richText.map((part) => part.text ?? '').join('').trim()
    if (typeof value.text === 'string') return value.text.trim()
    if (value.result != null) return cellText(value.result)
    if (typeof value.hyperlink === 'string') return value.hyperlink.trim()
  }
  return String(value).trim()
}

function buildColumnIndex(headerRow) {
  const map = {}
  const normalized = headerRow.map(normalizeHeader)
  for (const [field, aliases] of Object.entries(HEADER_MAP)) {
    for (const alias of aliases) {
      const idx = normalized.indexOf(normalizeHeader(alias))
      if (idx !== -1) {
        map[field] = idx
        break
      }
    }
  }
  return map
}

function cell(row, colIdx) {
  if (colIdx == null) return ''
  const v = row[colIdx]
  return v == null ? '' : String(v).trim()
}

function toBool(v) {
  const s = String(v ?? '').trim().toLowerCase()
  return s === 'true' || s === '1' || s === 'yes' || s === 'y' || s === '是'
}

function tsString(value) {
  return `'${String(value)
    .replace(/\\/g, '\\\\')
    .replace(/'/g, "\\'")
    .replace(/\r\n/g, '\\n')
    .replace(/\n/g, '\\n')
    .replace(/\r/g, '\\n')}'`
}

function isExternalCategory(cat) {
  const s = String(cat ?? '').trim().toLowerCase()
  return s.includes('外部') || s.includes('external') || s === 'ext'
}

function worksheetToRows(worksheet) {
  const rows = []
  worksheet.eachRow({ includeEmpty: false }, (row) => {
    const vals = []
    row.eachCell({ includeEmpty: true }, (cell, col) => {
      while (vals.length < col - 1) vals.push('')
      vals[col - 1] = cellText(cell.value)
    })
    rows.push(vals)
  })
  return rows
}

function normalizeIcon(raw, ctx) {
  let icon = raw.trim().replace(/\\/g, '/')
  if (!icon) throw new ToolsSyncError(`${ctx}缺少 icon`)
  icon = icon.replace(/^\/+/, '')
  if (icon.startsWith('public/')) icon = icon.slice('public/'.length)
  if (icon.includes('..')) throw new ToolsSyncError(`${ctx}图标路径不合法`)
  return icon
}

function intranetExpr(port, rawPath, ctx) {
  let path = rawPath.trim()
  if (!path) return `intranetUrl(${port})`
  if (/^https?:\/\//i.test(path)) {
    throw new ToolsSyncError(`${ctx}内网 url 请用「5174」或「5174/docs」，不要写完整 http`)
  }
  if (!path.startsWith('/')) path = `/${path}`
  if (path === '/') return `intranetUrl(${port})`
  return `intranetUrl(${port}, ${tsString(path)})`
}

/** 将写死内网 IP 的 http URL 规范为 intranetUrl（可选）。 */
function tryNormalizeIntranetHttp(url) {
  const m = url.match(/^https?:\/\/[\w.-]+:(\d{1,5})(\/.*)?$/i)
  if (!m) return null
  const port = Number(m[1])
  if (port < 1 || port > 65535) return null
  const pathPart = m[2] && m[2] !== '/' ? m[2] : ''
  return {
    urlExpr: intranetExpr(port, pathPart.replace(/^\//, ''), 'normalize'),
    needsIntranet: true,
    isIntranet: true
  }
}

function parseUrlField(rawUrl, legacy, ctx) {
  if (legacy?.linkType) {
    const kind = legacy.linkType.trim().toLowerCase()
    if (legacy.linkType === '内网' || kind === 'intranet') {
      if (!legacy.port) throw new ToolsSyncError(`${ctx}内网需要端口`)
      const port = Number(legacy.port)
      return { urlExpr: intranetExpr(port, legacy.url, ctx), needsIntranet: true, isIntranet: true }
    }
    if (legacy.linkType === '外链' || kind === 'url') {
      const url = legacy.url.trim()
      if (url === '#') return { urlExpr: tsString('#'), needsIntranet: false, isIntranet: false }
      if (!/^https?:\/\//i.test(url)) throw new ToolsSyncError(`${ctx}外链须以 http:// 或 https:// 开头`)
      const normalized = tryNormalizeIntranetHttp(url)
      if (normalized) return normalized
      return { urlExpr: tsString(url), needsIntranet: false, isIntranet: false }
    }
    throw new ToolsSyncError(`${ctx}链接类型请填「内网」或「外链」`)
  }

  const url = rawUrl.trim()
  if (!url) throw new ToolsSyncError(`${ctx}缺少 url`)

  if (url === '#') return { urlExpr: tsString('#'), needsIntranet: false, isIntranet: false }

  if (/^https?:\/\//i.test(url)) {
    const normalized = tryNormalizeIntranetHttp(url)
    if (normalized) return normalized
    return { urlExpr: tsString(url), needsIntranet: false, isIntranet: false }
  }

  const intranetMatch = url.match(/^(\d{1,5})(?:[/:](.+))?$/)
  if (intranetMatch) {
    const port = Number(intranetMatch[1])
    if (port < 1 || port > 65535) throw new ToolsSyncError(`${ctx}端口须在 1–65535`)
    const pathPart = intranetMatch[2] ? intranetMatch[2].replace(/^\/+/, '') : ''
    return {
      urlExpr: intranetExpr(port, pathPart, ctx),
      needsIntranet: true,
      isIntranet: true
    }
  }

  throw new ToolsSyncError(
    `${ctx}无法识别 url「${url}」：请填 http(s) 链接、#、或内网端口（如 5174、5174/docs）`
  )
}

function parseGroupSheet(rows) {
  if (rows.length === 0) return null
  const colIdx = buildColumnIndex(rows[0])
  if (colIdx.section_title == null) return null

  const map = new Map()
  const order = []
  for (let i = 1; i < rows.length; i++) {
    const row = rows[i]
    const title = cell(row, colIdx.section_title)
    if (!title) continue
    if (map.has(title)) continue
    map.set(title, {
      eyebrow: cell(row, colIdx.section_eyebrow),
      category: cell(row, colIdx.category),
      sectionId: cell(row, colIdx.section_id)
    })
    order.push(title)
  }
  return { map, order }
}

function parseToolsWithGroups(toolRows, groupInfo) {
  const { map: groupMap, order: groupOrder } = groupInfo
  if (toolRows.length === 0) return { internal: [], external: [], needsIntranet: false }

  const colIdx = buildColumnIndex(toolRows[0])
  for (const r of ['section_title', 'abbr', 'name', 'url', 'icon']) {
    if (colIdx[r] == null) throw new ToolsSyncError(`「${SHEET_TOOLS}」缺少列 ${r}`)
  }

  const toolsBySection = new Map()
  const sectionSeen = []
  let needsIntranet = false

  for (let i = 1; i < toolRows.length; i++) {
    const row = toolRows[i]
    const title = cell(row, colIdx.section_title)
    if (!title) continue
    const ctx = `「${SHEET_TOOLS}」第 ${i + 1} 行：`

    if (!toolsBySection.has(title)) {
      toolsBySection.set(title, [])
      sectionSeen.push(title)
    }

    const icon = normalizeIcon(cell(row, colIdx.icon), ctx)
    const iconFile = join(iconRoot, icon)
    if (!existsSync(iconFile)) console.warn(`[sync-tools] 警告：${ctx}找不到图标 shared/public/${icon}`)

    const legacy =
      colIdx.linkType != null || colIdx.port != null
        ? { linkType: cell(row, colIdx.linkType), url: cell(row, colIdx.url), port: cell(row, colIdx.port) }
        : null
    const { urlExpr, needsIntranet: ni } = parseUrlField(cell(row, colIdx.url), legacy, ctx)
    if (ni) needsIntranet = true

    toolsBySection.get(title).push({
      abbr: cell(row, colIdx.abbr),
      name: cell(row, colIdx.name),
      description: cell(row, colIdx.description),
      urlExpr,
      icon,
      iconLg: colIdx.iconLg != null ? toBool(row[colIdx.iconLg]) : false
    })
  }

  const orderedTitles = [...groupOrder]
  for (const t of sectionSeen) {
    if (!groupMap.has(t)) orderedTitles.push(t)
  }

  const internal = []
  const external = []
  for (const title of orderedTitles) {
    const tools = toolsBySection.get(title) || []
    if (tools.length === 0) continue
    const meta = groupMap.get(title) || { eyebrow: '', category: '', sectionId: '' }
    const section = {
      id: meta.sectionId || title,
      eyebrow: meta.eyebrow,
      title,
      external: isExternalCategory(meta.category),
      tools
    }
    if (section.external) external.push(section)
    else internal.push(section)
  }

  return { internal, external, needsIntranet }
}

function legacyHeaderColumns(worksheet) {
  const map = new Map()
  worksheet.getRow(1).eachCell({ includeEmpty: false }, (cell, col) => {
    const name = cellText(cell.value)
    if (!name) return
    map.set(name, col)
  })
  for (const name of Object.values(LEGACY_HEADERS)) {
    if (!map.has(name)) throw new ToolsSyncError(`表「${worksheet.name}」缺少表头「${name}」`)
  }
  return map
}

function readLegacyRows(worksheet) {
  const columns = legacyHeaderColumns(worksheet)
  const rows = []
  for (let line = 2; line <= worksheet.rowCount; line++) {
    const excelRow = worksheet.getRow(line)
    const row = { line }
    let any = false
    for (const [key, name] of Object.entries(LEGACY_HEADERS)) {
      const text = cellText(excelRow.getCell(columns.get(name)).value)
      row[key] = text
      if (text) any = true
    }
    if (any) rows.push(row)
  }
  return rows
}

function buildLegacySections(rows, { external, sheetName }) {
  const sections = []
  const byId = new Map()
  let carry = null
  let needsIntranet = false

  for (const row of rows) {
    const ctx = `「${sheetName}」第 ${row.line} 行：`
    if (!row.sectionId && !row.eyebrow && !row.title) {
      if (!carry) throw new ToolsSyncError(`${ctx}缺少板块信息`)
    } else {
      if (!row.sectionId) throw new ToolsSyncError(`${ctx}缺少「板块ID」`)
      if (!byId.has(row.sectionId)) {
        carry = {
          id: row.sectionId,
          eyebrow: row.eyebrow,
          title: row.title,
          external,
          tools: []
        }
        byId.set(row.sectionId, carry)
        sections.push(carry)
      } else {
        carry = byId.get(row.sectionId)
      }
    }

    const icon = normalizeIcon(row.icon, ctx)
    const iconFile = join(iconRoot, icon)
    if (!existsSync(iconFile)) console.warn(`[sync-tools] 警告：${ctx}找不到图标 shared/public/${icon}`)

    const legacy = { linkType: row.linkType, url: row.url, port: row.port }
    const { urlExpr, needsIntranet: ni } = parseUrlField(row.url, legacy, ctx)
    if (ni) needsIntranet = true

    carry.tools.push({
      abbr: row.abbr,
      name: row.name,
      description: row.description,
      urlExpr,
      icon,
      iconLg: toBool(row.iconLg)
    })
  }

  return { sections, needsIntranet }
}

function emitTool(tool) {
  const lines = [
    '      {',
    `        abbr: ${tsString(tool.abbr)},`,
    `        name: ${tsString(tool.name)},`
  ]
  if (tool.description) lines.push(`        description: ${tsString(tool.description)},`)
  lines.push(`        url: ${tool.urlExpr},`, `        icon: assetUrl(${tsString(tool.icon)}),`)
  if (tool.iconLg) lines.push('        iconLg: true,')
  lines.push('      }')
  return lines.join('\n')
}

function emitSection(section) {
  const lines = ['  {', `    id: ${tsString(section.id)},`]
  if (section.eyebrow) lines.push(`    eyebrow: ${tsString(section.eyebrow)},`)
  lines.push(`    title: ${tsString(section.title)},`, `    type: 'tools',`)
  if (section.external) lines.push('    external: true,')
  lines.push('    tools: [', section.tools.map(emitTool).join(',\n'), '    ]', '  }')
  return lines.join('\n')
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

function renderArray(sections, newline) {
  const literal = sections.length ? `[\n${sections.map(emitSection).join(',\n')}\n]` : '[]'
  return newline === '\n' ? literal : literal.replace(/\n/g, newline)
}

function replaceArrayExport(source, exportName, sections) {
  const marker = `export const ${exportName}: ToolSection[] = `
  const start = source.indexOf(marker)
  if (start < 0) throw new ToolsSyncError(`${exportName} 不在目标文件中，无法写回`)
  const bracket = start + marker.length
  if (source[bracket] !== '[') throw new ToolsSyncError(`${exportName} 的初始值不是数组`)
  const end = findArrayEnd(source, bracket)
  if (end < 0) throw new ToolsSyncError(`${exportName} 的数组没有结束`)
  const newline = source.includes('\r\n') ? '\r\n' : '\n'
  return source.slice(0, bracket) + renderArray(sections, newline) + source.slice(end + 1)
}

function syncExternalImport(source, needsIntranet) {
  const pattern = /^import \{[^}]*\} from '\.\/tools-data'/m
  if (!pattern.test(source)) throw new ToolsSyncError('toolsdata-external.ts 中找不到 tools-data 的导入')
  const line = needsIntranet
    ? "import { assetUrl, intranetUrl } from './tools-data'"
    : "import { assetUrl } from './tools-data'"
  return source.replace(pattern, line)
}

function writeIfChanged(file, content) {
  const previous = existsSync(file) ? readFileSync(file, 'utf8') : null
  if (previous === content) return false
  writeFileSync(file, content, 'utf8')
  return true
}

async function loadWorkbook(ExcelJS) {
  if (!existsSync(xlsxPath)) throw new ToolsSyncError(`找不到表格：${xlsxPath}`)
  let last
  for (let attempt = 0; attempt < 8; attempt++) {
    try {
      const workbook = new ExcelJS.Workbook()
      await workbook.xlsx.readFile(xlsxPath)
      return workbook
    } catch (error) {
      last = error
      await delay(200)
    }
  }
  const detail = last instanceof Error ? last.message : String(last)
  throw new ToolsSyncError(`无法读取表格（可能正被 Excel 占用）：${detail}`)
}

function loadExcelJS(imported) {
  const candidate = imported?.Workbook ? imported : imported?.default
  if (!candidate?.Workbook) throw new ToolsSyncError('exceljs 加载失败。请运行 web\\ensure_node_modules.bat')
  return candidate
}

function countTools(sections) {
  return sections.reduce((n, s) => n + s.tools.length, 0)
}

function resolveExceljsFromWeb() {
  const require = createRequire(join(repoRoot, 'web', 'package.json'))
  try {
    return require('exceljs')
  } catch {
    return null
  }
}

/**
 * 优先读三表；否则 fallback 旧版（仅 ud）。
 */
function parseWorkbook(workbook, target) {
  const internalWs = workbook.getWorksheet(target.internalSheet)
  const externalWs = workbook.getWorksheet(SHEET_EXTERNAL)

  if (internalWs && externalWs) {
    const internal = buildLegacySections(readLegacyRows(internalWs), {
      external: false,
      sheetName: target.internalSheet
    })
    const external = buildLegacySections(readLegacyRows(externalWs), {
      external: true,
      sheetName: SHEET_EXTERNAL
    })
    return {
      mode: 'three-sheet',
      internal: internal.sections,
      external: external.sections,
      needsIntranetExternal: external.needsIntranet
    }
  }

  // 旧版「分组」+「工具」（仅 ud）
  if (target.label === 'ud') {
    const groupWs = workbook.getWorksheet(SHEET_GROUPS)
    const toolsWs = workbook.getWorksheet(SHEET_TOOLS)
    const groupInfo = groupWs ? parseGroupSheet(worksheetToRows(groupWs)) : null
    if (groupInfo && toolsWs) {
      const result = parseToolsWithGroups(worksheetToRows(toolsWs), groupInfo)
      return {
        mode: 'groups-tools',
        internal: result.internal,
        external: result.external,
        needsIntranetExternal: result.needsIntranet
      }
    }

    const legacyInternal = workbook.getWorksheet(SHEET_INTERNAL_LEGACY)
    const legacyExternal = workbook.getWorksheet(SHEET_EXTERNAL_LEGACY)
    if (legacyInternal && legacyExternal) {
      const internal = buildLegacySections(readLegacyRows(legacyInternal), {
        external: false,
        sheetName: SHEET_INTERNAL_LEGACY
      })
      const external = buildLegacySections(readLegacyRows(legacyExternal), {
        external: true,
        sheetName: SHEET_EXTERNAL_LEGACY
      })
      return {
        mode: 'legacy',
        internal: internal.sections,
        external: external.sections,
        needsIntranetExternal: external.needsIntranet
      }
    }
  }

  throw new ToolsSyncError(
    `需要「${target.internalSheet}」+「${SHEET_EXTERNAL}」` +
      (target.label === 'ud' ? '，或旧版「分组」+「工具」/「内部工具」+「外部资源」' : '')
  )
}

export async function syncToolsFromXlsx(options = {}) {
  const target = options.target
    ? TARGETS[String(options.target).toLowerCase()] || resolveTarget()
    : resolveTarget()
  if (!target) throw new ToolsSyncError('无效的 TOOLS_TARGET')

  const internalOut = join(target.appRoot, 'src', 'tools-data.ts')
  const externalOut = join(target.appRoot, 'src', 'toolsdata-external.ts')
  if (!existsSync(internalOut) || !existsSync(externalOut)) {
    throw new ToolsSyncError(`目标前端缺少源文件：${internalOut} 或 ${externalOut}`)
  }

  let imported = resolveExceljsFromWeb()
  if (!imported) {
    try {
      imported = await import('exceljs')
    } catch {
      throw new ToolsSyncError('缺少依赖 exceljs。请运行 web\\ensure_node_modules.bat')
    }
  }
  const ExcelJS = loadExcelJS(imported)
  const workbook = await loadWorkbook(ExcelJS)
  const { mode, internal, external, needsIntranetExternal } = parseWorkbook(workbook, target)

  const changed = []
  let source = readFileSync(internalOut, 'utf8')
  source = replaceArrayExport(source, 'toolSections', internal)
  if (writeIfChanged(internalOut, source)) changed.push(`${target.label}/tools-data.ts`)

  source = readFileSync(externalOut, 'utf8')
  source = syncExternalImport(source, needsIntranetExternal)
  source = replaceArrayExport(source, 'externalToolSections', external)
  if (writeIfChanged(externalOut, source)) changed.push(`${target.label}/toolsdata-external.ts`)

  const summary = `内部 ${internal.length} 组/${countTools(internal)} 项；外部 ${external.length} 组/${countTools(external)} 项（${mode}）`
  const quiet = process.env.SYNC_TOOLS_QUIET === '1'
  if (changed.length) console.log(`[sync-tools:${target.label}] 已更新 ${changed.join('、')}（${summary}）`)
  else if (!quiet) console.log(`[sync-tools:${target.label}] TypeScript 已与 shared/data/tools.xlsx 一致（${summary}）`)
  return changed
}

function isDirectRun() {
  const entry = process.argv[1]
  if (!entry) return false
  try {
    return realpathSync(entry) === realpathSync(fileURLToPath(import.meta.url))
  } catch {
    return false
  }
}

if (isDirectRun()) {
  try {
    await syncToolsFromXlsx()
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    console.error(`[sync-tools] ${message}`)
    if (error instanceof Error && error.name !== 'ToolsSyncError' && error.stack) console.error(error.stack)
    process.exitCode = 1
  }
}
