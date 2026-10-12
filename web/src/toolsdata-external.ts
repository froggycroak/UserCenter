import type { ToolSection } from './tools-data'
import { assetUrl } from './tools-data'

/**
 * 外部资源板块（与 tools-data.ts 并列）。
 * 由 shared/data/tools.xlsx（「外部」表）生成，npm run dev / build 时自动同步。请改表格，不要手改这个数组。
 * 本文件内容不计入内部产品支持标签。
 */
export const externalToolSections: ToolSection[] = [
  {
    id: 'ai-chat',
    eyebrow: 'DIALOGUE',
    title: '智能对话',
    type: 'tools',
    external: true,
    tools: [
      {
        abbr: 'Qwen',
        name: '通义千问',
        description: '阿里云通义实验室出品的通用大模型，文档处理与代码编写能力突出。',
        url: 'https://tongyi.aliyun.com/qianwen',
        icon: assetUrl('images/icon/QianWen.png'),
      },
      {
        abbr: 'Doubao',
        name: '豆包',
        description: '字节跳动旗下智能助手，多模态对话、办公辅助与日常问答的高频之选。',
        url: 'https://www.doubao.com',
        icon: assetUrl('images/icon/Doubao.png'),
      },
      {
        abbr: 'Kimi',
        name: '月之暗面',
        description: '月之暗面开发，长文本处理能力卓越，适合论文阅读与资料整理。',
        url: 'https://kimi.moonshot.cn',
        icon: assetUrl('images/icon/Kimi.png'),
      },
      {
        abbr: 'YuanBao',
        name: '腾讯元宝',
        description: '腾讯混元大模型驱动，兼具语言理解、计算、编程与绘画能力。',
        url: 'https://yuanbao.tencent.com',
        icon: assetUrl('images/icon/YuanBao.png'),
      },
      {
        abbr: 'Zhipu',
        name: '智谱清言',
        description: '智谱 AI 出品，数据分析与专业报告生成能力突出。',
        url: 'https://chatglm.cn',
        icon: assetUrl('images/icon/Zhipu.png'),
      },
      {
        abbr: 'DeepSeek',
        name: '深度求索',
        description: '深度求索开源大模型，推理与代码能力广受好评。',
        url: 'https://chat.deepseek.com',
        icon: assetUrl('images/icon/DeepSeek.png'),
      }
    ]
  },
  {
    id: 'office-agent',
    eyebrow: 'AGENT',
    title: '数字员工',
    type: 'tools',
    external: true,
    tools: [
      {
        abbr: 'WorkBuddy',
        name: '腾讯AI智能体',
        description: '腾讯全场景 AI 办公工作台，多专家协同自主规划并交付文档、数据、PPT。',
        url: 'https://www.workbuddy.ai/app',
        icon: assetUrl('images/icon/WorkBuddy.svg'),
      },
      {
        abbr: 'Trae',
        name: '字节AI工程师',
        description: '字节跳动 AI 办公平台，TraeWork 网页版支持文档、数据、演示稿全流程智能处理。',
        url: 'https://work.trae.cn',
        icon: assetUrl('images/icon/Trae.png'),
      },
      {
        abbr: 'Coze',
        name: '扣子',
        description: '字节跳动 AI Agent 开发平台，无需代码即可搭建办公智能体与工作流。',
        url: 'https://www.coze.cn',
        icon: assetUrl('images/icon/Coze.png'),
      },
      {
        abbr: 'CodeBuddy',
        name: '腾讯云代码助手',
        description: '腾讯云 AI 代码助手，从产品构思到部署上线的一站式智能编程工作台。',
        url: 'https://www.codebuddy.ai/agents',
        icon: assetUrl('images/icon/CodeBuddy.svg'),
      },
      {
        abbr: 'Qoder',
        name: '通义灵码',
        description: '阿里云通义系列 AI 编程助手，实时代码补全、智能问答与单元测试生成。',
        url: 'https://lingma.aliyun.com',
        icon: assetUrl('images/icon/Lingma.png'),
      },
      {
        abbr: 'DoubaoWork',
        name: '豆包工作',
        description: '字节跳动豆包面向生产力场景的 AI 工作平台，自主拆解任务、调用工具完成调研、创作与数据分析。',
        url: 'https://www.doubao.com/work',
        icon: assetUrl('images/icon/DoubaoWork.png'),
      }
    ]
  },
  {
    id: 'ai-creation',
    eyebrow: 'CREATION',
    title: '媒体创作',
    type: 'tools',
    external: true,
    tools: [
      {
        abbr: 'Jimeng',
        name: '即梦AI',
        description: '字节跳动旗下 AI 创作平台，支持图片、视频与音乐一站式生成。',
        url: 'https://jimeng.jianying.com',
        icon: assetUrl('images/icon/Jimeng.png'),
      },
      {
        abbr: 'Kling',
        name: '可灵AI',
        description: '快手推出的 AI 视频生成工具，图生视频一致性表现优异。',
        url: 'https://klingai.kuaishou.com',
        icon: assetUrl('images/icon/Kling.png'),
      },
      {
        abbr: 'WanXiang',
        name: '通义万相',
        description: '阿里云通义系列 AI 绘画与视频生成大模型。',
        url: 'https://wanxiang.aliyun.com',
        icon: assetUrl('images/icon/WanXiang.png'),
      },
      {
        abbr: 'Haimian',
        name: '海绵音乐',
        description: '字节跳动 AI 音乐创作平台，擅长中文歌曲的词曲生成。',
        url: 'https://www.haimian.com',
        icon: assetUrl('images/icon/Haimian.png'),
      }
    ]
  },
  {
    id: 'ai-search',
    eyebrow: 'REFERENCE',
    title: '资料查询',
    type: 'tools',
    external: true,
    tools: [
      {
        abbr: 'Metaso',
        name: '秘塔搜索',
        description: '专注 AI 搜索，聚合全网信息给出结构化答案与来源。',
        url: 'https://metaso.cn',
        icon: assetUrl('images/icon/Metaso.png'),
      },
      {
        abbr: 'ShanghaiPlan',
        name: '上海规划资源',
        description: '上海市规划和自然资源局官网，城市更新与控规政策的官方口径。',
        url: 'https://ghzyj.sh.gov.cn/',
        icon: assetUrl('images/icon/shanghai.svg'),
      },
      {
        abbr: 'TianDiTu',
        name: '天地图',
        description: '国家地理信息公共服务平台，场地建模与区位分析的权威底图。',
        url: 'https://www.tianditu.gov.cn/',
        icon: assetUrl('images/icon/TianDiTu.svg'),
      }
    ]
  },
  {
    id: 'design-resources',
    eyebrow: 'DESIGNAID',
    title: '设计辅助',
    type: 'tools',
    external: true,
    tools: [
      {
        abbr: 'RhinoDev',
        name: '犀牛开发者',
        description: '参数化设计者必访的网站，犀牛官方开发指南。',
        url: 'https://developer.rhino3d.com/',
        icon: assetUrl('images/icon/rhinoceros.svg'),
      },
      {
        abbr: 'Food4Rhino',
        name: '犀牛插件社区',
        description: 'Rhino 与 Grasshopper 插件市场，参数化工具链的安装入口。',
        url: 'https://www.food4rhino.com/',
        icon: assetUrl('images/icon/FoodRhino.png'),
        iconLg: true,
      },
      {
        abbr: 'ArchiBro',
        name: '建筑学长',
        description: '曾经的建模绘图素材大王，如今的智能渲染灵感来源。',
        url: 'https://www.jianzhuxuezhang.com/',
        icon: assetUrl('images/icon/jzxz.png'),
      },
      {
        abbr: 'GoooodNet',
        name: '谷德设计网',
        description: '国内建筑师常用的建成案例与项目资讯平台。',
        url: 'https://www.gooood.cn/',
        icon: assetUrl('images/icon/GoooodNet.png'),
      }
    ]
  },
  {
    id: 'cloud',
    eyebrow: 'BASEMENT',
    title: '技术底座',
    type: 'tools',
    external: true,
    tools: [
      {
        abbr: 'Aliyun',
        name: '阿里云控制台',
        description: '云计算资源管理平台，整合存储、计算、大语言模型等资源。',
        url: 'https://home.console.aliyun.com',
        icon: assetUrl('images/icon/阿里云官方-中文LOGO.svg'),
      },
      {
        abbr: 'LiblibArt',
        name: '哩布哩布大模型资源',
        description: '国内 AI 绘画与模型社区，建筑概念图与智能渲染的灵感库。',
        url: 'https://www.liblib.art/',
        icon: assetUrl('images/icon/LiblibArt.png'),
      }
    ]
  }
]
