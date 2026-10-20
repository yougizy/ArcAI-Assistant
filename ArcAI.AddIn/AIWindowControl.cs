using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Chat-style ArcMap assistant. It can answer GIS questions or generate a
    /// Python 2.7/arcpy script, show it in an editable panel, run it outside
    /// ArcMap, and feed the execution result back into the conversation.
    /// </summary>
    public class AIWindowControl : UserControl
    {
        private AppSettings _settings;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly RichTextBox _chatBox;
        private readonly TextBox _inputBox;
        private readonly TextBox _codeBox;
        private readonly Button _sendButton;
        private readonly Button _stopButton;
        private readonly Button _settingsButton;
        private readonly Button _clearButton;
        private readonly Button _codeToggleButton;
        private readonly Button _runButton;
        private readonly Button _runInProcButton;
        private readonly Button _fixButton;
        private readonly Button _copyButton;
        private readonly Button _saveButton;
        private readonly Button _openShotButton;
        private readonly CheckBox _sendContextCheck;
        private readonly CheckBox _autoRunCheck;
        private readonly CheckBox _autoRepairCheck;
        private readonly Label _statusLabel;
        private readonly Label _providerLabel;
        private readonly Label _sessionLabel;
        private readonly SplitContainer _split;
        private readonly ProgressBar _busyBar;
        private readonly ContextMenuStrip _chatMenu;
        private readonly ToolTip _toolTip;

        private bool _busy;
        private volatile bool _disposed;
        private bool _lastRunSucceeded;
        private bool _lastRunCancelled;
        private bool _inputHintActive;
        private bool _autoScroll = true;
        private const string InputHint = "输入 GIS 需求，例如：统计每个行政区的道路总长度…";
        private bool _codeVisible;
        private CancellationTokenSource _requestCts;
        private CancellationTokenSource _runCts;
        private string _lastRequest = string.Empty;
        private string _lastCode = string.Empty;
        private string _lastError = string.Empty;
        private string _lastOutput = string.Empty;
        private string _lastImagePath = string.Empty;
        private string _outDir = string.Empty;

        public AIWindowControl()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            BackColor = SystemColors.Control;
            Size = new Size(520, 760);
            MinimumSize = new Size(380, 480);
            Dock = DockStyle.Fill;

            try
            {
                AppPaths.Ensure();
                _settings = AppSettings.Load();
            }
            catch (Exception ex)
            {
                Log.Error("Could not load settings in AIWindowControl.", ex);
                _settings = new AppSettings();
            }

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(8);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            Controls.Add(root);

            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.ColumnCount = 6;
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            root.Controls.Add(top, 0, 0);

            _statusLabel = new Label();
            _statusLabel.Text = "就绪";
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            top.Controls.Add(_statusLabel, 0, 0);

            _providerLabel = new Label();
            _providerLabel.AutoSize = true;
            _providerLabel.Dock = DockStyle.Fill;
            _providerLabel.TextAlign = ContentAlignment.MiddleRight;
            _providerLabel.ForeColor = SystemColors.GrayText;
            top.Controls.Add(_providerLabel, 1, 0);

            _busyBar = new ProgressBar();
            _busyBar.Style = ProgressBarStyle.Marquee;
            _busyBar.MarqueeAnimationSpeed = 30;
            _busyBar.Visible = false;
            _busyBar.Height = 10;
            _busyBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _busyBar.Margin = new Padding(4, 12, 4, 12);
            top.Controls.Add(_busyBar, 2, 0);

            _codeToggleButton = new Button();
            _codeToggleButton.Text = "代码";
            _codeToggleButton.Dock = DockStyle.Fill;
            _codeToggleButton.Click += delegate { SetCodeVisible(!_codeVisible); };
            top.Controls.Add(_codeToggleButton, 3, 0);

            _settingsButton = new Button();
            _settingsButton.Text = "设置";
            _settingsButton.Dock = DockStyle.Fill;
            _settingsButton.Click += OpenSettings;
            top.Controls.Add(_settingsButton, 4, 0);

            _clearButton = new Button();
            _clearButton.Text = "清空";
            _clearButton.Dock = DockStyle.Fill;
            _clearButton.Click += ClearAll;
            top.Controls.Add(_clearButton, 5, 0);

            var contextPanel = new Panel();
            contextPanel.Dock = DockStyle.Fill;
            root.Controls.Add(contextPanel, 0, 1);
            _sendContextCheck = new CheckBox();
            _sendContextCheck.Text = "发送地图上下文";
            _sendContextCheck.AutoSize = true;
            _sendContextCheck.Checked = _settings.SendContext;
            _sendContextCheck.Location = new Point(0, 5);
            _sendContextCheck.CheckedChanged += ContextToggled;
            contextPanel.Controls.Add(_sendContextCheck);
            _sessionLabel = new Label();
            _sessionLabel.AutoEllipsis = true;
            _sessionLabel.Dock = DockStyle.Fill;
            _sessionLabel.TextAlign = ContentAlignment.MiddleLeft;
            _sessionLabel.Padding = new Padding(150, 0, 0, 0);
            _sessionLabel.ForeColor = SystemColors.GrayText;
            contextPanel.Controls.Add(_sessionLabel);

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.Panel1MinSize = 120;
            _split.Panel2MinSize = 110;
            _split.Panel2Collapsed = true;
            root.Controls.Add(_split, 0, 2);

            _chatBox = new RichTextBox();
            _chatBox.ReadOnly = true;
            _chatBox.Dock = DockStyle.Fill;
            _chatBox.BackColor = Color.White;
            _chatBox.BorderStyle = BorderStyle.FixedSingle;
            _chatBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            _chatBox.WordWrap = true;
            _chatBox.DetectUrls = false;
            _chatBox.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f);
            _chatBox.HideSelection = false;
            _split.Panel1.Controls.Add(_chatBox);

            _chatMenu = new ContextMenuStrip();
            var copyAll = new ToolStripMenuItem("复制全部对话");
            copyAll.Click += CopyConversation;
            var autoScroll = new ToolStripMenuItem("自动滚动");
            autoScroll.CheckOnClick = true;
            autoScroll.Checked = _autoScroll;
            autoScroll.CheckedChanged += delegate { _autoScroll = autoScroll.Checked; };
            var clearChat = new ToolStripMenuItem("清空对话");
            clearChat.Click += ClearAll;
            _chatMenu.Items.Add(copyAll);
            _chatMenu.Items.Add(new ToolStripSeparator());
            _chatMenu.Items.Add(autoScroll);
            _chatMenu.Items.Add(clearChat);
            _chatBox.ContextMenuStrip = _chatMenu;

            var codeLayout = new TableLayoutPanel();
            codeLayout.Dock = DockStyle.Fill;
            codeLayout.ColumnCount = 1;
            codeLayout.RowCount = 2;
            codeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            codeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _split.Panel2.Controls.Add(codeLayout);

            var codeHeader = new TableLayoutPanel();
            codeHeader.Dock = DockStyle.Fill;
            codeHeader.ColumnCount = 7;
            codeHeader.RowCount = 1;
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            codeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            codeLayout.Controls.Add(codeHeader, 0, 0);

            var codeLabel = new Label();
            codeLabel.Text = "当前代码（可编辑）";
            codeLabel.Dock = DockStyle.Fill;
            codeLabel.TextAlign = ContentAlignment.MiddleLeft;
            codeHeader.Controls.Add(codeLabel, 0, 0);

            _runButton = MakeHeaderButton("运行", 66);
            _runButton.Click += delegate { SafeRunAsync(false); };
            _runButton.Enabled = false;
            codeHeader.Controls.Add(_runButton, 1, 0);

            _runInProcButton = MakeHeaderButton("内运行", 64);
            _runInProcButton.Click += delegate { SafeRunInProcAsync(); };
            _runInProcButton.Enabled = false;
            codeHeader.Controls.Add(_runInProcButton, 2, 0);

            _fixButton = MakeHeaderButton("AI 修复", 84);
            _fixButton.Click += delegate { SafeFixAsync(); };
            _fixButton.Enabled = false;
            codeHeader.Controls.Add(_fixButton, 3, 0);

            _copyButton = MakeHeaderButton("复制", 58);
            _copyButton.Click += CopyCode;
            codeHeader.Controls.Add(_copyButton, 4, 0);

            _saveButton = MakeHeaderButton("保存", 58);
            _saveButton.Click += SaveCode;
            codeHeader.Controls.Add(_saveButton, 5, 0);

            _openShotButton = MakeHeaderButton("打开出图", 82);
            _openShotButton.Enabled = false;
            _openShotButton.Click += OpenShot;
            codeHeader.Controls.Add(_openShotButton, 6, 0);

            _codeBox = new TextBox();
            _codeBox.Multiline = true;
            _codeBox.ScrollBars = ScrollBars.Both;
            _codeBox.WordWrap = false;
            _codeBox.AcceptsTab = true;
            _codeBox.Dock = DockStyle.Fill;
            _codeBox.Font = new Font("Consolas", 9.5f);
            _codeBox.BackColor = Color.White;
            _codeBox.TextChanged += delegate
            {
                _lastCode = _codeBox.Text;
                _runButton.Enabled = !_busy && !string.IsNullOrWhiteSpace(_lastCode);
                _runInProcButton.Enabled = !_busy && !string.IsNullOrWhiteSpace(_lastCode);
            };
            codeLayout.Controls.Add(_codeBox, 0, 1);

            var inputLayout = new TableLayoutPanel();
            inputLayout.Dock = DockStyle.Fill;
            inputLayout.ColumnCount = 2;
            inputLayout.RowCount = 2;
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            inputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.Controls.Add(inputLayout, 0, 3);

            _inputBox = new TextBox();
            _inputBox.Multiline = true;
            _inputBox.ScrollBars = ScrollBars.Vertical;
            _inputBox.AcceptsReturn = true;
            _inputBox.Dock = DockStyle.Fill;
            _inputBox.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f);
            _inputBox.KeyDown += InputKeyDown;
            _inputBox.KeyPress += InputKeyPress;
            _inputBox.TextChanged += InputTextChanged;
            _inputBox.LostFocus += InputLostFocus;
            inputLayout.Controls.Add(_inputBox, 0, 0);

            var sendPanel = new FlowLayoutPanel();
            sendPanel.Dock = DockStyle.Fill;
            sendPanel.FlowDirection = FlowDirection.TopDown;
            sendPanel.WrapContents = false;
            sendPanel.Padding = new Padding(4, 0, 0, 0);
            inputLayout.Controls.Add(sendPanel, 1, 0);

            _sendButton = new Button();
            _sendButton.Text = "发送";
            _sendButton.Width = 84;
            _sendButton.Height = 28;
            _sendButton.Click += delegate { SafeSendAsync(); };
            sendPanel.Controls.Add(_sendButton);

            _stopButton = new Button();
            _stopButton.Text = "停止";
            _stopButton.Width = 84;
            _stopButton.Height = 28;
            _stopButton.Enabled = false;
            _stopButton.Click += CancelWork;
            sendPanel.Controls.Add(_stopButton);

            var bottomBar = new FlowLayoutPanel();
            bottomBar.Dock = DockStyle.Fill;
            bottomBar.FlowDirection = FlowDirection.LeftToRight;
            bottomBar.WrapContents = false;
            inputLayout.Controls.Add(bottomBar, 0, 1);
            inputLayout.SetColumnSpan(bottomBar, 2);

            _autoRunCheck = new CheckBox();
            _autoRunCheck.Text = "生成后自动运行";
            _autoRunCheck.AutoSize = true;
            _autoRunCheck.Checked = false;
            bottomBar.Controls.Add(_autoRunCheck);

            _autoRepairCheck = new CheckBox();
            _autoRepairCheck.Text = "失败自动修复";
            _autoRepairCheck.AutoSize = true;
            _autoRepairCheck.Checked = true;
            _autoRepairCheck.Padding = new Padding(12, 0, 0, 0);
            bottomBar.Controls.Add(_autoRepairCheck);

            var hint = new Label();
            hint.Text = "Enter 发送 · Shift+Enter 换行 · Esc 停止";
            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Padding = new Padding(12, 4, 0, 0);
            bottomBar.Controls.Add(hint);

            _toolTip = new ToolTip();
            _toolTip.AutoPopDelay = 8000;
            _toolTip.InitialDelay = 500;
            _toolTip.ReshowDelay = 100;
            _toolTip.ShowAlways = true;
            _toolTip.SetToolTip(_codeToggleButton, "显示或隐藏代码面板");
            _toolTip.SetToolTip(_settingsButton, "配置 API、Python、上下文预算与思考挡位");
            _toolTip.SetToolTip(_clearButton, "清空当前对话和代码");
            _toolTip.SetToolTip(_runButton, "在独立 Python 2.7 进程中运行（Ctrl+Enter）");
            _toolTip.SetToolTip(_runInProcButton, "在 ArcMap 进程内运行，兼容 CURRENT 和 ArcMap 许可；会阻塞 ArcMap");
            _toolTip.SetToolTip(_fixButton, "把最后一次错误交给 AI 修复");
            _toolTip.SetToolTip(_copyButton, "复制当前代码");
            _toolTip.SetToolTip(_saveButton, "把当前代码保存为 .py");
            _toolTip.SetToolTip(_openShotButton, "打开最近一次导出的地图图片或输出目录");
            _toolTip.SetToolTip(_inputBox, "Enter 发送，Shift+Enter 换行，Ctrl+Enter 发送");
            _toolTip.SetToolTip(_codeBox, "可直接编辑；Ctrl+Enter 运行");
            _toolTip.SetToolTip(_chatBox, "右键：复制对话 / 自动滚动 / 清空");
            _toolTip.SetToolTip(_stopButton, "取消当前模型请求或终止 Python 进程（Esc）");
            SetInputHint();

            UpdateProviderLabel();
            UpdateSessionSummary();
            AppendChat("ArcAI",
                "你好，我是 ArcAI。可以问我 GIS/ArcGIS 问题，也可以让我生成并运行 Python 2.7 / arcpy 脚本。\n\n"
                + "可以试试：\n"
                + "• 列出当前地图图层，并说明每个图层的字段\n"
                + "• 统计每个行政区的道路总长度，输出到 GDB 并加入地图\n"
                + "• 把当前地图导出为 PNG\n\n"
                + "请先在“设置”中配置 API；需要我结合当前地图时，保持“发送地图上下文”勾选。",
                Color.Teal);
            AppendNotice("提示：代码会先在下方“代码”面板中显示；确认后点击“运行”，或勾选“生成后自动运行”。");
        }

        private static Button MakeHeaderButton(string text, int width)
        {
            var button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Width = width;
            button.Margin = new Padding(2, 3, 2, 3);
            return button;
        }

        private void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (_inputHintActive && e.Control
                && (e.KeyCode == Keys.V || e.KeyCode == Keys.X))
            {
                ClearInputHint();
                return;
            }
            if (_inputHintActive)
            {
                if (e.KeyCode == Keys.Enter)
                    e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                if (!_busy)
                {
                    SafeSendAsync();
                }
            }
        }

        private void InputKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!_inputHintActive)
                return;
            if (e.KeyChar != '\0' && (!char.IsControl(e.KeyChar) || e.KeyChar == '\b'))
                ClearInputHint();
        }

        private void InputTextChanged(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed)
                return;
            if (_inputHintActive && _inputBox.Text != InputHint)
            {
                // Pasting with the mouse/context menu bypasses KeyPress. Strip the
                // placeholder if it ended up in front of the pasted text.
                string text = _inputBox.Text;
                if (text.StartsWith(InputHint, StringComparison.Ordinal))
                    text = text.Substring(InputHint.Length);
                _inputHintActive = false;
                _inputBox.Text = text;
                _inputBox.SelectionStart = text.Length;
                _inputBox.ForeColor = SystemColors.WindowText;
            }
            if (!_inputHintActive)
                UpdateSendState();
        }

        private void InputLostFocus(object sender, EventArgs e)
        {
            if (!_inputHintActive && string.IsNullOrWhiteSpace(_inputBox.Text))
                SetInputHint();
        }

        private void SetInputHint()
        {
            if (_disposed || IsDisposed)
                return;
            _inputHintActive = true;
            _inputBox.Text = InputHint;
            _inputBox.ForeColor = SystemColors.GrayText;
            UpdateSendState();
        }

        private void ClearInputHint()
        {
            if (_disposed || IsDisposed)
                return;
            _inputHintActive = false;
            _inputBox.Clear();
            _inputBox.ForeColor = SystemColors.WindowText;
            UpdateSendState();
        }

        private void UpdateSendState()
        {
            if (_disposed || IsDisposed)
                return;
            _sendButton.Enabled = !_busy && !_inputHintActive
                && !string.IsNullOrWhiteSpace(_inputBox.Text);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_disposed && !IsDisposed)
            {
                if (keyData == Keys.Escape && _busy)
                {
                    CancelWork(this, EventArgs.Empty);
                    return true;
                }
                if (keyData == (Keys.Control | Keys.Enter))
                {
                    if (_busy)
                        return true;
                    if (_codeBox.Focused)
                        SafeRunAsync(false);
                    else
                        SafeSendAsync();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CopyConversation(object sender, EventArgs e)
        {
            if (!UiAlive() || _chatBox.TextLength == 0)
                return;
            try
            {
                Clipboard.SetText(_chatBox.Text);
                _statusLabel.Text = "对话已复制";
            }
            catch (Exception ex)
            {
                AppendNotice("复制失败：" + ex.Message);
            }
        }

        private void ContextToggled(object sender, EventArgs e)
        {
            _settings.SendContext = _sendContextCheck.Checked;
            try { _settings.Save(); } catch (Exception ex) { Log.Error("Could not save context preference.", ex); }
            UpdateSessionSummary();
        }

        private void UpdateProviderLabel()
        {
            if (_disposed || IsDisposed)
                return;
            _providerLabel.Text = _settings.Provider + " · " + _settings.Model + " · 思考:" + _settings.ThinkingLevel;
        }

        private void UpdateSessionSummary()
        {
            if (_disposed || IsDisposed)
                return;
            if (!_sendContextCheck.Checked)
            {
                _sessionLabel.Text = "地图上下文：不发送";
                return;
            }
            try
            {
                ESRI.ArcGIS.Framework.IApplication app = ArcSession.App();
                ESRI.ArcGIS.ArcMapUI.IMxDocument doc = ArcSession.Doc(app);
                string mxd = ArcSession.MxdPath(app);
                string name = string.IsNullOrEmpty(mxd) ? "未保存文档" : Path.GetFileName(mxd);
                _sessionLabel.Text = "地图上下文：" + name + " · " + doc.FocusMap.LayerCount + " 个图层";
            }
            catch
            {
                _sessionLabel.Text = "地图上下文：暂不可用";
            }
        }

        private async void SafeSendAsync()
        {
            try { await SendAsync(); }
            catch (Exception ex) { Log.Error("SendAsync failed after an await.", ex); }
        }

        private async void SafeRunAsync(bool autoRun)
        {
            try { await RunCoreAsync(autoRun); }
            catch (Exception ex) { Log.Error("RunCoreAsync failed after an await.", ex); }
        }

        private async void SafeFixAsync()
        {
            try { await FixAsync(); }
            catch (Exception ex) { Log.Error("FixAsync failed after an await.", ex); }
        }

        private async Task SendAsync()
        {
            if (_busy || _inputHintActive)
                return;
            string text = _inputBox.Text.Trim();
            if (text.Length == 0)
                return;

            // Short imperative commands operate on the current editor without a new API call.
            if (IsRunCommand(text) && !string.IsNullOrWhiteSpace(_codeBox.Text))
            {
                _inputBox.Clear();
                AppendChat("你", text, Color.FromArgb(0, 90, 158));
                await RunCoreAsync(false);
                return;
            }
            if (IsFixCommand(text) && !string.IsNullOrWhiteSpace(_lastError))
            {
                _inputBox.Clear();
                AppendChat("你", text, Color.FromArgb(0, 90, 158));
                await FixAsync();
                return;
            }

            if (!EnsureConfigured())
                return;

            _inputBox.Clear();
            AppendChat("你", text, Color.FromArgb(0, 90, 158));
            SetBusy(true, "正在思考…");
            _requestCts = new CancellationTokenSource();
            bool autoRun = false;

            try
            {
                JObject context;
                string mxdPath;
                string outDir;
                BuildContext(out context, out mxdPath, out outDir);
                string contextJson = context.ToString(Formatting.Indented);

                string userContent = PromptBuilder.BuildUserPrompt(text, contextJson, mxdPath, outDir,
                    _settings.ContextBudgetTokens, _settings.MaxTokens);
                var messages = new List<ChatMessage>();
                messages.Add(new ChatMessage("system", PromptBuilder.GetSystemPrompt(_settings.ThinkingLevel)));
                messages.AddRange(TrimHistory(_history));
                messages.Add(new ChatMessage("user", userContent));

                string response = await LlmClient.ChatAsync(_settings, messages, _requestCts.Token);
                if (!UiAlive())
                    return;
                CodeExtraction extracted = CodeExtractor.Extract(response);

                if (!string.IsNullOrWhiteSpace(extracted.Explanation))
                    AppendChat("ArcAI", extracted.Explanation, Color.Teal);
                else if (!extracted.HasCode)
                    AppendChat("ArcAI", response.Trim(), Color.Teal);

                if (extracted.HasCode)
                {
                    _lastCode = extracted.Code;
                    _codeBox.Text = _lastCode;
                    AppendCodeBlock(_lastCode);
                    SetCodeVisible(true);
                    _lastError = string.Empty;
                    _lastOutput = string.Empty;
                    _fixButton.Enabled = false;
                    AppendNotice("代码已生成。可检查/修改下方代码后运行；勾选“生成后自动运行”可自动执行。");
                    _statusLabel.Text = "代码已生成";
                    autoRun = _autoRunCheck.Checked;
                }
                else
                {
                    AppendNotice("本轮是问答，没有需要执行的代码。");
                    _statusLabel.Text = "已回答";
                }

                _lastRequest = text;
                _history.Add(new ChatMessage("user", text));
                _history.Add(new ChatMessage("assistant", response));
                TrimHistoryInPlace();
            }
            catch (OperationCanceledException)
            {
                AppendNotice("请求已取消。");
                _statusLabel.Text = "已取消";
            }
            catch (Exception ex)
            {
                Log.Error("Chat generation failed.", ex);
                AppendTool("错误", ex.Message, false);
                _lastError = ex.Message;
                _statusLabel.Text = "生成失败";
            }
            finally
            {
                if (_requestCts != null)
                {
                    _requestCts.Dispose();
                    _requestCts = null;
                }
                SetBusy(false, _statusLabel.Text);
                UpdateSessionSummary();
            }

            if (autoRun && !string.IsNullOrWhiteSpace(_codeBox.Text))
                await RunCoreAsync(true);
        }

        private async Task AutoRunAfterGenerateAsync()
        {
            if (_settings.AutoRunInProc)
                await RunInProcAsync();
            else
                await RunCoreAsync(true);
        }

        private async void SafeRunInProcAsync()
        {
            try { await RunInProcAsync(); }
            catch (Exception ex)
            {
                Log.Error("InProc run failed after an await.", ex);
                AppendNotice("内运行失败：" + ex.Message);
            }
        }

        private async Task RunInProcAsync()
        {
            if (_busy)
                return;
            string code = _codeBox.Text;
            if (string.IsNullOrWhiteSpace(code))
                return;
            List<ValidationIssue> issues = ArcPyValidator.Validate(code, true);
            if (ArcPyValidator.HasCritical(issues))
            {
                AppendTool("代码校验", ArcPyValidator.Format(issues), false);
                return;
            }
            if (_settings.ConfirmBeforeRun
                && MessageBox.Show(this,
                    "将在 ArcMap 进程内运行代码。运行期间 ArcMap 会无响应，请先保存地图。继续？",
                    "ArcAI 内运行", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                   != DialogResult.Yes)
                return;

            JObject context;
            string mxdPath;
            string outDir;
            BuildContext(out context, out mxdPath, out outDir);
            if (string.IsNullOrEmpty(outDir))
                outDir = ArcMapContext.CreateOutputDir();
            try
            {
                InProcRunner.Prepare(code, context, outDir);
                bool autoStarted = InProcRunner.TryExecuteButton();
                AppendNotice(autoStarted
                    ? "已自动在 ArcMap 进程内执行；运行期间 ArcMap 会无响应，正在等待结果…"
                    : "已准备在 ArcMap 内执行。请点击 ArcMap 上 ArcAI InProc 工具条的"
                      + "“在 ArcMap 内执行”按钮；正在等待结果…");
                SetBusy(true, "等待 ArcMap 内执行…");
                _runCts = new CancellationTokenSource();
                RunnerResult result = await InProcRunner.WaitAsync(
                    _settings.RunTimeoutSeconds, _runCts.Token);
                if (!UiAlive())
                    return;
                if (result.Ok)
                {
                    if (!string.IsNullOrWhiteSpace(result.Stdout))
                        AppendTool("运行输出", Limit(result.Stdout, 12000), true);
                    try
                    {
                        ESRI.ArcGIS.Framework.IApplication app = ArcSession.App();
                        var errors = new List<string>();
                        ArcMapActions.AddLayers(app, result.AddLayers, errors);
                        ArcMapActions.ExecuteMapActions(app, result.MapActions, errors);
                        try { ArcSession.Doc(app).ActiveView.Refresh(); } catch { }
                        if (errors.Count > 0)
                            AppendTool("地图操作", string.Join("\n", errors.ToArray()), false);
                    }
                    catch (Exception ex)
                    {
                        AppendTool("回图失败", ex.Message, false);
                    }
                    if (result.Images.Count > 0)
                    {
                        _lastImagePath = result.Images[0];
                        _openShotButton.Enabled = true;
                        AppendNotice("出图：" + _lastImagePath);
                    }
                    _lastRunSucceeded = true;
                    _lastError = string.Empty;
                    _fixButton.Enabled = false;
                    _statusLabel.Text = "ArcMap 内执行完成";
                }
                else
                {
                    AppendTool("运行错误", (result.Error ?? string.Empty) + "\n"
                        + (result.Traceback ?? string.Empty), false);
                    string diagnosis = DescribeExecutionError(result.Error, result.Traceback);
                    if (!string.IsNullOrEmpty(diagnosis))
                        AppendNotice("自动诊断：" + diagnosis);
                    _lastError = result.Error ?? "ArcMap 内执行失败。";
                    _lastOutput = (result.Stdout ?? string.Empty) + "\n" + (result.Stderr ?? string.Empty);
                    _fixButton.Enabled = true;
                    _statusLabel.Text = "执行失败";
                }
            }
            catch (OperationCanceledException)
            {
                AppendNotice("已取消等待内运行结果。");
            }
            catch (Exception ex)
            {
                Log.Error("InProc run failed.", ex);
                AppendTool("内运行失败", ex.Message, false);
                _lastError = ex.Message;
            }
            finally
            {
                if (_runCts != null)
                {
                    _runCts.Dispose();
                    _runCts = null;
                }
                SetBusy(false, _statusLabel.Text);
            }
        }

        private static string DescribeExecutionError(string error, string traceback)
        {
            string text = ((error ?? string.Empty) + "\n" + (traceback ?? string.Empty)).ToLowerInvariant();
            if (text.Contains("rasterserver") || text.Contains("imageserver")
                || text.Contains("影像服务器"))
                return "栅格裁剪触发了 ImageServer 许可问题；内运行已使用 ArcMap 许可，若仍失败请改用 arcpy.sa.ExtractByMask。";
            if (text.Contains("current") && (text.Contains("mapdocument") || text.Contains("mapping")))
                return "代码使用了 CURRENT；请确认是在“内运行”模式执行，并确保 ArcMap 已打开地图。";
            if (text.Contains("000732") || text.Contains("does not exist")
                || text.Contains("不存在"))
                return "输入路径或图层名无法解析；请使用上下文中的 full_path 或 arcai_path(\"图层名\")，不要使用裸图层名。";
            if (text.Contains("syntaxerror") || text.Contains("invalid syntax"))
                return "代码语法不完整，可能是模型输出被 max_tokens 截断；请在设置中提高“输出 tokens”，或点击 AI 修复。";
            if (text.Contains("importerror") || text.Contains("modulenotfounderror"))
                return "缺少 Python 模块或扩展；请检查 arcpy/扩展许可（Spatial、3D Analyst）是否可用。";
            if (text.Contains("permission") || text.Contains("access is denied")
                || text.Contains("拒绝访问"))
                return "输出文件被占用或没有写权限；请更换输出文件名/目录，或关闭占用文件的程序。";
            return null;
        }

        private async Task RunOnceAsync(bool autoRun)
        {
            if (_busy)
                return;
            _lastRunSucceeded = false;
            _lastRunCancelled = false;
            _lastError = string.Empty;
            string code = _codeBox.Text.Trim();
            if (code.Length == 0)
            {
                AppendNotice("没有可运行的代码。");
                return;
            }
            _lastCode = code;

            List<ValidationIssue> validation = ArcPyValidator.Validate(code);
            if (validation.Count > 0)
            {
                AppendNotice("ArcMap 10.8 兼容性检查：\n" + ArcPyValidator.Format(validation));
                if (ArcPyValidator.HasCritical(validation))
                {
                    _lastError = "静态兼容性检查失败：\n" + ArcPyValidator.Format(validation);
                    _statusLabel.Text = "代码校验失败";
                    return;
                }
            }

            List<SafetyWarning> warnings = SafetyScanner.Scan(code);
            if (warnings.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("安全检查发现 " + warnings.Count + " 项需要注意：");
                foreach (SafetyWarning warning in warnings)
                    sb.AppendLine("第 " + warning.Line + " 行：" + warning.Message);
                AppendNotice(sb.ToString().TrimEnd());
            }
            bool highRisk = SafetyScanner.HasHighRisk(warnings);
            bool mustConfirm = highRisk || (!autoRun && _settings.ConfirmBeforeRun);
            if (mustConfirm && !ConfirmRun(warnings, highRisk))
            {
                AppendNotice("已取消运行。");
                return;
            }

            string python = PythonRunner.ResolvePython(_settings.PythonPath);
            if (string.IsNullOrEmpty(python))
            {
                AppendNotice("找不到 ArcGIS Python 2.7。请在设置中指定 python.exe。");
                OpenSettings(null, EventArgs.Empty);
                python = PythonRunner.ResolvePython(_settings.PythonPath);
                if (string.IsNullOrEmpty(python))
                    return;
            }

            JObject context;
            string mxdPath;
            string outDir;
            BuildContext(out context, out mxdPath, out outDir);
            if (string.IsNullOrEmpty(outDir))
            {
                outDir = ArcMapContext.CreateOutputDir();
                context["output_dir"] = outDir;
            }
            _outDir = outDir;

            SetBusy(true, "正在保存地图快照…");
            string snapshot = null;
            try
            {
                snapshot = ArcMapActions.CreateSnapshot(ArcSession.App());
                if (string.IsNullOrEmpty(snapshot))
                    AppendNotice("未能创建地图快照；脚本仍可使用图层数据源路径和 arcai_out_dir。");
            }
            catch (Exception ex)
            {
                Log.Error("Snapshot failed.", ex);
                AppendNotice("创建快照失败：" + ex.Message);
            }

            _runCts = new CancellationTokenSource();
            SetBusy(true, "正在执行 Python 2.7…");
            try
            {
                RunnerResult result = await PythonRunner.RunAsync(python, code, context, snapshot,
                    outDir, _settings.RunTimeoutSeconds, _runCts.Token);
                if (!UiAlive())
                    return;

                if (result.Cancelled)
                {
                    _lastRunCancelled = true;
                    _statusLabel.Text = "已取消";
                    AppendTool("运行结果", "已取消。", false);
                    _lastError = "执行已取消。";
                    _lastOutput = result.Stdout + "\n" + result.Stderr;
                    _fixButton.Enabled = true;
                    AppendHistory(_lastRequest, code, false, "cancelled");
                    AddToolHistory("执行已取消。");
                }
                else if (result.Ok)
                {
                    _statusLabel.Text = "执行完成";
                    string output = string.IsNullOrWhiteSpace(result.Stdout) ? "（无输出）" : result.Stdout.Trim();
                    AppendTool("运行结果",
                        "执行成功，用时约 " + result.ElapsedSeconds.ToString("0.0") + " 秒。\n" + Limit(output, 8000),
                        true);
                    _lastError = string.Empty;
                    _lastOutput = result.Stdout + "\n" + result.Stderr;
                    _fixButton.Enabled = false;
                    _lastRunSucceeded = true;

                    if (result.AddLayers.Count > 0)
                    {
                        var errors = new List<string>();
                        int added = ArcMapActions.AddLayers(ArcSession.App(), result.AddLayers, errors);
                        AppendNotice("已把 " + added + " 个结果图层加入当前地图。");
                        if (errors.Count > 0)
                            AppendNotice("部分图层添加失败：\n" + Limit(string.Join("\n", errors.ToArray()), 4000));
                    }

                    if (result.MapActions.Count > 0)
                    {
                        var actionErrors = new List<string>();
                        int applied = ArcMapActions.ExecuteMapActions(ArcSession.App(),
                            result.MapActions, actionErrors);
                        if (applied > 0)
                            AppendNotice("已执行 " + applied + " 个 ArcMap 操作（缩放/选择/图层可见性等）。");
                        if (actionErrors.Count > 0)
                            AppendNotice("部分 ArcMap 操作失败：\n"
                                + Limit(string.Join("\n", actionErrors.ToArray()), 4000));
                    }

                    if (result.Images.Count > 0)
                    {
                        string image = result.Images[result.Images.Count - 1];
                        if (!string.IsNullOrWhiteSpace(image))
                        {
                            _lastImagePath = image;
                            _openShotButton.Enabled = File.Exists(image);
                            AppendNotice("出图已保存：" + image
                                + (File.Exists(image) ? "\n可点击代码面板右上角“打开出图”查看。" : ""));
                        }
                    }

                    AppendHistory(_lastRequest, code, true, string.Empty);
                    AddToolHistory("【脚本执行结果】成功，用时约 "
                        + result.ElapsedSeconds.ToString("0.0") + " 秒。\n"
                        + Limit(output, 4000));
                }
                else
                {
                    _statusLabel.Text = "执行失败";
                    string error = result.Error ?? "未知错误";
                    AppendTool("运行结果", "执行失败：" + error
                        + (string.IsNullOrWhiteSpace(result.Traceback) ? string.Empty
                           : "\n\n" + Limit(result.Traceback, 8000)), false);
                    _lastError = error
                        + (string.IsNullOrWhiteSpace(result.Traceback) ? string.Empty : "\n" + result.Traceback);
                    _lastOutput = result.Stdout + "\n" + result.Stderr;
                    _fixButton.Enabled = true;
                    SetCodeVisible(true);
                    AppendHistory(_lastRequest, code, false, _lastError);
                    AddToolHistory("【脚本执行结果】失败：" + Limit(_lastError, 4000));
                }
            }
            catch (OperationCanceledException)
            {
                AppendNotice("Python 执行已取消。");
                _lastRunCancelled = true;
                _statusLabel.Text = "已取消";
                AppendHistory(_lastRequest, code, false, "cancelled");
            }
            catch (Exception ex)
            {
                Log.Error("Python execution failed.", ex);
                AppendTool("错误", ex.Message, false);
                _lastError = ex.Message;
                _lastOutput = string.Empty;
                _fixButton.Enabled = true;
                _statusLabel.Text = "执行失败";
                AppendHistory(_lastRequest, code, false, ex.Message);
                AddToolHistory("【脚本执行结果】失败：" + Limit(ex.Message, 4000));
            }
            finally
            {
                if (!string.IsNullOrEmpty(snapshot))
                    ArcMapActions.DeleteSnapshot(snapshot);
                if (_runCts != null)
                {
                    _runCts.Dispose();
                    _runCts = null;
                }
                SetBusy(false, _statusLabel.Text);
                UpdateSessionSummary();
            }
        }

        private async Task RunCoreAsync(bool autoRun)
        {
            if (_busy)
                return;
            int maxAttempts = _autoRepairCheck.Checked ? 3 : 1;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await RunOnceAsync(autoRun);
                if (_disposed || IsDisposed)
                    return;
                if (_lastRunSucceeded || _lastRunCancelled)
                    return;
                if (string.IsNullOrWhiteSpace(_lastError))
                    return; // e.g. missing Python, user declined safety confirmation
                if (!_autoRepairCheck.Checked || attempt >= maxAttempts - 1)
                    return;

                AppendNotice("检测到执行/校验失败，正在自动修复并重试（"
                    + (attempt + 1) + "/" + (maxAttempts - 1) + "）…");
                bool repaired = await AutoRepairAsync();
                if (_disposed || IsDisposed)
                    return;
                if (!repaired)
                    return;
            }
        }

        /// <summary>One repair request used by the automatic retry loop.</summary>
        private async Task<bool> AutoRepairAsync()
        {
            if (_busy)
                return false;
            if (string.IsNullOrWhiteSpace(_lastCode) || string.IsNullOrWhiteSpace(_lastError))
                return false;
            if (!EnsureConfigured())
                return false;

            SetBusy(true, "正在请求 AI 修复…");
            _requestCts = new CancellationTokenSource();
            string repairError = _lastError;
            try
            {
                string repair = PromptBuilder.BuildRepairPrompt(_lastRequest, _lastCode,
                    repairError, _lastOutput);
                var messages = new List<ChatMessage>();
                messages.Add(new ChatMessage("system", PromptBuilder.GetSystemPrompt(_settings.ThinkingLevel)));
                messages.AddRange(TrimHistory(_history));
                messages.Add(new ChatMessage("user", repair));

                string response = await LlmClient.ChatAsync(_settings, messages, _requestCts.Token);
                if (!UiAlive())
                    return false;
                CodeExtraction extracted = CodeExtractor.Extract(response);
                if (!extracted.HasCode)
                {
                    AppendNotice("自动修复没有返回代码。");
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(extracted.Explanation))
                    AppendChat("ArcAI", extracted.Explanation, Color.Teal);
                _lastCode = extracted.Code;
                _codeBox.Text = _lastCode;
                AppendCodeBlock(_lastCode);
                SetCodeVisible(true);
                _lastError = string.Empty;
                _lastOutput = string.Empty;
                _fixButton.Enabled = false;
                _statusLabel.Text = "自动修复完成";

                _history.Add(new ChatMessage("user",
                    "【自动修复】" + Limit(repairError, 1000)));
                _history.Add(new ChatMessage("assistant", response));
                TrimHistoryInPlace();
                return true;
            }
            catch (OperationCanceledException)
            {
                _lastRunCancelled = true;
                AppendNotice("自动修复已取消。");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Auto repair failed.", ex);
                AppendTool("错误", "自动修复失败：" + ex.Message, false);
                _statusLabel.Text = "修复失败";
                return false;
            }
            finally
            {
                if (_requestCts != null)
                {
                    _requestCts.Dispose();
                    _requestCts = null;
                }
                SetBusy(false, _statusLabel.Text);
            }
        }

        private async Task FixAsync()
        {
            if (_busy)
                return;
            if (string.IsNullOrWhiteSpace(_lastCode) || string.IsNullOrWhiteSpace(_lastError))
            {
                AppendNotice("还没有可修复的失败执行。");
                return;
            }
            if (!EnsureConfigured())
                return;

            AppendChat("你", "请修复上次失败。", Color.FromArgb(0, 90, 158));
            SetBusy(true, "正在请求修复…");
            string repairError = _lastError;
            _requestCts = new CancellationTokenSource();
            bool autoRun = false;
            try
            {
                string repair = PromptBuilder.BuildRepairPrompt(_lastRequest, _lastCode, _lastError, _lastOutput);
                var messages = new List<ChatMessage>();
                messages.Add(new ChatMessage("system", PromptBuilder.GetSystemPrompt(_settings.ThinkingLevel)));
                messages.AddRange(TrimHistory(_history));
                messages.Add(new ChatMessage("user", repair));

                string response = await LlmClient.ChatAsync(_settings, messages, _requestCts.Token);
                if (!UiAlive())
                    return;
                CodeExtraction extracted = CodeExtractor.Extract(response);
                if (!string.IsNullOrWhiteSpace(extracted.Explanation))
                    AppendChat("ArcAI", extracted.Explanation, Color.Teal);
                if (extracted.HasCode)
                {
                    _lastCode = extracted.Code;
                    _codeBox.Text = _lastCode;
                    AppendCodeBlock(_lastCode);
                    SetCodeVisible(true);
                    _lastError = string.Empty;
                    _lastOutput = string.Empty;
                    _fixButton.Enabled = false;
                    autoRun = _autoRunCheck.Checked;
                    _statusLabel.Text = "修复完成";
                    AppendNotice("已生成修复版本，可检查后运行。");
                }
                else
                {
                    _statusLabel.Text = "已回答";
                    AppendNotice("模型没有返回修复代码，请查看说明。");
                }

                _history.Add(new ChatMessage("user", "请修复上次失败：" + Limit(repairError, 1000)));
                _history.Add(new ChatMessage("assistant", response));
                TrimHistoryInPlace();
            }
            catch (OperationCanceledException)
            {
                AppendNotice("修复请求已取消。");
                _statusLabel.Text = "已取消";
            }
            catch (Exception ex)
            {
                Log.Error("Repair request failed.", ex);
                AppendTool("错误", ex.Message, false);
                _statusLabel.Text = "修复失败";
            }
            finally
            {
                if (_requestCts != null)
                {
                    _requestCts.Dispose();
                    _requestCts = null;
                }
                SetBusy(false, _statusLabel.Text);
            }

            if (autoRun && !string.IsNullOrWhiteSpace(_codeBox.Text))
                await AutoRunAfterGenerateAsync();
        }

        private void AddToolHistory(string text)
        {
            _history.Add(new ChatMessage("user", text));
            TrimHistoryInPlace();
        }

        private void BuildContext(out JObject context, out string mxdPath, out string outDir)
        {
            context = new JObject();
            mxdPath = null;
            outDir = null;
            if (_sendContextCheck.Checked)
            {
                try
                {
                    context = ArcMapContext.Capture(ArcSession.App());
                }
                catch (Exception ex)
                {
                    Log.Error("Context capture failed; continuing without context.", ex);
                    AppendNotice("读取地图上下文失败：" + ex.Message);
                    context = new JObject();
                }
            }

            JToken documentPath = context["document"] == null ? null : context["document"]["path"];
            if (documentPath != null && documentPath.Type != JTokenType.Null)
                mxdPath = documentPath.ToString();

            JToken output = context["output_dir"];
            if (output != null && output.Type != JTokenType.Null)
                outDir = output.ToString();
            if (string.IsNullOrEmpty(outDir))
            {
                outDir = ArcMapContext.CreateOutputDir();
                context["output_dir"] = outDir;
            }
            _outDir = outDir;
        }

        private bool ConfirmRun(List<SafetyWarning> warnings, bool highRisk)
        {
            if (!UiAlive())
                return false;
            var sb = new StringBuilder();
            sb.AppendLine("即将以当前 Windows 用户权限运行生成的 Python 2.7 脚本。");
            sb.AppendLine("脚本可能在磁盘上创建、修改或删除数据，请先检查代码。");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("安全检查提示：");
                int count = 0;
                foreach (SafetyWarning warning in warnings)
                {
                    sb.AppendLine("  第 " + warning.Line + " 行：" + warning.Message);
                    count++;
                    if (count >= 8)
                    {
                        sb.AppendLine("  …");
                        break;
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("是否继续运行？");
            return MessageBox.Show(this, sb.ToString(), "ArcAI · 运行确认",
                MessageBoxButtons.YesNo,
                highRisk ? MessageBoxIcon.Warning : MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private bool EnsureConfigured()
        {
            if (!UiAlive())
                return false;
            bool local = IsLocal(_settings.BaseUrl);
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl)
                && !string.IsNullOrWhiteSpace(_settings.Model)
                && (!string.IsNullOrWhiteSpace(_settings.ApiKey) || local))
                return true;

            DialogResult answer = MessageBox.Show(this,
                "尚未完整配置 API（Base URL、模型或 API Key）。是否现在打开设置？",
                "ArcAI", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes)
                return false;
            OpenSettings(null, EventArgs.Empty);
            local = IsLocal(_settings.BaseUrl);
            return !string.IsNullOrWhiteSpace(_settings.BaseUrl)
                && !string.IsNullOrWhiteSpace(_settings.Model)
                && (!string.IsNullOrWhiteSpace(_settings.ApiKey) || local);
        }

        private static bool IsLocal(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            return url.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRunCommand(string text)
        {
            string value = (text ?? string.Empty).Trim().TrimEnd('。', '！', '!').ToLowerInvariant();
            return value == "运行" || value == "运行代码" || value == "执行"
                || value == "执行代码" || value == "run";
        }

        private static bool IsFixCommand(string text)
        {
            string value = (text ?? string.Empty).Trim().TrimEnd('。', '！', '!').ToLowerInvariant();
            return value == "修复" || value == "修复代码" || value == "fix";
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed)
                return;
            try
            {
                using (var form = new SettingsForm(_settings))
                {
                    if (form.ShowDialog(this) == DialogResult.OK && form.Result != null)
                    {
                        _settings = form.Result;
                        _sendContextCheck.Checked = _settings.SendContext;
                        UpdateProviderLabel();
                        UpdateSessionSummary();
                        AppendNotice("设置已保存。");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings dialog failed.", ex);
                AppendTool("错误", "打开设置失败：" + ex.Message, false);
            }
        }

        private void CopyCode(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed)
                return;
            if (string.IsNullOrWhiteSpace(_codeBox.Text))
                return;
            try
            {
                Clipboard.SetText(_codeBox.Text);
                _statusLabel.Text = "代码已复制到剪贴板";
            }
            catch (Exception ex)
            {
                AppendNotice("复制失败：" + ex.Message);
            }
        }

        private void SaveCode(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed)
                return;
            if (string.IsNullOrWhiteSpace(_codeBox.Text))
                return;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "Python 脚本|*.py|所有文件|*.*";
                dialog.FileName = "arcai_script.py";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    File.WriteAllText(dialog.FileName, _codeBox.Text, new UTF8Encoding(false));
                    _statusLabel.Text = "已保存脚本";
                    AppendNotice("已保存：" + dialog.FileName);
                }
                catch (Exception ex)
                {
                    AppendTool("错误", "保存失败：" + ex.Message, false);
                }
            }
        }

        private void OpenShot(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed)
                return;
            try
            {
                if (!string.IsNullOrWhiteSpace(_lastImagePath) && File.Exists(_lastImagePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _lastImagePath,
                        UseShellExecute = true
                    });
                    return;
                }
                AppPaths.Ensure();
                Directory.CreateDirectory(AppPaths.Outputs);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + AppPaths.Outputs + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendNotice("打开出图失败：" + ex.Message);
            }
        }

        private void CancelWork(object sender, EventArgs e)
        {
            if (_requestCts != null)
            {
                _requestCts.Cancel();
                AppendNotice("正在取消模型请求…");
            }
            if (_runCts != null)
            {
                _runCts.Cancel();
                AppendNotice("正在终止 Python 进程…");
            }
        }

        private void ClearAll(object sender, EventArgs e)
        {
            if (_disposed || IsDisposed)
                return;
            if (_busy)
                return;
            if (_history.Count > 0 || _chatBox.TextLength > 0)
            {
                if (MessageBox.Show(this, "清空当前对话和代码？", "ArcAI",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            _history.Clear();
            _inputHintActive = false;
            _inputBox.Clear();
            _inputBox.ForeColor = SystemColors.WindowText;
            _codeBox.Text = string.Empty;
            _chatBox.Clear();
            _lastRequest = string.Empty;
            _lastCode = string.Empty;
            _lastError = string.Empty;
            _lastOutput = string.Empty;
            _lastImagePath = string.Empty;
            _openShotButton.Enabled = false;
            _fixButton.Enabled = false;
            _runButton.Enabled = false;
            SetCodeVisible(false);
            _statusLabel.Text = "已清空";
            _statusLabel.ForeColor = SystemColors.ControlText;
            if (!_inputBox.Focused)
                SetInputHint();
            else
                UpdateSendState();
            AppendChat("ArcAI",
                "对话已清空。可以继续问 GIS 问题，或让我生成并运行 Python 2.7 / arcpy 脚本。",
                Color.Teal);
        }

        private void SetBusy(bool busy, string status)
        {
            if (_disposed || IsDisposed)
                return;
            _busy = busy;
            _busyBar.Visible = busy;
            _statusLabel.ForeColor = busy ? Color.FromArgb(0, 90, 158) : SystemColors.ControlText;
            _sendButton.Text = busy ? "发送中…" : "发送";
            UpdateSendState();
            _inputBox.Enabled = !busy;
            _stopButton.Enabled = busy;
            _settingsButton.Enabled = !busy;
            _clearButton.Enabled = !busy;
            _autoRunCheck.Enabled = !busy;
            _autoRepairCheck.Enabled = !busy;
            _codeToggleButton.Enabled = true;
            _runButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_codeBox.Text);
            _runInProcButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_codeBox.Text);
            _fixButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_lastError);
            _openShotButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_lastImagePath)
                && File.Exists(_lastImagePath);
            if (!string.IsNullOrEmpty(status))
                _statusLabel.Text = status;
            UseWaitCursor = busy;
        }

        private void SetCodeVisible(bool visible)
        {
            if (_disposed || IsDisposed)
                return;
            if (_codeVisible == visible)
                return;
            _codeVisible = visible;
            _split.Panel2Collapsed = !visible;
            _codeToggleButton.Text = visible ? "隐藏代码" : "代码";
            if (visible)
            {
                try { _split.SplitterDistance = Math.Max(140, _split.Height - 240); }
                catch { }
                _codeBox.Focus();
            }
        }

        private void AppendChat(string speaker, string text, Color color)
        {
            if (!UiAlive() || string.IsNullOrEmpty(text))
                return;
            AppendSpeaker(speaker, color);
            _chatBox.SelectionColor = Color.Black;
            _chatBox.SelectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f);
            _chatBox.SelectionBackColor = speaker == "你"
                ? Color.FromArgb(235, 245, 255)
                : Color.White;
            _chatBox.AppendText(text.TrimEnd() + Environment.NewLine + Environment.NewLine);
            _chatBox.SelectionBackColor = Color.White;
            ScrollChat();
        }

        private void AppendNotice(string text)
        {
            if (!UiAlive() || string.IsNullOrEmpty(text))
                return;
            AppendSpeaker("ArcAI 提示", Color.Gray);
            _chatBox.SelectionColor = SystemColors.GrayText;
            _chatBox.SelectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f);
            _chatBox.SelectionBackColor = Color.FromArgb(255, 251, 235);
            _chatBox.AppendText(text.TrimEnd() + Environment.NewLine + Environment.NewLine);
            _chatBox.SelectionBackColor = Color.White;
            ScrollChat();
        }

        private void AppendTool(string speaker, string text, bool ok)
        {
            if (!UiAlive() || string.IsNullOrEmpty(text))
                return;
            AppendSpeaker(speaker, ok ? Color.SeaGreen : Color.Firebrick);
            _chatBox.SelectionColor = ok ? Color.DarkGreen : Color.DarkRed;
            _chatBox.SelectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f);
            _chatBox.SelectionBackColor = ok
                ? Color.FromArgb(238, 250, 242)
                : Color.FromArgb(255, 240, 240);
            _chatBox.AppendText(text.TrimEnd() + Environment.NewLine + Environment.NewLine);
            _chatBox.SelectionBackColor = Color.White;
            if (!ok)
                _statusLabel.ForeColor = Color.Firebrick;
            ScrollChat();
        }

        private void AppendSpeaker(string speaker, Color color)
        {
            if (!UiAlive())
                return;
            _chatBox.SelectionStart = _chatBox.TextLength;
            _chatBox.SelectionLength = 0;
            _chatBox.SelectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f);
            _chatBox.SelectionColor = Color.Silver;
            _chatBox.AppendText(DateTime.Now.ToString("HH:mm") + "  ");
            _chatBox.SelectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold);
            _chatBox.SelectionColor = color;
            _chatBox.AppendText(speaker + Environment.NewLine);
            _chatBox.SelectionColor = Color.Black;
        }

        private void AppendCodeBlock(string code)
        {
            if (!UiAlive() || string.IsNullOrEmpty(code))
                return;
            AppendSpeaker("代码", Color.DarkSlateBlue);
            _chatBox.SelectionFont = new Font("Consolas", 9f);
            _chatBox.SelectionBackColor = Color.FromArgb(245, 245, 245);
            _chatBox.SelectionColor = Color.Black;
            _chatBox.AppendText(Limit(code, 12000) + Environment.NewLine + Environment.NewLine);
            _chatBox.SelectionBackColor = Color.White;
            _chatBox.SelectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f);
            _chatBox.SelectionColor = Color.Black;
            ScrollChat();
        }

        private void ScrollChat()
        {
            if (!UiAlive() || !_autoScroll)
                return;
            _chatBox.SelectionStart = _chatBox.TextLength;
            _chatBox.SelectionLength = 0;
            _chatBox.ScrollToCaret();
            if (_chatBox.TextLength > 500000)
            {
                string keep = _chatBox.Text.Substring(_chatBox.TextLength - 300000);
                _chatBox.Text = "[较早的对话已折叠]" + Environment.NewLine + keep;
                _chatBox.SelectionStart = _chatBox.TextLength;
                _chatBox.ScrollToCaret();
            }
        }

        private void AppendHistory(string request, string code, bool ok, string error)
        {
            try
            {
                AppPaths.Ensure();
                var entry = new JObject();
                entry["time"] = DateTime.Now.ToString("s");
                entry["request"] = request ?? string.Empty;
                entry["code"] = Limit(code, 150000);
                entry["ok"] = ok;
                entry["error"] = Limit(error, 8000);
                File.AppendAllText(AppPaths.History,
                    entry.ToString(Formatting.None) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("Could not append history.", ex);
            }
        }

        private List<ChatMessage> TrimHistory(List<ChatMessage> history)
        {
            var result = new List<ChatMessage>();
            if (history == null || history.Count == 0)
                return result;
            int maxTokens = PromptBuilder.HistoryTokenLimit(
                _settings.ContextBudgetTokens, _settings.MaxTokens);
            double tokens = 0;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                ChatMessage message = history[i];
                double size = PromptBuilder.EstimateTokens(message.Content);
                if (result.Count > 0 && tokens + size > maxTokens)
                    break;
                result.Insert(0, message);
                tokens += size;
            }
            return result;
        }

        private void TrimHistoryInPlace()
        {
            int maxTokens = PromptBuilder.HistoryTokenLimit(
                _settings.ContextBudgetTokens, _settings.MaxTokens);
            while (_history.Count > 2 && HistoryTokens() > maxTokens)
                _history.RemoveAt(0);
            while (_history.Count > 2000)
                _history.RemoveAt(0);
        }

        private double HistoryTokens()
        {
            double total = 0;
            foreach (ChatMessage message in _history)
                total += PromptBuilder.EstimateTokens(message.Content);
            return total;
        }

        private static string Limit(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max) + "\n…（已截断）";
        }

        private bool UiAlive()
        {
            return !_disposed && !IsDisposed && _chatBox != null && !_chatBox.IsDisposed;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_disposed || IsDisposed)
                return;
            if (Visible)
            {
                UpdateProviderLabel();
                UpdateSessionSummary();
                if (IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke(new Action(delegate { if (!_disposed) _inputBox.Focus(); }));
                    }
                    catch
                    {
                        // The dockable window can be destroyed between the check and
                        // BeginInvoke; this is not an error worth surfacing.
                    }
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_disposed)
                return;
            UpdateProviderLabel();
            UpdateSessionSummary();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                // Cancel in-flight work; the async methods dispose their own CTS in
                // their finally blocks. Do not dispose here: the continuation may
                // still be unwinding on the UI thread.
                if (_requestCts != null)
                {
                    try { _requestCts.Cancel(); } catch { }
                }
                if (_runCts != null)
                {
                    try { _runCts.Cancel(); } catch { }
                }
                if (_chatMenu != null)
                    _chatMenu.Dispose();
                if (_toolTip != null)
                    _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
