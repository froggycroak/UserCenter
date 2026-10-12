export type ToolItem = {
  abbr: string
  name: string
  description?: string
  url: string
  icon: string
  iconLg?: boolean
}

export type ToolSection = {
  id: string
  eyebrow?: string
  title: string
  type: 'tools'
  tools: ToolItem[]
  /** 外部资源板块，不计入内部产品支持标签。 */
  external?: boolean
}

/** 内网工具主机；仅本机 `.env.local` / 构建环境注入，勿写进仓库。 */
const toolkitHost = (import.meta.env.VITE_TOOLKIT_HOST || '').trim().replace(/\/$/, '')
const staticDemo = import.meta.env.VITE_STATIC_DEMO === '1'

export function assetUrl(path: string): string {
  const base = import.meta.env.BASE_URL || '/'
  const rel = path.replace(/^\//, '')
  return (base.endsWith('/') ? base : base + '/') + rel
}

/** 静态演示或不设主机时返回 `#`，避免公网暴露内网拓扑。 */
export function intranetUrl(port: number, path = '/'): string {
  if (staticDemo || !toolkitHost) return '#'
  const p = path.startsWith('/') ? path : '/' + path
  return `http://${toolkitHost}:${port}${p}`
}

/** 由 shared/data/tools.xlsx（内部-ud）生成，npm run dev / build 时自动同步。请改表格，不要手改这个数组。 */
export const toolSections: ToolSection[] = [
  {
    id: 'urban',
    eyebrow: 'URBAN RESEARCH',
    title: '城市研究',
    type: 'tools',
    tools: [
      {
        abbr: 'StocKase',
        name: '存量楼宇活化案例库',
        description: '楼宇资产活化更新的智囊团，以案例数据驱动策划评估。',
        url: intranetUrl(5174),
        icon: assetUrl('images/icon/026-offices.svg'),
      },
      {
        abbr: 'UrbanCheck',
        name: '城市区位分析体检',
        description: '根据城市区位和公服配套设施，分析城市区域的发展潜力和问题。',
        url: intranetUrl(5180),
        icon: assetUrl('images/icon/map (1).svg'),
      },
      {
        abbr: 'RegionShot',
        name: '场地模型快速生成系统',
        description: '从网络获取数据，快速生成建筑设计前期的场地模型。',
        url: intranetUrl(5190),
        icon: assetUrl('images/icon/Urban Planning.svg'),
      },
      {
        abbr: 'PotentialStock',
        name: '存量楼宇项目挖掘看板',
        description: '上海市重点区域潜在存量楼宇改造项目的信息看板。',
        url: intranetUrl(5182),
        icon: assetUrl('images/icon/楼宇.svg'),
      },
      {
        abbr: 'StockAgent',
        name: '楼宇资产活化智能体',
        url: intranetUrl(5110),
        icon: assetUrl('images/icon/chat (1).svg'),
      }
    ]
  },
  {
    id: 'architecture',
    eyebrow: 'ARCHITECTURAL INTELLIGENCE',
    title: '建筑智能',
    type: 'tools',
    tools: [
      {
        abbr: 'ProtoMass',
        name: '建筑原型智能设计',
        description: '面向原创设计业务场景的建筑体量原型智能演绎工具。',
        url: intranetUrl(7251),
        icon: assetUrl('images/icon/logo_proto.jpg'),
      },
      {
        abbr: 'OvalAgent',
        name: '体育建筑智能设计助手',
        description: '体育场馆设计的宝藏工具，体育建筑设计师的得力助手。',
        url: 'https://aiovaltool.com/',
        icon: assetUrl('images/icon/stadium.svg'),
      },
      {
        abbr: 'HopeStation',
        name: '研究型医院数据库',
        description: '国内外研究型医院的图片和数据集成，为尖端医疗建筑设计提供参考。',
        url: 'http://medical.sstddev.com/',
        icon: assetUrl('images/icon/医院.svg'),
      }
    ]
  },
  {
    id: 'internal',
    eyebrow: 'INTERNAL SERVICE',
    title: '通用服务',
    type: 'tools',
    tools: [
      {
        abbr: 'CookBook',
        name: '在线知识库',
        description: '内部在线知识库。',
        url: intranetUrl(2010),
        icon: assetUrl('images/icon/cookbook.svg'),
      }
    ]
  }
]
