using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArcAI.AddIn
{
    internal sealed class CodeExtraction
    {
        public string Code;
        public string Explanation;
        public bool HasCode;

        public CodeExtraction()
        {
            Code = string.Empty;
            Explanation = string.Empty;
            HasCode = false;
        }
    }

    /// <summary>Extracts the first suitable fenced Python block from a model reply.</summary>
    internal static class CodeExtractor
    {
        private sealed class Block
        {
            public string Language;
            public string Code;
            public int Index;
            public int Length;
        }

        private static readonly Regex Fence = new Regex(
            @"```[ \t]*(?<lang>[A-Za-z0-9_+.-]*)[ \t]*\r?\n(?<code>.*?)```",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        public static CodeExtraction Extract(string response)
        {
            var result = new CodeExtraction();
            if (string.IsNullOrWhiteSpace(response))
                return result;

            var blocks = new List<Block>();
            foreach (Match match in Fence.Matches(response))
            {
                blocks.Add(new Block
                {
                    Language = (match.Groups["lang"].Value ?? string.Empty).Trim().ToLowerInvariant(),
                    Code = (match.Groups["code"].Value ?? string.Empty).Trim(),
                    Index = match.Index,
                    Length = match.Length
                });
            }

            Block chosen = null;
            if (blocks.Count > 0)
            {
                chosen = blocks.FirstOrDefault(b => b.Language == "python" || b.Language == "py");
                if (chosen == null)
                    chosen = blocks.OrderByDescending(b => b.Code.Length).First();
            }
            else
            {
                int importIndex = response.IndexOf("import arcpy", StringComparison.OrdinalIgnoreCase);
                if (importIndex >= 0)
                {
                    chosen = new Block
                    {
                        Language = "python",
                        Code = response.Substring(importIndex).Trim(),
                        Index = importIndex,
                        Length = response.Length - importIndex
                    };
                }
            }

            if (chosen == null)
            {
                result.Explanation = response.Trim();
                return result;
            }

            string code = Normalize(chosen.Code);
            result.Code = code;
            result.HasCode = code.Length > 0;

            if (chosen.Index >= 0 && chosen.Length > 0 && chosen.Index + chosen.Length <= response.Length)
            {
                result.Explanation = (response.Substring(0, chosen.Index)
                    + response.Substring(chosen.Index + chosen.Length)).Trim();
            }
            else
            {
                result.Explanation = string.Empty;
            }
            return result;
        }

        private static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;
            string normalized = code.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            // A common failure mode is a bare "python" line inside the fence.
            if (normalized.StartsWith("python\n", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(7).TrimStart();
            return normalized;
        }
    }
}
