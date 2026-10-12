# Digital Urban Renew · User Center

建筑与城市更新研究场景下的**数字化门户与用户中心**：把工具导航、账号体系、智能助手收拢到同一套界面里，并作为其它应用的统一登录入口。

本仓库同时包含 **Research Portal**（`web_sstd`）：独立静态站点，nginx 挂在 **:1088**；User Center 仍由 Kestrel 一体托管在 **:1010**。

---

## 本站职能


| 模块                  | 说明                                            |
| ------------------- | --------------------------------------------- |
| **工具导航（Toolkit）**   | 城市研究、建筑智能、通用服务与外部资源的入口目录；内网工具仅在部署环境可点开。       |
| **用户中心（Account）**   | 注册（邀请码）、登录、资料与改密；其它网页可用 iframe 弹出同一套登录 / 资料窗。 |
| **智能助手（Assistant）** | 对话式入口，对接研究侧模型与工具能力（完整能力依赖服务端与内网服务）。           |
| **技术支持（Support）**   | 建议与反馈提交。                                      |
| **运维（Ops）**         | 邀请码、模型选择等管理能力（管理员白名单）。                        |


公开的 [GitHub Pages](https://pages.github.com/) 演示站只托管 **User Center（`web/`）页面壳**：可以浏览布局与交互；登录、对话、提交等接口在演示模式下为占位，便于展示产品形态而不暴露内网拓扑。

---

## 仓库结构（双前端）

| 路径 | 角色 | 运行 / 部署 |
|------|------|-------------|
| `web/` | User Center 前端 | Vite → `server/wwwroot`，Kestrel **:1010** |
| `web_sstd/` | Research Portal（原 user-portal） | Vite → `web_sstd/dist`，nginx **:1088** |
| `shared/data/tools.xlsx` | 工具目录唯一数据源 | 三表：`内部-ud` / `内部-sstd` / `外部` |
| `shared/public/` | 共用静态资源（含图标） | `web` 经 Junction；`web_sstd` 经 Vite `publicDir` |
| `server/` | .NET 8 API + 静态托管 | 仅服务 User Center |

### tools.xlsx

- **内部-ud** → `web/src/tools-data.ts`
- **内部-sstd** → `web_sstd/src/tools-data.ts`（两套内部分组互不复用）
- **外部** → 两应用的 `toolsdata-external.ts`（内容一致）

改表后：`npm run sync:tools`（单端）或 `npm run sync:tools:all`（两端，在 `web/` 下）。`dev` / `build` 会自动同步。

### 依赖

两前端共用同一 `node_modules` 实体（LibCustom Junction）。安装请用 `web\ensure_node_modules.bat`，**不要**在 Junction 上直接 `npm install`。

### 构建

```bat
build.bat          rem User Center：前端 + dotnet → :1010
build_sstd.bat     rem Portal：仅 web_sstd/dist → 交给 nginx :1088
```

nginx 示例：[`server/.script/nginx-web_sstd.example.conf`](server/.script/nginx-web_sstd.example.conf)。若 1088 不反代 `/api` 而直连 1010，请在 `CorsOrigins` 中加入 `http://<host>:1088`。

---


## 系列应用开发

User Center 扮演**门户 + 账号层**，串联本人主导的城市与建筑数字化应用（与门户 Toolkit 目录一致）。内网应用仅在部署环境可打开；下列公网链接可直接访问。

### 城市研究

- **StocKase** — 存量楼宇活化案例库：以案例数据驱动楼宇资产活化与策划评估。
- **UrbanCheck** — 城市区位分析体检：结合区位与公服配套，分析区域发展潜力与问题。
- **RegionShot** — 场地模型快速生成：从网络数据快速生成建筑设计前期场地模型。
- **PotentialStock** — 存量楼宇项目挖掘看板：上海重点区域潜在改造项目信息看板。
- **StockAgent** — 楼宇资产活化智能体。

### 建筑智能

- **ProtoMass** — 建筑原型智能设计：面向原创设计的建筑体量原型演绎。
- **OvalAgent** — [体育建筑智能设计助手](https://aiovaltool.com/)：体育场馆设计辅助。
- **HopeStation** — [研究型医院数据库](http://medical.sstddev.com/)：国内外研究型医院图文与数据参考。

### 通用服务

- **CookBook** — 在线知识库：内部知识沉淀与查阅。

---



## 技术概览

- **前端**：Vue 3 + Vite + vue-router（`web` + `web_sstd`）
- **后端**：.NET 8 Minimal API（Kestrel 一体托管 User Center 静态资源与 API）
- **身份**：对接 Casdoor（密码换票 / JWT），门户与其它应用共用一套账号

源码中的 `*.example.json` 仅为占位模板；真实主机、密钥与运维配置不放在公开仓库。

---
