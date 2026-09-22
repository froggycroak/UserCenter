export type ToolItem = {
  abbr: string
  name: string
  description?: string
  url: string
  icon: string
  iconLg?: boolean
  status: 'active' | 'warning' | 'pending'
  statusText: string
}

export type ToolSection = {
  id: string
  eyebrow?: string
  title: string
  type: 'tools'
  tools: ToolItem[]
}

/** 内网工具主机；仅本机 `.env.local` / 构建环境注入，勿写进仓库。 */
const toolkitHost = (import.meta.env.VITE_TOOLKIT_HOST || '').trim().replace(/\/$/, '')
const staticDemo = import.meta.env.VITE_STATIC_DEMO === '1'

function assetUrl(path: string): string {
  const base = import.meta.env.BASE_URL || '/'
  const rel = path.replace(/^\//, '')
  return (base.endsWith('/') ? base : base + '/') + rel
}

/** 静态演示或不设主机时返回 `#`，避免公网暴露内网拓扑。 */
function intranetUrl(port: number, path = '/'): string {
  if (staticDemo || !toolkitHost) return '#'
  const p = path.startsWith('/') ? path : '/' + path
  return `http://${toolkitHost}:${port}${p}`
}

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
        status: 'warning',
        statusText: '需内网访问'
      },
      {
        abbr: 'UrbanCheck',
        name: '城市区位分析体检',
        description: '根据城市区位和公服配套设施，分析城市区域的发展潜力和问题。',
        url: intranetUrl(5180),
        icon: assetUrl('images/icon/map (1).svg'),
        status: 'pending',
        statusText: '需内网访问'
      },
      {
        abbr: 'RegionShot',
        name: '场地模型快速生成系统',
        description: '从网络获取数据，快速生成建筑设计前期的场地模型。',
        url: intranetUrl(5190),
        icon: assetUrl('images/icon/Urban Planning.svg'),
        status: 'pending',
        statusText: '需内网访问'
      },
      {
        abbr: 'PotentialStock',
        name: '存量楼宇项目挖掘看板',
        description: '上海市重点区域潜在存量楼宇改造项目的信息看板。',
        url: intranetUrl(5182),
        icon: assetUrl('images/icon/楼宇.svg'),
        status: 'pending',
        statusText: '需内网访问'
      },
      {
        abbr: 'StockAgent',
        name: '楼宇资产活化智能体',
        description: '',
        url: intranetUrl(5110),
        icon: assetUrl('images/icon/chat (1).svg'),
        status: 'pending',
        statusText: '需内网访问'
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
        status: 'pending',
        statusText: '需内网访问'
      },
      {
        abbr: 'OvalAgent',
        name: '体育建筑智能设计助手',
        description: '体育场馆设计的宝藏工具，体育建筑设计师的得力助手。',
        url: 'https://aiovaltool.com/',
        icon: assetUrl('images/icon/stadium.svg'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'HopeStation',
        name: '研究型医院数据库',
        description: '国内外研究型医院的图片和数据集成，为尖端医疗建筑设计提供参考。',
        url: 'http://medical.sstddev.com/',
        icon: assetUrl('images/icon/医院.svg'),
        status: 'active',
        statusText: '正常访问'
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
        status: 'pending',
        statusText: '需内网访问'
      }
    ]
  },
  {
    id: 'external',
    eyebrow: 'EXTERNAL RESOURCES',
    title: '外部资源',
    type: 'tools',
    tools: [
      {
        abbr: 'Aliyun',
        name: '阿里云控制台',
        description: '云计算资源管理平台，整合存储、计算、大语言模型等资源。',
        url: 'https://home.console.aliyun.com',
        icon: assetUrl('images/icon/阿里云官方-中文LOGO.svg'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'Rhino',
        name: '犀牛开发者',
        description: '参数化设计者必访的网站，犀牛官方开发指南。',
        url: 'https://developer.rhino3d.com/',
        icon: assetUrl('images/icon/rhinoceros.svg'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'ArchiBro',
        name: '建筑学长',
        description: '曾经的建模绘图素材大王，如今的智能渲染灵感来源。',
        url: 'https://www.jianzhuxuezhang.com/',
        icon: assetUrl('images/icon/jzxz.png'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'Food4Rhino',
        name: 'food4Rhino',
        description: 'Rhino 与 Grasshopper 插件市场，参数化工具链的安装入口。',
        url: 'https://www.food4rhino.com/',
        icon: assetUrl('images/icon/FoodRhino.png'),
        iconLg: true,
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'GoooodNet',
        name: '谷德设计网',
        description: '国内建筑师常用的建成案例与项目资讯平台。',
        url: 'https://www.gooood.cn/',
        icon: assetUrl('images/icon/GoooodNet.png'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'ShanghaiPlan',
        name: '上海规划资源',
        description: '上海市规划和自然资源局官网，城市更新与控规政策的官方口径。',
        url: 'https://ghzyj.sh.gov.cn/',
        icon: assetUrl('images/icon/shanghai.svg'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'TianDiTu',
        name: '天地图',
        description: '国家地理信息公共服务平台，场地建模与区位分析的权威底图。',
        url: 'https://www.tianditu.gov.cn/',
        icon: assetUrl('images/icon/TianDiTu.svg'),
        status: 'active',
        statusText: '正常访问'
      },
      {
        abbr: 'LiblibArt',
        name: '哩布哩布大模型资源',
        description: '国内 AI 绘画与模型社区，建筑概念图与智能渲染的灵感库。',
        url: 'https://www.liblib.art/',
        icon: assetUrl('images/icon/LiblibArt.png'),
        status: 'active',
        statusText: '正常访问'
      }
    ]
  }
]
