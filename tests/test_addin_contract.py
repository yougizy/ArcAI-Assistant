# -*- coding: utf-8 -*-
"""Contract checks for the Add-In manifest and window-id handshake.

These are text/XML checks (no ArcObjects needed), so they run under both the
ArcGIS Python 2.7 and a normal Python 3 interpreter.
"""
from __future__ import print_function

import io
import os
import unittest
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CONFIG = os.path.join(ROOT, "ArcAI.AddIn", "Config.xml")
BUTTON = os.path.join(ROOT, "ArcAI.AddIn", "ShowAIButton.cs")
WINDOW = os.path.join(ROOT, "ArcAI.AddIn", "AIWindow.cs")
SETTINGS = os.path.join(ROOT, "ArcAI.AddIn", "SettingsForm.cs")
CONTROL = os.path.join(ROOT, "ArcAI.AddIn", "AIWindowControl.cs")
PROMPT = os.path.join(ROOT, "ArcAI.AddIn", "PromptBuilder.cs")
RUNNER = os.path.join(ROOT, "ArcAI.AddIn", "Python", "runner.py")
CONTEXT = os.path.join(ROOT, "ArcAI.AddIn", "ArcMapContext.cs")
ACTIONS = os.path.join(ROOT, "ArcAI.AddIn", "ArcMapActions.cs")
VALIDATOR = os.path.join(ROOT, "ArcAI.AddIn", "ArcPyValidator.cs")
APPSETTINGS = os.path.join(ROOT, "ArcAI.AddIn", "AppSettings.cs")
LLM = os.path.join(ROOT, "ArcAI.AddIn", "LlmClient.cs")
INPROC_CONFIG = os.path.join(ROOT, "InProcAddIn", "config.xml")
NS = "{http://schemas.esri.com/Desktop/AddIns}"


def read(path):
    with io.open(path, "r", encoding="utf-8") as handle:
        return handle.read()


class AddInContractTests(unittest.TestCase):
    def test_dockable_window_is_registered(self):
        tree = ET.parse(CONFIG)
        windows = tree.findall(".//" + NS + "DockableWindows/" + NS + "DockableWindow")
        self.assertEqual(1, len(windows), "expected exactly one DockableWindow")
        window = windows[0]
        self.assertEqual("ArcAI_DockableWindow", window.attrib.get("id"))
        self.assertEqual("AIWindow", window.attrib.get("class"))
        self.assertEqual(u"AI 助手", window.attrib.get("caption"))
        self.assertEqual("Images/ai16.png", window.attrib.get("image"))

    def test_button_uses_the_same_id_and_keeps_fallbacks(self):
        source = read(BUTTON)
        self.assertIn('"ArcAI_DockableWindow"', source)
        self.assertIn('"ArcAI.DockableWindow"', source)
        self.assertIn('"AIWindow"', source)
        self.assertIn('"ArcAI.AddIn.AIWindow"', source)
        self.assertIn("GetDockableWindow", source)

    def test_window_host_implements_create_child(self):
        source = read(WINDOW)
        self.assertIn("class AIWindow : DockableWindow", source)
        self.assertIn("override IntPtr OnCreateChild()", source)
        self.assertIn("AIWindow.OnCreateChild failed", source)

    def test_manifest_has_toolbar_and_extension(self):
        tree = ET.parse(CONFIG)
        self.assertIsNotNone(tree.find(".//" + NS + "Toolbars/" + NS + "Toolbar"))
        self.assertIsNotNone(tree.find(".//" + NS + "Extensions/" + NS + "Extension"))

    def test_settings_form_keeps_api_key_and_python_inputs_visible(self):
        # Regression guard: nested Panels with anchored TextBoxes collapsed to zero
        # width in ArcMap's DPI layout. Use TableLayoutPanel + Dock=Fill instead.
        source = read(SETTINGS)
        self.assertIn("var keyPanel = new TableLayoutPanel()", source)
        self.assertIn("var pythonPanel = new TableLayoutPanel()", source)
        self.assertIn("_apiKey.Dock = DockStyle.Fill", source)
        self.assertIn("_pythonPath.Dock = DockStyle.Fill", source)

    def test_chat_window_has_conversation_and_run_controls(self):
        source = read(CONTROL)
        for token in ("_chatBox", "_inputBox", "_sendButton", "_autoRunCheck",
                      "_autoRepairCheck", "AppendChat", "SendAsync", "RunCoreAsync",
                      "RunOnceAsync", "AutoRepairAsync", "SetCodeVisible",
                      "_openShotButton", u"打开出图", "_disposed", "UiAlive",
                      "SafeSendAsync", "SafeRunAsync", "SafeFixAsync",
                      "_busyBar", "_inputHintActive", "_autoScroll", "ContextMenuStrip",
                      "ProcessCmdKey", "CopyConversation", "InputHint"):
            self.assertIn(token, source)
        self.assertIn(u"生成后自动运行", source)
        self.assertIn(u"失败自动修复", source)
        self.assertIn(u"AppendCodeBlock", source)
        self.assertIn(u"AppendTool", source)

    def test_chat_ui_has_placeholder_and_shortcuts(self):
        source = read(CONTROL)
        for token in ("SetInputHint", "ClearInputHint", "UpdateSendState",
                      "Ctrl+Enter", u"复制全部对话", u"发送中…",
                      "ProgressBarStyle.Marquee", "_toolTip", "_chatMenu"):
            self.assertIn(token, source)

    def test_closing_window_cancels_and_guards_async_ui(self):
        source = read(CONTROL)
        self.assertIn("private volatile bool _disposed;", source)
        self.assertIn("if (!UiAlive())", source)
        self.assertIn("if (_disposed || IsDisposed)", source)
        self.assertIn("_requestCts.Cancel()", source)
        settings = read(SETTINGS)
        self.assertIn("private volatile bool _closed;", settings)
        self.assertIn("IsDisposed", settings)

    def test_system_prompt_allows_qa_without_forced_code(self):
        source = read(PROMPT)
        self.assertIn(u"不要强行输出代码", source)
        self.assertIn(u"普通问答不要附带无关代码", source)
        self.assertIn(u"若只是问答，直接回答，不要输出代码", source)
        self.assertIn(u"arcai_zoom_to_layer", source)
        self.assertIn(u"arcai_export_map", source)
        self.assertIn("ExtractByMask", source)

    def test_runner_exposes_map_actions_and_export(self):
        source = read(RUNNER)
        for token in ("arcai_zoom_to_layer", "arcai_select_by_attribute",
                      "arcai_export_map", "map_actions", "images",
                      "ExportToPNG", "ExportToPDF", "arcai_path", "arcai_paths",
                      "_install_clip_fallback", "ExtractByMask", "ImageServer"):
            self.assertIn(token, source)
        self.assertIn("Clip_management", read(VALIDATOR))

    def test_context_includes_full_paths_and_fields(self):
        source = read(CONTEXT)
        for token in ("full_path", "DescribeFields", "definition_query",
                      "CombineDatasetPath", "NormalizeDataSourcePath", "extent",
                      "ResolvePath", "DatasetPath", "data_paths"):
            self.assertIn(token, source)

    def test_arcpy_validator_catches_desktop_pitfalls(self):
        source = read(VALIDATOR)
        for token in ("arcpy\\.mp", "MapDocument", "f-string", "pathlib",
                      "while\\s+True", "HasCritical"):
            self.assertIn(token, source)

    def test_thinking_level_is_exposed_and_mapped(self):
        settings = read(APPSETTINGS)
        self.assertIn("ThinkingLevel", settings)
        self.assertIn(u'ThinkingLevel = "自动"', settings)
        llm = read(LLM)
        for token in ("ApplyThinking", "reasoning_effort", "enable_thinking",
                      "thinking_budget", "DeepSeek"):
            self.assertIn(token, llm)
        form = read(SETTINGS)
        self.assertIn(u"思考挡位", form)
        self.assertIn("_thinking", form)
        prompt = read(PROMPT)
        self.assertIn("GetSystemPrompt", prompt)
        self.assertIn(u"思考挡位", prompt)

    def test_context_budget_is_one_million(self):
        settings = read(APPSETTINGS)
        self.assertIn("ContextBudgetTokens = 1000000", settings)
        prompt = read(PROMPT)
        for token in ("DefaultContextBudgetTokens = 1000000", "HistoryTokenLimit",
                      "LimitToTokens", "EstimateTokens", "BuildUserPrompt"):
            self.assertIn(token, prompt)
        control = read(CONTROL)
        self.assertIn("_settings.ContextBudgetTokens", control)
        self.assertIn("HistoryTokens", control)
        form = read(SETTINGS)
        self.assertIn("_contextBudget", form)
        self.assertIn(u"上下文预算默认", form)
        context = read(CONTEXT)
        self.assertIn("MaxLayers = 500", context)
        self.assertIn("Math.Min(source.FieldCount, 200)", context)


    def test_inproc_addin_is_registered(self):
        tree = ET.parse(INPROC_CONFIG)
        addin_id = tree.find(".//" + NS + "AddInID")
        self.assertIsNotNone(addin_id)
        self.assertEqual("{8f3c2b61-5a7d-4e9c-b2f4-1d6a9c8e7b30}", addin_id.text)
        addin = tree.find(".//" + NS + "AddIn")
        self.assertIsNotNone(addin)
        self.assertEqual("PYTHON", addin.attrib.get("language"))
        self.assertEqual("ArcAIInProc_addin.py", addin.attrib.get("library"))
        button = tree.find(".//" + NS + "Button")
        self.assertIsNotNone(button)
        self.assertEqual("ArcAIInProc_addin.button", button.attrib.get("id"))
        self.assertEqual("InProcButton", button.attrib.get("class"))
        toolbar = tree.find(".//" + NS + "Toolbar")
        self.assertIsNotNone(toolbar)
        self.assertEqual("true", toolbar.attrib.get("showInitially"))
        self.assertEqual("ArcAIInProc_addin.button",
                         tree.find(".//" + NS + "Button[@refID]").attrib.get("refID"))

    def test_map_actions_are_applied_in_arcmap(self):
        source = read(ACTIONS)
        for token in ("ExecuteMapActions", "zoom_to_layer", "zoom_to_extent",
                      "set_layer_visible", "select_by_attribute", "clear_selection"):
            self.assertIn(token, source)


if __name__ == "__main__":
    unittest.main(verbosity=2)
