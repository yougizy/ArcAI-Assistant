# ArcAI Assistant

**ArcMap 10.8 的对话式 AI/GIS 助手：读取当前地图上下文，生成 Python 2.7 + arcpy 代码，并可直接在 ArcMap 进程内或独立 Python 进程中执行。**

ArcAI Assistant 是一个 ArcMap 停靠窗口插件（.NET Add-In + WinForms），不是外部悬浮窗口。它把自然语言需求转换成 arcpy 脚本，支持预览/编辑/运行、结果回图、出图和失败自动修复；同时提供伴生 Python Add-In，用于在 **ArcMap 自己的 Python 2.7 进程和许可环境**中运行代码。

> 适用场景：ArcMap 10.8 的日常数据处理、批量分析、地图制图、路径排查和 Python 2.7/arcpy 脚本辅助编写。
>
> **AI 生成的代码不是沙箱**，会以当前 Windows 用户权限执行。运行前请检查代码并备份重要数据。

---

## 目录

- [核心功能](#核心功能)
- [两种运行模式](#两种运行模式)
- [架构](#架构)
- [环境要求](#环境要求)
- [构建](#构建)
- [安装](#安装)
- [首次使用](#首次使用)
- [使用示例](#使用示例)
- [脚本可用的上下文与 helper](#脚本可用的上下文与-helper)
- [地图路径自动识别](#地图路径自动识别)
- [错误诊断与自动修复](#错误诊断与自动修复)
- [安全说明](#安全说明)
- [数据与日志位置](#数据与日志位置)
- [测试](#测试)
- [目录结构](#目录结构)
- [常见问题](#常见问题)
- [License](#license)

---

## 核心功能

- **对话式 GIS 助手**：聊天记录、问答、代码、执行结果都在同一个停靠窗口中；普通 GIS 问题直接回答，不强行输出代码。
- **真正的 ArcMap 停靠窗口**：.NET Add-In + WinForms DockableWindow，可随 ArcMap 布局停靠。
- **读取当前地图数据**：自动提取图层名、数据源 `full_path`、字段、坐标系、范围、比例尺、选择数量等。
- **自动识别文件路径**：解析 Shapefile、文件 GDB 要素类、栅格、组图层和相对路径；提供 `data_paths`、`arcai_paths`、`arcai_path("图层名")`，减少模型猜路径。
- **两种执行环境**：
  - **外部 Python 2.7**：不冻结 ArcMap，使用 MXD 快照，适合长任务；
  - **ArcMap 进程内**：使用 ArcMap 的 Python 2.7/arcpy 和 ArcMap 许可，支持 `MapDocument("CURRENT")`。
- **代码生成与编辑**：提取模型返回的 ```` ```python ```` 代码块，在代码面板中预览、编辑、复制、保存。
- **运行前兼容性检查**：拦截 `arcpy.mp`、`MapDocument("CURRENT")`（外部模式）、f-string、pathlib、Python 3 print 参数等常见问题。
- **失败自动修复**：外部模式可自动把错误发回模型、生成修复代码并重试；内运行模式会识别错误类型并给出诊断，也可点击“AI 修复”。
- **栅格 Clip 兼容回退**：外部进程遇到 `RasterCommander.ImageServer` / “当前许可不支持影像服务器”时，自动对栅格改用 `arcpy.sa.ExtractByMask`。
- **结果回图**：`arcai_add_layer` / `arcai_add_layers` 把结果加入当前地图；支持 `.shp`、`.lyr`、文件 GDB 要素类/栅格、常见栅格文件。
- **地图操作 helper**：缩放、按属性选择、清除选择、图层可见性、移除图层、刷新、出图。
- **出图**：PNG/JPG/PDF/TIFF，聊天中显示路径，可直接“打开出图”。
- **UI 交互**：
  - 输入占位提示、发送按钮空输入禁用；
  - `Enter` 发送、`Shift+Enter` 换行、`Ctrl+Enter` 发送/运行、`Esc` 停止；
  - 忙碌进度条、消息时间戳、用户/工具消息底色；
  - 右键复制全部对话、自动滚动开关、清空确认；
  - 主要按钮工具提示、欢迎示例。
- **模型设置**：OpenAI 兼容接口，内置 DeepSeek、OpenAI、通义千问、Kimi、智谱 GLM、本地 Ollama 和自定义预设。
- **思考挡位**：自动 / 低 / 中 / 高；按服务商发送 `reasoning_effort` 或 `enable_thinking`/`thinking_budget`。
- **1M 上下文预算**：默认 1,000,000 tokens，可按模型实际上限调整；地图上下文和历史按 token 预算裁剪。
- **API Key 安全**：勾选“记住 Key”时使用 Windows DPAPI（当前用户）加密，明文不写入配置文件。
- **历史记录**：每次执行写入 `%APPDATA%\ArcAI\history.jsonl`，不包含 API Key。

---

## 两种运行模式

| | **外部运行**（“运行”按钮） | **内运行**（“内运行”按钮 / 自动运行） |
|---|---|---|
| 进程 | 独立 ArcGIS Python 2.7 子进程 | ArcMap 进程内的 Python 2.7 |
| 许可 | 独立 Python 的 ArcGIS 许可 | ArcMap 的许可环境 |
| 当前文档 | MXD 快照（`arcai_mxd`） | 当前文档；`arcai_mxd = CURRENT` |
| `MapDocument("CURRENT")` | 不允许 | 允许 |
| ArcMap 界面 | 保持响应，可停止并杀进程 | 执行期间会阻塞/无响应 |
| 适用 | 长任务、批量处理、怕拖垮 ArcMap | 依赖 ArcMap 许可/当前文档、需要 `CURRENT` 的代码 |
| 默认自动运行 | 关闭“自动运行用内运行”后使用 | **默认**；勾选“生成后自动运行”时使用内运行 |

### 内运行的工作方式

ArcMap 没有稳定公开的“从 .NET 直接执行 Python”接口，因此项目提供一个伴生的 **ArcAI InProc Python Add-In**：

```text
ArcAI 聊天窗口（.NET）
  └─ 写 inproc_job.json
       └─ 自动尝试触发 ArcAI InProc 按钮
            └─ ArcMap 进程内执行 runner.py
                 └─ 写 inproc_result.json
                      └─ ArcAI 窗口读取并显示结果
```

- 插件会尝试通过 ArcObjects 命令自动触发 `ArcAI InProc` 按钮；
- 如果自动触发失败，会提示你手动点击 **ArcMap → ArcAI InProc → 在 ArcMap 内执行**，然后继续等待结果；
- 内运行期间 ArcMap 会无响应，长任务建议先用外部运行验证。

---

## 架构

```text
ArcMap 10.8
├─ ArcAI Assistant (.NET Add-In / WinForms DockableWindow)
│  ├─ 对话 UI / 设置 / 历史
│  ├─ ArcMapContext：读取当前地图、图层、字段、路径
│  ├─ PromptBuilder：系统提示词 + 地图上下文 + 路径目录
│  ├─ LlmClient：OpenAI 兼容 /chat/completions
│  ├─ ArcPyValidator + SafetyScanner：兼容性与安全检查
│  ├─ PythonRunner：外部 Python 2.7 子进程
│  │    └─ Python/runner.py：arcpy 执行 + helper + 结果 JSON
│  └─ InProcRunner：写 job，等待内运行结果
│
└─ ArcAI InProc (Python Add-In)
   └─ 按钮：在 ArcMap 进程内运行 %APPDATA%\ArcAI\runner.py
```

数据流：

```text
用户需求
  → 地图上下文（full_path / fields / data_paths）
  → LLM
  → Python 2.7 代码
  → 兼容性检查 / 安全扫描
  → 外部进程 或 ArcMap 内运行
  → stdout / traceback / 结果图层 / 地图操作 / 图片
  → 回到聊天窗口
```

---

## 环境要求

- **ArcGIS Desktop / ArcMap 10.8（32 位）**
- .NET Framework 4.5+（ArcGIS 10.8 自带）
- ArcGIS Python **2.7**，默认为：
  `C:\Python27\ArcGIS10.8\python.exe`
- 构建需要：
  - .NET SDK（`dotnet build`）
  - 网络（首次还原 NuGet `Newtonsoft.Json` 和 .NET Framework 引用程序集）
  - Python 3（`build.ps1` 的打包脚本需要；用于生成 `dist\ArcAI-InProc.esriaddin`）

ArcObjects 引用默认从以下位置查找：

- `%WINDIR%\Microsoft.NET\assembly\GAC_MSIL`
- `D:\ARCGIS\Desktop10.8\bin`

如果 ArcGIS 安装在其他目录，请修改 `ArcAI.AddIn/ArcAI.AddIn.csproj` 中的 `ESRIBin` / `ESRIGAC`。

---

## 构建

```powershell
cd ArcAI-Assistant
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

产物：

```text
dist\ArcAI-Assistant.esriaddin   # 主 .NET Add-In
dist\ArcAI-InProc.esriaddin      # 内运行伴生 Python Add-In
```

`build.ps1` 会：

1. `dotnet build` 编译 .NET Add-In；
2. 打包主 Add-In（`config.xml` + `Images` + `Install`）；
3. 打包 `InProcAddIn` 为 Python Add-In。

---

## 安装

**完全关闭 ArcMap**，然后：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

安装脚本会安装两个 Add-In：

| Add-In | GUID / 位置 |
|---|---|
| ArcAI Assistant | `{6f2c9d41-8b7a-4c3e-9f10-2d5a7e4b6c81}` |
| ArcAI InProc | `{8f3c2b61-5a7d-4e9c-b2f4-1d6a9c8e7b30}` |

安装目录：

```text
%USERPROFILE%\Documents\ArcGIS\AddIns\Desktop10.8\{GUID}\
```

主 Add-In 需要解压后的 `Install\ArcAI.AddIn.dll`、`Images\`、`config.xml` 和 `.esriaddin`；脚本会自动处理、`Unblock-File` 并清除旧 AssemblyCache。

卸载（先关闭 ArcMap）：

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

`%APPDATA%\ArcAI` 下的设置、日志和历史默认保留；如需彻底清除 API Key，手动删除该目录。

### 手动安装（可选）

如果脚本安装失败，可关闭 ArcMap 后双击：

```text
dist\ArcAI-Assistant.esriaddin
dist\ArcAI-InProc.esriaddin
```

再重启 ArcMap。

---

## 首次使用

1. 重启 ArcMap。
2. 启用工具条：
   - `自定义 > 工具条 > ArcAI`
   - `自定义 > 工具条 > ArcAI InProc`
3. 点击 `ArcAI` 工具条的 **AI 助手**，打开停靠窗口。
4. 点击 **设置**：
   - 选择服务商预设，或填写 `Base URL` / 模型 / API Key；
   - 本地 Ollama 可不填 Key；
   - 选择思考挡位；
   - 选择是否发送地图上下文、运行前确认、自动运行用内运行。
5. 在输入框提问，`Enter` 发送。

> 如果 `ArcAI InProc` 工具条没有出现：先关闭 ArcMap，确认安装目录包含 `config.xml`，然后重启；仍没有时在 `自定义 > 工具条` 中手动勾选。内运行自动触发失败时，插件会提示你手动点击该工具条按钮。

---

## 使用示例

```text
列出当前地图的所有图层，并说明每个图层的字段和坐标系。
```

```text
用当前地图里的“道路”和“行政区”图层，统计每个行政区的道路总长度，
输出到 GDB，把结果加入地图并缩放到结果。
```

```text
把当前地图导出为 PNG，200 DPI。
```

```text
检查这段 arcpy 代码为什么报 ERROR 000732，并给出修复后的完整代码。
```

### 快捷命令

在输入框中输入：

- `运行` / `运行代码` / `执行`：运行代码面板中的当前代码（外部模式）；
- `修复` / `修复代码`：让 AI 修复上次失败；
- 代码面板的 **内运行**：在 ArcMap 进程内执行；
- 勾选 **生成后自动运行**：生成代码后按设置选择内运行/外部运行。

---

## 脚本可用的上下文与 helper

### 上下文

| 名称 | 说明 |
|---|---|
| `arcai_context` | 当前地图上下文 dict：文档、地图、图层、字段、范围、选择数量等 |
| `arcai_paths` | 图层名 → 已解析绝对路径的 dict |
| `arcai_path(name)` | 按图层名取数据源绝对路径；找不到会给出可用图层列表 |
| `arcai_mxd` | 外部模式为 MXD 快照路径；内运行为 `"CURRENT"` |
| `arcai_out_dir` | 可写输出目录 |

每个图层通常包含：

```json
{
  "name": "道路",
  "full_path": "D:\\GIS\\道路.shp",
  "workspace": "D:\\GIS",
  "layer_type": "feature",
  "fields": [{"name": "NAME", "type": "String"}],
  "spatial_reference": "WGS_1984_Web_Mercator_Auxiliary_Sphere",
  "extent": {"xmin": 0, "ymin": 0, "xmax": 1, "ymax": 1},
  "selected_count": 0
}
```

根节点还包含 `data_paths` 目录，便于模型直接选择输入数据。

### 地图操作 / 出图

| helper | 说明 |
|---|---|
| `arcai_add_layer(path, name=None)` | 登记结果图层；执行成功后加入当前地图 |
| `arcai_add_layers(paths)` | 批量登记 |
| `arcai_remove_layer(name)` | 移除当前地图图层 |
| `arcai_set_layer_visible(name, visible)` | 设置图层可见性 |
| `arcai_zoom_to_layer(name)` | 缩放到图层 |
| `arcai_zoom_to_extent(xmin, ymin, xmax, ymax, wkid=None)` | 缩放到范围 |
| `arcai_select_by_attribute(layer, where)` | 按属性选择 |
| `arcai_clear_selection(layer=None)` | 清除选择 |
| `arcai_refresh()` | 刷新地图 |
| `arcai_export_map(path=None, format="PNG", dpi=150)` | 导出 PNG/JPG/PDF/TIFF |
| `arcai_log(message)` | 进度输出 |

外部模式下，地图操作在脚本执行结束后由插件在 ArcMap 主线程应用；内运行可直接使用 `CURRENT` 修改当前文档。`arcai_export_map` 在外模式基于快照，在内模式基于当前文档。

---

## 地图路径自动识别

`ArcMapContext` 会为当前活动数据框中的图层解析：

- Shapefile → 完整 `.shp` 路径；
- 文件 GDB → `xxx.gdb\要素类`；
- 栅格 → 栅格文件路径；
- 组图层 → 递归子图层；
- 相对路径 → 按 MXD 所在目录解析为绝对路径；
- 同时保留 `fields`、`workspace`、`spatial_reference`、`extent`、`definition_query`。

模型被要求在生成代码时优先使用 `full_path` 或 `arcai_path("图层名")`，不要猜盘符和目录。路径缺失时先检查 `arcai_paths`，仍缺失再询问用户。

> 当前解析的是活动数据框中的图层；纯表格图层或未暴露数据源的图层可能没有 `full_path`。数据源损坏时路径仍可能保留，代码中的 `arcpy.Exists` 会报错。

---

## 错误诊断与自动修复

### 外部模式

1. 运行前执行 `ArcPyValidator` 兼容性检查和安全扫描；
2. 运行失败后，如果勾选“失败自动修复”，自动把原需求、代码、traceback、stdout 发回模型；
3. 生成修复代码并重试，最多 2 次；
4. 结果和错误进入后续对话上下文。

### 内运行模式

- 读取 `inproc_result.json` 中的 `error` / `traceback`；
- 自动分类常见问题：
  - `RasterCommander.ImageServer` / 影像服务器 → 建议改用 `ExtractByMask`；
  - `ERROR 000732` / 不存在 → 路径/图层名问题，使用 `full_path` / `arcai_path`；
  - `CURRENT` → 需要内运行；
  - `SyntaxError` → 可能被 `max_tokens` 截断，提高输出 tokens；
  - `ImportError` → 模块/扩展许可；
  - 拒绝访问 → 输出占用/权限。
- 可点击 **AI 修复** 让模型生成修复版本，再次内运行。

### 栅格 Clip 兼容回退

外部 Python 进程对栅格执行 `Clip_management` 时，某些 ArcGIS 环境会报：

```text
RasterCommander.ImageServer
当前许可不支持影像服务器
```

插件会检测该错误，并在输入确认是栅格时自动改用：

```python
arcpy.CheckOutExtension("Spatial")
arcpy.sa.ExtractByMask(in_raster, mask).save(out_raster)
```

矢量 `Clip_management` 不受影响。

---

## 安全说明

- **AI 代码不是沙箱**：会以当前用户权限执行，可能创建、修改或删除本机数据。
- 运行前检查代码；插件提供确认和风险提示，但最终判断由用户负责。
- 外部模式默认 `arcpy.env.overwriteOutput = False`；若模型设置为 `True`，安全扫描会警告。
- 内运行会阻塞 ArcMap，长任务请使用外部模式。
- API Key 通过 Windows DPAPI（CurrentUser）加密；不会明文写入 `settings.json`，不会写入脚本。
- 勾选“发送地图上下文”时，图层名、数据源路径、字段、范围等会发送到配置的 API；涉密数据请关闭。
- 不要把 `%APPDATA%\ArcAI`、`settings.json`、日志或 `.esriaddin` 构建产物提交到 GitHub。

---

## 数据与日志位置

```text
%APPDATA%\ArcAI\
├─ settings.json       # 设置；API Key 为 DPAPI 密文
├─ arcai.log           # 运行日志
├─ history.jsonl       # 每次执行的历史（不含 API Key）
├─ runner.py           # 内运行/外部运行共用的 Python runner
├─ inproc_job.json     # 内运行任务（临时）
├─ inproc_result.json  # 内运行结果（临时）
├─ outputs\            # 出图/输出
└─ snapshots\          # 外部模式 MXD 快照
```

输出默认目录：

```text
%APPDATA%\ArcAI\outputs\yyyyMMdd_HHmmss\
```

---

## 测试

```bash
# Python 3
python -m unittest discover -s tests -p "test_*.py" -v

# ArcGIS Python 2.7
C:\Python27\ArcGIS10.8\python.exe -m unittest discover -s tests -p "test_*.py" -v
```

当前测试（22 项）覆盖：

- runner 的 JSON / Unicode / 错误契约；
- `arcai_path` / `arcai_paths`；
- `Clip_management` 栅格兼容回退；
- 地图操作/出图 helper；
- Add-In `config.xml`、DockableWindow ID；
- InProc Python Add-In 注册与工具栏配置；
- 路径识别、上下文预算、思考挡位、UI 快捷交互；
- 设置窗口布局回归。

GitHub Actions 中只运行 Python 3 测试（`.github/workflows/tests.yml`）；.NET Add-In 和 ArcObjects 构建需要 Windows + ArcGIS Desktop 10.8，不能在托管 Linux runner 上完成。

---

## 目录结构

```text
ArcAI-Assistant/
├─ ArcAI.AddIn/
│  ├─ ArcAI.AddIn.csproj
│  ├─ Config.xml                 # 主 Add-In 清单
│  ├─ AIWindow.cs                # DockableWindow 宿主
│  ├─ AIWindowControl.cs         # 对话 UI / 运行流程 / 内运行
│  ├─ SettingsForm.cs            # API/模型/上下文设置
│  ├─ AppSettings.cs / Dpapi.cs  # 设置与 DPAPI
│  ├─ LlmClient.cs               # OpenAI 兼容客户端
│  ├─ PromptBuilder.cs           # 提示词、上下文预算、路径规则
│  ├─ CodeExtractor.cs           # 提取 Python 代码块
│  ├─ ArcPyValidator.cs          # 10.8 兼容性检查
│  ├─ SafetyScanner.cs           # 危险操作扫描
│  ├─ ArcMapContext.cs           # 地图/图层/路径上下文
│  ├─ ArcMapActions.cs           # 回图、地图操作、快照
│  ├─ PythonRunner.cs            # 外部 Python 2.7 执行
│  ├─ InProcRunner.cs            # 内运行 job/result 桥
│  ├─ Python/runner.py           # 外部/内运行共用的 Python runner
│  └─ Images/
├─ InProcAddIn/                  # ArcAI InProc Python Add-In
│  ├─ config.xml
│  ├─ ArcAIInProc_addin.py
│  └─ Images/
├─ tests/
│  ├─ test_runner.py
│  ├─ test_addin_contract.py
│  └─ fake_arcpy/
├─ tools/
│  ├─ make_icons.py
│  ├─ package_addin.py
│  ├─ package_inproc.py
│  └─ mock_llm_server.py
├─ build.ps1
├─ install.ps1
├─ uninstall.ps1
├─ README.md
├─ SECURITY.md
├─ CHANGELOG.md
├─ .github/workflows/tests.yml
└─ dist/                         # 构建产物（不提交）
```

---

## 常见问题

### 其他电脑无法连接第三方 API

按以下顺序排查：

1. **重新填写 API Key**：Key 用当前 Windows 用户 DPAPI 加密，**不能从开发机复制 `settings.json`**；每台电脑/每个用户都要重新输入。
2. **确认 Base URL、模型名和 Key**：在设置中点击“测试连接”；401/403 多为 Key/权限，404 多为 Base URL 路径，407 为代理认证。
3. **点击“网络诊断”**：检查 DNS、TCP、TLS 证书、系统代理和未携带 Key 的 HTTP HEAD 状态。
4. **代理/防火墙**：公司网络可能要求 Windows 系统代理；插件已尝试使用系统代理和 Windows 默认凭据。仍失败时检查代理、VPN、防火墙和 443 端口。
5. **SSL 证书**：企业代理替换证书时，安装根证书，或临时取消“验证 SSL 证书”（仅限可信网络）。
6. **TLS/系统**：ArcMap 10.8 使用 .NET Framework 4.5；确认系统更新启用 TLS 1.2，避免过旧的 Windows/证书环境。
7. **API 供应商限制**：确认该 Key 在目标电脑所在网络/地区可用，且模型有权限；某些供应商还需要特定 header 或区域地址。

诊断结果中：

- `DNS/TCP/TLS 失败`：网络、代理、证书、防火墙；
- `HTTP 401/403/405`：网络已连通，重点检查 Key、URL、模型权限；
- `HTTP 407`：代理认证；
- `HTTP 超时`：代理/防火墙/供应商网络。

### 工具条/窗口没有出现

- 安装时 ArcMap 必须完全关闭；
- 主 Add-In 目录需同时有 `config.xml`、`Images\`、`Install\ArcAI.AddIn.dll` 和 `.esriaddin`；
- 在 `自定义 > 工具条` 勾选 `ArcAI` / `ArcAI InProc`；
- 停靠窗口也可能在 ArcMap 的 `Windows` 菜单；
- 查看 `%APPDATA%\ArcAI\arcai.log`；没有日志通常意味着 Add-In 未加载；
- 重新运行 `install.ps1` 并重启 ArcMap。

### 内运行没有自动开始

- 确认 `ArcAI InProc` 已安装且 `config.xml` 存在；
- 插件自动触发命令可能受 ArcMap 版本/命令注册影响；提示手动点击时，点击 **ArcAI InProc → 在 ArcMap 内执行** 即可，窗口会继续等待结果；
- 若找不到按钮，关闭 ArcMap 后双击 `dist\ArcAI-InProc.esriaddin` 重新安装。

### 找不到 Python 2.7

- 设置中指定 `C:\Python27\ArcGIS10.8\python.exe`；
- 确认该 Python 能 `import arcpy`。

### API 返回 401/404

- 401：Key 错误或无权限；
- 404：Base URL 路径不对；确认是否包含 `/v1`，或填写完整 `/chat/completions` 地址。

### 代码被截断 / SyntaxError

- 提高设置中的“输出 tokens”（默认 8192，可到 32768）；
- 长脚本建议分步生成；
- 查看 `invalid syntax` 是否出现在最后一行。

### 结果图层没有加入地图

- 支持 `.shp`、`.lyr`、文件 GDB 要素类/栅格、常见栅格文件；
- 检查聊天中的“部分图层添加失败”提示；
- 其他数据源请手动添加。

---



- 版权人：胡景意
- 第三方依赖（ArcGIS/Esri、Newtonsoft.Json、模型 API 服务）不因本 MIT 许可证而被重新许可；请分别遵守其条款。
- 本项目不分发 ArcGIS 二进制文件。
