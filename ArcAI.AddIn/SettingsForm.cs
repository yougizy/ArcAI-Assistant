using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArcAI.AddIn
{
    /// <summary>API / model / execution settings. Saves the key only through DPAPI.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly ComboBox _provider;
        private readonly TextBox _baseUrl;
        private readonly TextBox _model;
        private readonly TextBox _apiKey;
        private readonly CheckBox _showKey;
        private readonly CheckBox _rememberKey;
        private readonly CheckBox _verifySsl;
        private readonly NumericUpDown _temperature;
        private readonly ComboBox _thinking;
        private readonly NumericUpDown _timeout;
        private readonly NumericUpDown _runTimeout;
        private readonly NumericUpDown _maxTokens;
        private readonly NumericUpDown _contextBudget;
        private readonly CheckBox _sendContext;
        private readonly CheckBox _confirmRun;
        private readonly CheckBox _autoRunInProc;
        private readonly TextBox _pythonPath;
        private readonly Label _status;
        private readonly Button _testButton;
        private string _lastPreset = string.Empty;
        private bool _testing;
        private volatile bool _closed;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
            Text = "ArcAI 设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(620, 600);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Padding = new Padding(12);
            grid.ColumnCount = 2;
            grid.RowCount = 15;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 14; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(grid);

            AddLabel(grid, 0, "服务商");
            _provider = new ComboBox();
            _provider.DropDownStyle = ComboBoxStyle.DropDownList;
            _provider.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            foreach (ProviderPreset preset in AppSettings.Presets)
                _provider.Items.Add(preset.Name);
            _provider.SelectedItem = _settings.Provider;
            if (_provider.SelectedIndex < 0)
            {
                int custom = _provider.Items.IndexOf("自定义");
                _provider.SelectedIndex = custom >= 0 ? custom : 0;
            }
            _lastPreset = _provider.SelectedItem as string ?? string.Empty;
            _provider.SelectedIndexChanged += ProviderChanged;
            grid.Controls.Add(_provider, 1, 0);

            AddLabel(grid, 1, "API Base URL");
            _baseUrl = new TextBox();
            _baseUrl.Text = _settings.BaseUrl;
            _baseUrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            grid.Controls.Add(_baseUrl, 1, 1);

            AddLabel(grid, 2, "模型名称");
            _model = new TextBox();
            _model.Text = _settings.Model;
            _model.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            grid.Controls.Add(_model, 1, 2);

            AddLabel(grid, 3, "API Key");
            var keyPanel = new TableLayoutPanel();
            keyPanel.Dock = DockStyle.Fill;
            keyPanel.ColumnCount = 2;
            keyPanel.RowCount = 1;
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _apiKey = new TextBox();
            _apiKey.UseSystemPasswordChar = true;
            _apiKey.Text = _settings.ApiKey ?? string.Empty;
            _apiKey.Dock = DockStyle.Fill;
            _apiKey.Margin = new Padding(0, 3, 6, 3);
            _showKey = new CheckBox();
            _showKey.Text = "显示";
            _showKey.AutoSize = true;
            _showKey.Anchor = AnchorStyles.Left;
            _showKey.CheckedChanged += delegate { _apiKey.UseSystemPasswordChar = !_showKey.Checked; };
            keyPanel.Controls.Add(_apiKey, 0, 0);
            keyPanel.Controls.Add(_showKey, 1, 0);
            grid.Controls.Add(keyPanel, 1, 3);

            AddLabel(grid, 4, "安全");
            var securityPanel = new FlowLayoutPanel();
            securityPanel.Dock = DockStyle.Fill;
            securityPanel.FlowDirection = FlowDirection.LeftToRight;
            securityPanel.WrapContents = false;
            _rememberKey = new CheckBox();
            _rememberKey.Text = "记住 Key（DPAPI 加密）";
            _rememberKey.AutoSize = true;
            _rememberKey.Checked = _settings.RememberApiKey;
            _verifySsl = new CheckBox();
            _verifySsl.Text = "验证 SSL 证书";
            _verifySsl.AutoSize = true;
            _verifySsl.Checked = _settings.VerifySsl;
            securityPanel.Controls.Add(_rememberKey);
            securityPanel.Controls.Add(_verifySsl);
            grid.Controls.Add(securityPanel, 1, 4);

            AddLabel(grid, 5, "Temperature");
            _temperature = new NumericUpDown();
            _temperature.DecimalPlaces = 2;
            _temperature.Increment = 0.1M;
            _temperature.Minimum = 0M;
            _temperature.Maximum = 2M;
            _temperature.Value = (decimal)Math.Max(0.0, Math.Min(2.0, _settings.Temperature));
            _temperature.Width = 100;
            grid.Controls.Add(_temperature, 1, 5);

            AddLabel(grid, 6, "请求超时（秒）");
            _timeout = new NumericUpDown();
            _timeout.Minimum = 20;
            _timeout.Maximum = 3600;
            _timeout.Value = Math.Max(20, Math.Min(3600, _settings.TimeoutSeconds));
            _timeout.Width = 100;
            grid.Controls.Add(_timeout, 1, 6);

            AddLabel(grid, 7, "执行超时（秒）");
            _runTimeout = new NumericUpDown();
            _runTimeout.Minimum = 30;
            _runTimeout.Maximum = 14400;
            _runTimeout.Value = Math.Max(30, Math.Min(14400, _settings.RunTimeoutSeconds));
            _runTimeout.Width = 100;
            grid.Controls.Add(_runTimeout, 1, 7);

            AddLabel(grid, 8, "tokens 预算");
            var tokenPanel = new FlowLayoutPanel();
            tokenPanel.Dock = DockStyle.Fill;
            tokenPanel.FlowDirection = FlowDirection.LeftToRight;
            tokenPanel.WrapContents = false;
            _maxTokens = new NumericUpDown();
            _maxTokens.Minimum = 256;
            _maxTokens.Maximum = 32768;
            _maxTokens.Increment = 256;
            _maxTokens.Value = Math.Max(256, Math.Min(32768, _settings.MaxTokens));
            _maxTokens.Width = 100;
            tokenPanel.Controls.Add(new Label
            {
                Text = "输出",
                AutoSize = true,
                Margin = new Padding(0, 8, 4, 0)
            });
            tokenPanel.Controls.Add(_maxTokens);
            _contextBudget = new NumericUpDown();
            _contextBudget.Minimum = 8000;
            _contextBudget.Maximum = 1000000;
            _contextBudget.Increment = 10000;
            _contextBudget.ThousandsSeparator = true;
            _contextBudget.Value = Math.Max(8000, Math.Min(1000000, _settings.ContextBudgetTokens));
            _contextBudget.Width = 120;
            tokenPanel.Controls.Add(new Label
            {
                Text = "上下文",
                AutoSize = true,
                Margin = new Padding(12, 8, 4, 0)
            });
            tokenPanel.Controls.Add(_contextBudget);
            grid.Controls.Add(tokenPanel, 1, 8);

            AddLabel(grid, 9, "思考挡位");
            _thinking = new ComboBox();
            _thinking.DropDownStyle = ComboBoxStyle.DropDownList;
            _thinking.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _thinking.Items.AddRange(new object[] { "自动", "低", "中", "高" });
            _thinking.SelectedItem = _settings.ThinkingLevel;
            if (_thinking.SelectedIndex < 0)
                _thinking.SelectedIndex = 0;
            grid.Controls.Add(_thinking, 1, 9);

            AddLabel(grid, 10, "ArcGIS Python 2.7");
            var pythonPanel = new TableLayoutPanel();
            pythonPanel.Dock = DockStyle.Fill;
            pythonPanel.ColumnCount = 2;
            pythonPanel.RowCount = 1;
            pythonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pythonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pythonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _pythonPath = new TextBox();
            _pythonPath.Text = _settings.PythonPath ?? string.Empty;
            _pythonPath.Dock = DockStyle.Fill;
            _pythonPath.Margin = new Padding(0, 3, 6, 3);
            var browse = new Button();
            browse.Text = "浏览…";
            browse.AutoSize = true;
            browse.Margin = new Padding(0, 1, 0, 1);
            browse.Click += BrowsePython;
            pythonPanel.Controls.Add(_pythonPath, 0, 0);
            pythonPanel.Controls.Add(browse, 1, 0);
            grid.Controls.Add(pythonPanel, 1, 10);

            AddLabel(grid, 11, "行为");
            var behavior = new FlowLayoutPanel();
            behavior.Dock = DockStyle.Fill;
            behavior.FlowDirection = FlowDirection.LeftToRight;
            behavior.WrapContents = false;
            _sendContext = new CheckBox();
            _sendContext.Text = "发送当前地图上下文";
            _sendContext.AutoSize = true;
            _sendContext.Checked = _settings.SendContext;
            _confirmRun = new CheckBox();
            _confirmRun.Text = "运行前确认";
            _confirmRun.AutoSize = true;
            _confirmRun.Checked = _settings.ConfirmBeforeRun;
            _autoRunInProc = new CheckBox();
            _autoRunInProc.Text = "自动运行用内运行";
            _autoRunInProc.AutoSize = true;
            _autoRunInProc.Checked = _settings.AutoRunInProc;
            behavior.Controls.Add(_sendContext);
            behavior.Controls.Add(_confirmRun);
            behavior.Controls.Add(_autoRunInProc);
            grid.Controls.Add(behavior, 1, 11);

            _status = new Label();
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.ForeColor = SystemColors.GrayText;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(_status, 0, 12);
            grid.SetColumnSpan(_status, 2);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            var ok = new Button();
            ok.Text = "确定";
            ok.Width = 88;
            ok.DialogResult = DialogResult.OK;
            ok.Click += SaveAndClose;
            var cancel = new Button();
            cancel.Text = "取消";
            cancel.Width = 88;
            cancel.DialogResult = DialogResult.Cancel;
            _testButton = new Button();
            _testButton.Text = "测试连接";
            _testButton.Width = 100;
            _testButton.Click += TestConnection;
            var diagnose = new Button();
            diagnose.Text = "网络诊断";
            diagnose.Width = 100;
            diagnose.Click += Diagnose;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_testButton);
            buttons.Controls.Add(diagnose);
            grid.Controls.Add(buttons, 0, 13);
            grid.SetColumnSpan(buttons, 2);

            var hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.ForeColor = SystemColors.GrayText;
            hint.Text = "提示：Base URL 通常以 /v1 结尾；程序会自动追加 /chat/completions。\n"
                + "思考挡位：自动不发送额外参数；低/中/高会发送 reasoning_effort（OpenAI 兼容）或 enable_thinking/thinking_budget（通义千问）；不支持该参数的模型可能忽略或报错。\n"
                + "上下文预算默认 1,000,000 tokens，仅对支持相应上下文窗口的模型有效；超限时服务商会报错，可调低。\n"
                + "API Key 使用当前 Windows 用户 DPAPI 加密，不能跨电脑/用户复制；每台电脑需重新填写。\n"
                + "其他电脑连不上 API 时，先点“网络诊断”检查 DNS/TCP/TLS/代理；公司代理可在 Windows Internet 选项中配置。\n"
                + "地图上下文包含图层名称、数据源路径和坐标系；只有勾选发送时才会传给模型。";
            grid.Controls.Add(hint, 0, 14);
            grid.SetColumnSpan(hint, 2);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static void AddLabel(TableLayoutPanel grid, int row, string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(label, 0, row);
        }

        private void ProviderChanged(object sender, EventArgs e)
        {
            if (_baseUrl == null || _model == null)
                return; // the initial SelectedItem assignment happens before these fields exist
            string selected = _provider.SelectedItem as string ?? string.Empty;
            ProviderPreset preset = null;
            foreach (ProviderPreset item in AppSettings.Presets)
            {
                if (string.Equals(item.Name, selected, StringComparison.OrdinalIgnoreCase))
                {
                    preset = item;
                    break;
                }
            }
            if (preset == null || preset.Name == "自定义")
            {
                _lastPreset = selected;
                return;
            }

            ProviderPreset previous = null;
            foreach (ProviderPreset item in AppSettings.Presets)
            {
                if (string.Equals(item.Name, _lastPreset, StringComparison.OrdinalIgnoreCase))
                {
                    previous = item;
                    break;
                }
            }
            bool untouched = string.IsNullOrWhiteSpace(_baseUrl.Text)
                || (previous != null && previous.BaseUrl == _baseUrl.Text)
                || _baseUrl.Text == _settings.BaseUrl;
            bool modelUntouched = string.IsNullOrWhiteSpace(_model.Text)
                || (previous != null && previous.Model == _model.Text)
                || _model.Text == _settings.Model;
            if (untouched && !string.IsNullOrEmpty(preset.BaseUrl))
                _baseUrl.Text = preset.BaseUrl;
            if (modelUntouched && !string.IsNullOrEmpty(preset.Model))
                _model.Text = preset.Model;
            _lastPreset = selected;
        }

        private void BrowsePython(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "python.exe|python.exe|可执行文件|*.exe|所有文件|*.*";
                dialog.Title = "选择 ArcGIS Python 2.7 的 python.exe";
                string current = _pythonPath.Text.Trim();
                if (current.Length > 0)
                {
                    try { dialog.InitialDirectory = Path.GetDirectoryName(current); }
                    catch { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _pythonPath.Text = dialog.FileName;
            }
        }

        private async void Diagnose(object sender, EventArgs e)
        {
            if (_closed)
                return;
            string url = _baseUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                _status.Text = "请先填写 Base URL。";
                return;
            }
            _status.Text = "正在诊断网络…";
            try
            {
                string report = await NetworkDiagnostics.RunAsync(url);
                if (_closed || IsDisposed)
                    return;
                _status.Text = "网络诊断完成";
                MessageBox.Show(this, report, "ArcAI 网络诊断",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!_closed && !IsDisposed)
                    _status.Text = "诊断失败：" + ex.Message;
            }
        }

        private async void TestConnection(object sender, EventArgs e)
        {
            if (_testing)
                return;
            AppSettings test = Collect();
            if (string.IsNullOrWhiteSpace(test.BaseUrl))
            {
                _status.Text = "请先填写 Base URL。";
                return;
            }
            if (string.IsNullOrWhiteSpace(test.ApiKey) && !IsLocal(test.BaseUrl))
            {
                _status.Text = "请先填写 API Key（本地 Ollama 可留空）。";
                return;
            }

            _testing = true;
            _testButton.Enabled = false;
            _status.Text = "正在测试连接…";
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var messages = new List<ChatMessage>
                    {
                        new ChatMessage("user", "请只回复 OK。")
                    };
                    string reply = await LlmClient.ChatAsync(test, messages, cts.Token);
                    if (_closed || IsDisposed)
                        return;
                    _status.Text = "连接成功：" + (reply.Length > 80 ? reply.Substring(0, 80) : reply);
                }
            }
            catch (Exception ex)
            {
                if (!_closed && !IsDisposed)
                    _status.Text = "连接失败：" + ex.Message;
            }
            finally
            {
                _testing = false;
                if (!_closed && !IsDisposed)
                    _testButton.Enabled = true;
            }
        }

        private static bool IsLocal(string url)
        {
            return url.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private AppSettings Collect()
        {
            var result = new AppSettings();
            result.Provider = _provider.SelectedItem as string ?? "自定义";
            result.BaseUrl = _baseUrl.Text.Trim();
            result.Model = _model.Text.Trim();
            result.ApiKey = _apiKey.Text.Trim();
            result.RememberApiKey = _rememberKey.Checked;
            result.VerifySsl = _verifySsl.Checked;
            result.Temperature = (double)_temperature.Value;
            result.ThinkingLevel = _thinking.SelectedItem as string ?? "自动";
            result.TimeoutSeconds = (int)_timeout.Value;
            result.RunTimeoutSeconds = (int)_runTimeout.Value;
            result.MaxTokens = (int)_maxTokens.Value;
            result.ContextBudgetTokens = (int)_contextBudget.Value;
            result.SendContext = _sendContext.Checked;
            result.ConfirmBeforeRun = _confirmRun.Checked;
            result.AutoRunInProc = _autoRunInProc.Checked;
            result.PythonPath = _pythonPath.Text.Trim();
            result.Normalize();
            return result;
        }

        private void SaveAndClose(object sender, EventArgs e)
        {            if (string.IsNullOrWhiteSpace(_baseUrl.Text))
            {
                MessageBox.Show(this, "请填写 API Base URL。", "ArcAI 设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (string.IsNullOrWhiteSpace(_model.Text))
            {
                MessageBox.Show(this, "请填写模型名称。", "ArcAI 设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            Result = Collect();
            try
            {
                Result.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存设置失败：" + ex.Message, "ArcAI 设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.None;
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _closed = true;
            base.Dispose(disposing);
        }
    }
}
