# Changelog

## 1.0.0 — 2026-10-19

首个可发布的 ArcMap 10.8 AI 助手版本。

### Added

- .NET Add-In + WinForms ArcMap 停靠窗口
- 对话式 GIS 问答与 Python 2.7 / arcpy 代码生成
- 外部 Python 2.7 执行模式（MXD 快照、非阻塞、可停止）
- ArcMap 进程内执行模式（伴生 Python Add-In，支持 `CURRENT` 和 ArcMap 许可）
- 地图上下文：图层、字段、坐标系、范围、选择数
- 自动路径识别：`full_path`、`data_paths`、`arcai_paths`、`arcai_path()`
- 结果回图、缩放、选择、可见性、移除图层、刷新
- PNG/JPG/PDF/TIFF 出图
- 兼容性检查（`arcpy.mp`、`CURRENT`、Python 3 语法等）
- 安全扫描、高风险确认
- 外部模式失败自动修复（最多 2 次重试）
- 内运行错误诊断（ImageServer、000732、CURRENT、SyntaxError 等）
- 栅格 `Clip_management` → `ExtractByMask` 兼容回退
- OpenAI 兼容 API、多个服务商预设
- DPAPI 加密 API Key
- 思考挡位（自动/低/中/高）
- 1,000,000 tokens 上下文预算
- 对话 UI：占位提示、进度条、时间戳、右键菜单、快捷键
- 22 项 Python 3 / ArcGIS Python 2.7 测试
- GitHub Actions Python 测试

### Known limitations

- 内运行会阻塞 ArcMap。
- 外部进程与 ArcMap Python 窗口的许可/许可扩展环境可能不同。
- 自动路径识别当前针对活动数据框；部分表格/特殊图层可能没有 `full_path`。
- 1M 上下文预算取决于模型实际支持的上下文窗口。
- AI 生成代码不是沙箱，运行前必须审查。
