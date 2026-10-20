using System;
using System.Text;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Prompts sent to the model. The generated code runs in a separate Python 2.7
    /// process, not inside ArcMap, so the model must use the arcpy/out-of-process
    /// contract instead of arcpy.mapping.MapDocument("CURRENT").
    /// </summary>
    internal static class PromptBuilder
    {
        public const string SystemPrompt =
@"你是 ArcGIS Desktop 10.8 的 AI 助手，既能回答 GIS/ArcGIS 问题，也能在需要时生成并执行 Python 2.7 + arcpy 代码。

回答方式：
1. 如果用户只是询问概念、工具位置、操作步骤、报错原因或让你解释代码，直接用简洁中文回答，不要强行输出代码。
2. 如果用户需要处理数据、批量操作、空间分析、地图制图或修改 ArcGIS 内容，输出一个完整的 ```python 代码块，并在代码前后给出简短中文说明。
3. 只有在确实需要执行脚本时才给代码；普通问答不要附带无关代码。
4. 如果需求缺少关键路径、字段名或参数，先问一个关键问题，不要凭空猜测。

代码规则：
1. 代码必须兼容 Python 2.7 + arcpy：不要使用 f-string、pathlib、类型注解、async/await 等 Python 3 语法。中文字符串使用 u""。
2. 脚本在 ArcMap 进程之外的独立 Python 2.7 进程中运行，不能使用 arcpy.mapping.MapDocument(""CURRENT"")，也不能直接修改当前地图文档。
3. 当前地图上下文通过变量 arcai_context（dict）提供；当前文档快照路径通过 arcai_mxd 提供（可能为 None）；输出目录通过 arcai_out_dir 提供。
4. 如果希望把结果图层加入当前 ArcMap 地图，调用 arcai_add_layer(路径, 名称)（或 arcai_add_layers）；不要直接操作地图文档。
5. 默认不要覆盖已有数据；除非用户明确要求，不要删除或格式化任何数据。
6. 不要调用 os.system、subprocess、网络请求或 GUI；不要写无限循环；只使用 arcpy 和 Python 标准库。
7. 代码要自包含、可重复运行，包含必要的 arcpy.env 设置、坐标系检查、扩展模块检查、异常处理和 print/arcpy.AddMessage。
8. 需要代码时只输出一个 ```python 代码块，代码前后可以有简短中文说明；不要输出多个代码块、伪代码或 PowerShell。

地图数据上下文：
- 每个图层包含 full_path（可直接用于 arcpy 的完整数据源路径）、fields（字段名/类型）、spatial_reference、selected_count 等；读取或处理 ArcMap 中链接的数据时，优先使用 full_path 和 fields。
- 插件会自动解析图层数据源并写入上下文 data_paths（name/path 列表）；.shp、文件 GDB 的 feature class、栅格文件都可直接使用。
- 生成代码时优先调用 arcai_path(""图层名"") 或使用 full_path；不要猜测盘符、目录或文件名。路径缺失时先检查 arcai_paths，仍没有就询问用户。
- 需要直接操作 ArcMap（在脚本执行结束后由插件应用）时，调用：
  arcai_path(name)
  arcai_add_layer(path, name=None)
  arcai_remove_layer(name)
  arcai_set_layer_visible(name, visible)
  arcai_zoom_to_layer(name)
  arcai_zoom_to_extent(xmin, ymin, xmax, ymax, wkid=None)
  arcai_select_by_attribute(layer, where)
  arcai_clear_selection(layer=None)
  arcai_refresh()
- 需要出图时调用 arcai_export_map(path=None, format=""PNG"", dpi=150)。它把当前 MXD 快照导出为 PNG/JPG/PDF/TIFF，返回文件路径；插件会在聊天中提供打开入口。
- 地图操作与出图都应通过上述 helper 完成，不要用 arcpy.mapping.MapDocument(""CURRENT"")。

ArcGIS Desktop 10.8 兼容性自检（输出代码前必须逐条检查）：
- 不要使用 arcpy.mp（ArcGIS Pro API）；ArcMap 10.8 使用 arcpy.mapping。
- 不要使用 MapDocument(""CURRENT"")；使用 arcai_mxd 或上下文的 full_path。
- 不要使用 f-string、pathlib、typing、dataclasses、async/await、类型注解等 Python 3 语法；这是 Python 2.7。
- 优先使用 10.8 经典工具形式（arcpy.Buffer_analysis、arcpy.Clip_analysis、arcpy.Project_management、arcpy.SpatialJoin_analysis 等）；不要使用只有 ArcGIS Pro 才有的 API。
- 字段名必须与上下文的 fields 完全一致；输入路径先 arcpy.Exists 检查；输出写入 arcai_out_dir，使用唯一文件名，默认不覆盖。
- Spatial/3D Analyst 先 CheckExtension/CheckOutExtension，并在 finally CheckInExtension。
- 栅格裁剪优先使用 arcpy.sa.ExtractByMask（先 CheckOutExtension(""Spatial"")）；不要用 Clip_management 裁剪栅格，外部 Python 进程可能报 RasterCommander.ImageServer / 当前许可不支持影像服务器。向量裁剪仍可使用 Clip_management。
- 使用 arcpy.da 游标时字段名不要写错；中文路径使用 unicode，打印使用 print() 或 print 语句均可（Python 2.7）。
- 出图使用 arcai_export_map 或 arcpy.mapping.ExportToPNG/PDF（基于 arcai_mxd），不要使用 arcpy.mp。
- 如果缺少关键路径、字段或参数，先问一个关键问题，不要猜测。

上下文中的路径是用户本机路径，不要改写成其他路径。回答使用中文。";

        public static string GetSystemPrompt(string thinkingLevel)
        {
            string level = (thinkingLevel ?? string.Empty).Trim();
            if (level.Length == 0 || level == "自动")
                return SystemPrompt;
            return SystemPrompt + "\n\n本次请求的思考挡位：" + level
                + "。请据此控制分析深度和自检强度；代码规则仍然必须遵守。";
        }

        public static string BuildUserPrompt(string request, string contextJson,
            string mxdPath, string outDir,
            int contextBudgetTokens = DefaultContextBudgetTokens,
            int responseTokens = DefaultResponseTokens)
        {
            int inputLimit = InputTokenLimit(contextBudgetTokens, responseTokens);
            int contextLimit = ContextTokenLimit(contextBudgetTokens, responseTokens);
            var sb = new StringBuilder();
            sb.AppendLine("用户请求：");
            sb.AppendLine(request == null ? string.Empty : request.Trim());
            sb.AppendLine();
            sb.AppendLine("当前 ArcGIS 会话上下文（JSON，按 " + contextLimit + " tokens 预算裁剪）：");
            sb.AppendLine("```json");
            sb.AppendLine(LimitToTokens(contextJson, contextLimit));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("执行环境：ArcGIS Desktop 10.8 / Python 2.7 / arcpy；独立进程运行。");
            sb.AppendLine("arcai_mxd：" + (string.IsNullOrEmpty(mxdPath) ? "None（当前文档未保存）" : mxdPath));
            sb.AppendLine("arcai_out_dir：" + (outDir ?? string.Empty));
            sb.AppendLine("上下文预算：" + contextBudgetTokens + " tokens（输出预留 " + responseTokens
                + "，输入可用约 " + inputLimit + " tokens；地图上下文约占 55%，历史约占 30%）。");
            sb.AppendLine();
            sb.AppendLine("请回答用户。若需要执行 GIS 操作，请给出一个完整的 ```python 代码块和简短说明；"
                + "若只是问答，直接回答，不要输出代码。");
            return sb.ToString();
        }

        public const int DefaultContextBudgetTokens = 1000000;
        public const int DefaultResponseTokens = 4096;

        public static int InputTokenLimit(int contextBudgetTokens, int responseTokens)
        {
            int budget = contextBudgetTokens <= 0 ? DefaultContextBudgetTokens : contextBudgetTokens;
            return Math.Max(4000, budget - Math.Max(0, responseTokens));
        }

        public static int ContextTokenLimit(int contextBudgetTokens, int responseTokens)
        {
            return Math.Max(2000, InputTokenLimit(contextBudgetTokens, responseTokens) * 55 / 100);
        }

        public static int HistoryTokenLimit(int contextBudgetTokens, int responseTokens)
        {
            return Math.Max(2000, InputTokenLimit(contextBudgetTokens, responseTokens) * 30 / 100);
        }

        /// <summary>
        /// Conservative token estimate: ASCII roughly 4 chars/token, Chinese and
        /// other non-ASCII roughly 1 char/token. This is used only to bound the
        /// amount of context we send; it does not need to match a tokenizer.
        /// </summary>
        public static double EstimateTokens(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;
            double tokens = 0;
            for (int i = 0; i < value.Length; i++)
                tokens += value[i] <= 127 ? 0.25 : 1.0;
            return tokens;
        }

        public static string LimitToTokens(string value, int maxTokens)
        {
            if (string.IsNullOrEmpty(value))
                return "{}";
            int limit = Math.Max(1, maxTokens);
            double tokens = 0;
            for (int i = 0; i < value.Length; i++)
            {
                tokens += value[i] <= 127 ? 0.25 : 1.0;
                if (tokens > limit)
                    return value.Substring(0, i)
                        + "\n…（上下文已按 " + limit + " tokens 预算截断）";
            }
            return value;
        }

        public static string BuildRepairPrompt(string originalRequest, string code,
            string error, string output)
        {
            var sb = new StringBuilder();
            sb.AppendLine("下面这段代码在 ArcMap 外部的 Python 2.7 进程中运行失败。请修复它。");
            sb.AppendLine();
            sb.AppendLine("原始需求：");
            sb.AppendLine(originalRequest == null ? string.Empty : originalRequest.Trim());
            sb.AppendLine();
            sb.AppendLine("错误：");
            sb.AppendLine(error == null ? "(未提供)" : error.Trim());
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(output))
            {
                sb.AppendLine("捕获的输出（可能很长）：");
                sb.AppendLine("```text");
                sb.AppendLine(Limit(output, 6000));
                sb.AppendLine("```");
                sb.AppendLine();
            }
            sb.AppendLine("原代码：");
            sb.AppendLine("```python");
            sb.AppendLine(code == null ? string.Empty : code.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("如果错误包含 RasterCommander.ImageServer 或 当前许可不支持影像服务器，"
                + "请把栅格 Clip_management 改为 arcpy.sa.ExtractByMask(in_raster, mask).save(out_raster)，"
                + "并保留 Spatial 扩展检查；不要重复使用会触发该错误的栅格 Clip_management。");
            sb.AppendLine();
            sb.AppendLine("请只返回修复后的完整代码块（不要 diff、不要省略），并保持 Python 2.7 兼容。");
            return sb.ToString();
        }

        private static string Limit(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "{}";
            if (value.Length <= max)
                return value;
            return value.Substring(0, max) + "\n…（上下文已截断）";
        }
    }
}
