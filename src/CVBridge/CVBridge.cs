using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CVBridge
{
    internal sealed class BridgeSettings
    {
        public string Host = "";
        public string User = "";
        public string Port = "22";
        public string KeyPath = DefaultKeyPath();
        public string RemoteDir = ".server_clipboard_bridge";

        public string Target
        {
            get { return User + "@" + Host; }
        }

        private static string DefaultKeyPath()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string ed25519 = Path.Combine(profile, ".ssh", "id_ed25519");
            if (File.Exists(ed25519)) return ed25519;

            string rsa = Path.Combine(profile, ".ssh", "id_rsa");
            if (File.Exists(rsa)) return rsa;

            return Path.Combine(profile, ".ssh", "id_ed25519");
        }
    }

    internal sealed class NativeResult
    {
        public int ExitCode;
        public string Output;
    }

    internal static class NativeRunner
    {
        public static NativeResult Run(string fileName, IList<string> args, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = JoinArguments(args);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                StringBuilder output = new StringBuilder();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException(fileName + " timed out.");
                }

                process.WaitForExit();
                NativeResult result = new NativeResult();
                result.ExitCode = process.ExitCode;
                result.Output = output.ToString();
                return result;
            }
        }

        private static string JoinArguments(IList<string> args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteArg(args[i]));
            }
            return sb.ToString();
        }

        private static string QuoteArg(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length == 0) return "\"\"";

            bool needsQuotes = false;
            for (int i = 0; i < arg.Length; i++)
            {
                char c = arg[i];
                if (char.IsWhiteSpace(c) || c == '"')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes) return arg;

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            for (int i = 0; i < arg.Length; i++)
            {
                char c = arg[i];
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                }
                else
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                    sb.Append(c);
                }
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }

    internal sealed class LogForm : Form
    {
        private readonly string logPath;
        private readonly TextBox logText;

        public LogForm(string path)
        {
            logPath = path;
            Text = "CV Bridge Logs";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 560);
            MinimumSize = new Size(720, 420);
            Font = new Font("Segoe UI", 9F);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Top;
            actions.Height = 46;
            actions.Padding = new Padding(10, 8, 10, 8);
            Controls.Add(actions);

            Button refresh = new Button();
            refresh.Text = "刷新";
            refresh.Location = new Point(10, 8);
            refresh.Size = new Size(78, 30);
            refresh.Click += delegate { LoadLog(); };
            actions.Controls.Add(refresh);

            Button copy = new Button();
            copy.Text = "复制";
            copy.Location = new Point(98, 8);
            copy.Size = new Size(78, 30);
            copy.Click += delegate
            {
                if (logText.TextLength > 0) Clipboard.SetText(logText.Text);
            };
            actions.Controls.Add(copy);

            Button clear = new Button();
            clear.Text = "清空";
            clear.Location = new Point(186, 8);
            clear.Size = new Size(78, 30);
            clear.Click += delegate
            {
                if (MessageBox.Show(this, "确定清空日志吗？", "CV Bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.WriteAllText(logPath, "", Encoding.UTF8);
                    LoadLog();
                }
            };
            actions.Controls.Add(clear);

            Button openFolder = new Button();
            openFolder.Text = "打开目录";
            openFolder.Location = new Point(274, 8);
            openFolder.Size = new Size(90, 30);
            openFolder.Click += delegate
            {
                string dir = Path.GetDirectoryName(logPath);
                if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir)) Process.Start("explorer.exe", dir);
            };
            actions.Controls.Add(openFolder);

            logText = new TextBox();
            logText.Dock = DockStyle.Fill;
            logText.Multiline = true;
            logText.ReadOnly = true;
            logText.ScrollBars = ScrollBars.Both;
            logText.WordWrap = false;
            logText.Font = new Font("Consolas", 9.5F);
            Controls.Add(logText);
            logText.BringToFront();

            LoadLog();
        }

        private void LoadLog()
        {
            if (File.Exists(logPath)) logText.Text = File.ReadAllText(logPath, Encoding.UTF8);
            else logText.Text = "";
            logText.SelectionStart = logText.TextLength;
            logText.ScrollToCaret();
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string configPath;
        private readonly string logPath;
        private readonly object logLock = new object();
        private readonly BridgeSettings settings = new BridgeSettings();

        private TextBox hostBox;
        private TextBox userBox;
        private TextBox portBox;
        private TextBox keyBox;
        private TextBox remoteDirBox;
        private TextBox textBox;
        private TextBox localFileBox;
        private TextBox remoteFileBox;
        private CheckBox recursiveBox;
        private ToolStripStatusLabel statusLabel;
        private ListView historyList;
        private TextBox logBox;
        private TextBox liveLogBox;

        public MainForm()
        {
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CVBridge.ini");
            logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CVBridge.log");
            WriteLog("APP START");
            LoadSettings();
            BuildUi();
            ApplySettingsToUi();
            FormClosing += delegate { WriteLog("APP EXIT"); };
        }

        private void BuildUi()
        {
            Text = "CV Bridge";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1060, 760);
            Size = new Size(1180, 800);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(239, 243, 248);

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 72;
            header.BackColor = Color.FromArgb(24, 32, 42);
            Controls.Add(header);

            Label title = new Label();
            title.Text = "CV Bridge";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(20, 17);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "SSH clipboard and file bridge";
            subtitle.ForeColor = Color.FromArgb(176, 194, 211);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(156, 33);
            header.Controls.Add(subtitle);

            Label version = new Label();
            version.Text = "ready";
            version.ForeColor = Color.FromArgb(126, 160, 184);
            version.AutoSize = true;
            version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            version.Location = new Point(ClientSize.Width - 72, 34);
            header.Controls.Add(version);

            Panel root = new Panel();
            root.Location = new Point(0, header.Height);
            root.Size = new Size(ClientSize.Width, ClientSize.Height - header.Height - 24);
            root.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            root.Padding = new Padding(14);
            Controls.Add(root);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158F));
            root.Controls.Add(layout);

            Panel conn = new Panel();
            conn.Dock = DockStyle.Fill;
            conn.BackColor = Color.White;
            conn.Padding = new Padding(12);
            conn.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(conn, 0, 0);

            hostBox = AddLabeledBox(conn, "服务器", 12, 18, 168);
            userBox = AddLabeledBox(conn, "用户", 224, 18, 110);
            portBox = AddLabeledBox(conn, "端口", 378, 18, 58);
            remoteDirBox = AddLabeledBox(conn, "远程目录", 480, 18, 190);

            keyBox = AddLabeledBox(conn, "密钥", 12, 72, 520);
            Button browseKey = AddButton(conn, "浏览", 580, 69, 72, NeutralColor());
            browseKey.Click += delegate { BrowseKey(); };

            Button testButton = AddButton(conn, "测试连接", 700, 18, 94, AccentColor());
            testButton.Click += delegate { RunUiAction("测试连接", TestConnection); };

            Button saveButton = AddButton(conn, "保存配置", 804, 18, 94, Color.FromArgb(83, 95, 107));
            saveButton.Click += delegate { RunUiAction("保存配置", SaveSettingsFromUi); };

            Button openButton = AddButton(conn, "打开密钥目录", 700, 69, 140, Color.FromArgb(83, 95, 107));
            openButton.Click += delegate { OpenKeyFolder(); };

            Button logsButton = AddButton(conn, "完整日志", 850, 69, 86, Color.FromArgb(83, 95, 107));
            logsButton.Click += delegate { ShowLogs(); };

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(tabs, 0, 1);

            TabPage clipTab = new TabPage("文本桥接");
            clipTab.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(clipTab);
            BuildClipboardTab(clipTab);

            TabPage fileTab = new TabPage("文件传输");
            fileTab.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(fileTab);
            BuildFileTab(fileTab);

            Panel liveLogs = BuildLiveLogPanel();
            layout.Controls.Add(liveLogs, 0, 2);

            StatusStrip status = new StatusStrip();
            statusLabel = new LabelStripItem();
            statusLabel.Text = "就绪";
            status.Items.Add(statusLabel);
            Controls.Add(status);
            LoadRecentLogIntoPanel();
        }

        private void BuildClipboardTab(TabPage tab)
        {
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(10);
            tab.Controls.Add(left);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Bottom;
            actions.Height = 52;
            left.Controls.Add(actions);

            textBox = new TextBox();
            textBox.Multiline = true;
            textBox.ScrollBars = ScrollBars.Both;
            textBox.AcceptsReturn = true;
            textBox.AcceptsTab = true;
            textBox.WordWrap = false;
            textBox.Dock = DockStyle.Fill;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = new Font("Consolas", 10F);
            left.Controls.Add(textBox);

            AddButton(actions, "粘贴本机", 0, 10, 88, NeutralColor()).Click += delegate
            {
                if (Clipboard.ContainsText()) textBox.Text = Clipboard.GetText();
            };
            AddButton(actions, "复制本机", 98, 10, 88, NeutralColor()).Click += delegate
            {
                Clipboard.SetText(textBox.Text);
                SetStatus("已复制到本机剪贴板");
            };
            AddButton(actions, "发送到服务器", 208, 10, 118, AccentColor()).Click += delegate
            {
                RunUiAction("发送到服务器", SendText);
            };
            AddButton(actions, "获取服务器", 336, 10, 104, AccentColor()).Click += delegate
            {
                RunUiAction("获取服务器", FetchText);
            };
            AddButton(actions, "清空", 450, 10, 68, Color.FromArgb(126, 89, 89)).Click += delegate
            {
                textBox.Clear();
            };

            Panel right = new Panel();
            right.Dock = DockStyle.Right;
            right.Width = 340;
            right.Padding = new Padding(10);
            tab.Controls.Add(right);
            right.BringToFront();

            Label historyTitle = new Label();
            historyTitle.Text = "历史记录";
            historyTitle.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            historyTitle.Dock = DockStyle.Top;
            historyTitle.Height = 28;
            right.Controls.Add(historyTitle);

            Panel histActions = new Panel();
            histActions.Dock = DockStyle.Bottom;
            histActions.Height = 52;
            right.Controls.Add(histActions);

            historyList = new ListView();
            historyList.Dock = DockStyle.Fill;
            historyList.View = View.Details;
            historyList.FullRowSelect = true;
            historyList.GridLines = false;
            historyList.BorderStyle = BorderStyle.FixedSingle;
            historyList.Columns.Add("时间", 130);
            historyList.Columns.Add("字节", 58);
            historyList.Columns.Add("预览", 125);
            historyList.DoubleClick += delegate { RunUiAction("加载历史", LoadSelectedHistory); };
            right.Controls.Add(historyList);
            historyList.BringToFront();

            AddButton(histActions, "刷新", 0, 10, 74, NeutralColor()).Click += delegate
            {
                RunUiAction("刷新历史", RefreshHistory);
            };
            AddButton(histActions, "加载", 84, 10, 74, AccentColor()).Click += delegate
            {
                RunUiAction("加载历史", LoadSelectedHistory);
            };
            AddButton(histActions, "复制", 168, 10, 74, NeutralColor()).Click += delegate
            {
                RunUiAction("复制历史", CopySelectedHistory);
            };
        }

        private void BuildFileTab(TabPage tab)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(14);
            tab.Controls.Add(panel);

            localFileBox = AddLabeledBox(panel, "本地路径", 12, 24, 610);
            Button fileButton = AddButton(panel, "选择文件", 675, 21, 90, NeutralColor());
            fileButton.Click += delegate { BrowseLocalFile(); };
            Button folderButton = AddButton(panel, "选择文件夹", 775, 21, 100, NeutralColor());
            folderButton.Click += delegate { BrowseLocalFolder(); };

            remoteFileBox = AddLabeledBox(panel, "服务器路径", 12, 78, 610);
            recursiveBox = new CheckBox();
            recursiveBox.Text = "目录递归";
            recursiveBox.Location = new Point(675, 80);
            recursiveBox.Size = new Size(100, 24);
            panel.Controls.Add(recursiveBox);

            Button upload = AddButton(panel, "上传", 110, 130, 90, AccentColor());
            upload.Click += delegate { RunUiAction("上传文件", UploadFile); };
            Button download = AddButton(panel, "下载", 214, 130, 90, AccentColor());
            download.Click += delegate { RunUiAction("下载文件", DownloadFile); };

            logBox = new TextBox();
            logBox.Location = new Point(12, 188);
            logBox.Size = new Size(900, 310);
            logBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.Font = new Font("Consolas", 9.5F);
            panel.Controls.Add(logBox);
        }

        private Panel BuildLiveLogPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(15, 23, 42);
            panel.Padding = new Padding(12, 8, 12, 12);
            panel.Margin = new Padding(0);

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 34;
            header.BackColor = Color.FromArgb(15, 23, 42);
            panel.Controls.Add(header);

            Label title = new Label();
            title.Text = "实时日志";
            title.ForeColor = Color.FromArgb(226, 232, 240);
            title.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            title.Location = new Point(0, 8);
            title.Size = new Size(120, 22);
            header.Controls.Add(title);

            Label hint = new Label();
            hint.Text = "启动、连接、传输和错误会显示在这里";
            hint.ForeColor = Color.FromArgb(148, 163, 184);
            hint.Location = new Point(82, 10);
            hint.Size = new Size(330, 20);
            header.Controls.Add(hint);

            Button refresh = AddSmallLogButton(header, "刷新", 612);
            refresh.Click += delegate { LoadRecentLogIntoPanel(); };

            Button copy = AddSmallLogButton(header, "复制", 678);
            copy.Click += delegate
            {
                if (liveLogBox != null && liveLogBox.TextLength > 0) Clipboard.SetText(liveLogBox.Text);
            };

            Button clear = AddSmallLogButton(header, "清空", 744);
            clear.Click += delegate
            {
                if (MessageBox.Show(this, "确定清空日志吗？", "CV Bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.WriteAllText(logPath, "", Encoding.UTF8);
                    LoadRecentLogIntoPanel();
                    WriteLog("LOG CLEARED");
                }
            };

            Button full = AddSmallLogButton(header, "完整日志", 810);
            full.Size = new Size(82, 25);
            full.Click += delegate { ShowLogs(); };

            liveLogBox = new TextBox();
            liveLogBox.Dock = DockStyle.Fill;
            liveLogBox.Multiline = true;
            liveLogBox.ReadOnly = true;
            liveLogBox.ScrollBars = ScrollBars.Vertical;
            liveLogBox.WordWrap = false;
            liveLogBox.BorderStyle = BorderStyle.None;
            liveLogBox.BackColor = Color.FromArgb(15, 23, 42);
            liveLogBox.ForeColor = Color.FromArgb(203, 213, 225);
            liveLogBox.Font = new Font("Consolas", 9F);
            panel.Controls.Add(liveLogBox);
            liveLogBox.BringToFront();

            return panel;
        }

        private Button AddSmallLogButton(Control parent, string text, int x)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, 5);
            b.Size = new Size(58, 25);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
            b.BackColor = Color.FromArgb(30, 41, 59);
            b.ForeColor = Color.FromArgb(226, 232, 240);
            b.Cursor = Cursors.Hand;
            parent.Controls.Add(b);
            return b;
        }

        private TextBox AddLabeledBox(Control parent, string label, int x, int y, int width)
        {
            Label l = new Label();
            l.Text = label;
            l.Location = new Point(x, y - 17);
            l.Size = new Size(width, 17);
            l.ForeColor = Color.FromArgb(71, 84, 99);
            parent.Controls.Add(l);

            TextBox box = new TextBox();
            box.Location = new Point(x, y);
            box.Size = new Size(width, 25);
            box.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(box);
            return box;
        }

        private Button AddButton(Control parent, string text, int x, int y, int width, Color color)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(width, 32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Lighten(color, 18);
            b.FlatAppearance.MouseDownBackColor = Darken(color, 18);
            b.BackColor = color;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            parent.Controls.Add(b);
            return b;
        }

        private Color Lighten(Color color, int amount)
        {
            return Color.FromArgb(
                Math.Min(255, color.R + amount),
                Math.Min(255, color.G + amount),
                Math.Min(255, color.B + amount));
        }

        private Color Darken(Color color, int amount)
        {
            return Color.FromArgb(
                Math.Max(0, color.R - amount),
                Math.Max(0, color.G - amount),
                Math.Max(0, color.B - amount));
        }

        private Color AccentColor()
        {
            return Color.FromArgb(8, 126, 126);
        }

        private Color NeutralColor()
        {
            return Color.FromArgb(91, 110, 126);
        }

        private void RunUiAction(string name, Action action)
        {
            SaveSettingsFromUi();
            SetBusy(true);
            SetStatus(name + "...");
            WriteLog("ACTION START: " + name);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    action();
                    WriteLog("ACTION OK: " + name);
                    BeginInvoke(new Action(delegate { SetStatus(name + "完成"); }));
                }
                catch (Exception ex)
                {
                    WriteLog("ACTION FAIL: " + name + Environment.NewLine + ex);
                    BeginInvoke(new Action(delegate
                    {
                        SetStatus(name + "失败");
                        MessageBox.Show(this, ex.Message, "CV Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
                finally
                {
                    BeginInvoke(new Action(delegate { SetBusy(false); }));
                }
            });
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void SetStatus(string text)
        {
            statusLabel.Text = text;
        }

        private void ShowLogs()
        {
            WriteLog("OPEN LOG WINDOW");
            using (LogForm form = new LogForm(logPath))
            {
                form.ShowDialog(this);
            }
        }

        private void SaveSettingsFromUi()
        {
            settings.Host = hostBox.Text.Trim();
            settings.User = userBox.Text.Trim();
            settings.Port = portBox.Text.Trim();
            settings.KeyPath = keyBox.Text.Trim();
            settings.RemoteDir = remoteDirBox.Text.Trim();
            SaveSettings();
        }

        private void ApplySettingsToUi()
        {
            hostBox.Text = settings.Host;
            userBox.Text = settings.User;
            portBox.Text = settings.Port;
            keyBox.Text = settings.KeyPath;
            remoteDirBox.Text = settings.RemoteDir;
        }

        private void LoadSettings()
        {
            if (!File.Exists(configPath)) return;
            foreach (string rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key == "Host") settings.Host = value;
                else if (key == "User") settings.User = value;
                else if (key == "Port") settings.Port = value;
                else if (key == "KeyPath") settings.KeyPath = Environment.ExpandEnvironmentVariables(value);
                else if (key == "RemoteDir") settings.RemoteDir = value;
            }
        }

        private void SaveSettings()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Host=" + settings.Host);
            sb.AppendLine("User=" + settings.User);
            sb.AppendLine("Port=" + settings.Port);
            sb.AppendLine("KeyPath=" + settings.KeyPath);
            sb.AppendLine("RemoteDir=" + settings.RemoteDir);
            File.WriteAllText(configPath, sb.ToString(), Encoding.UTF8);
            WriteLog("CONFIG SAVED: " + settings.User + "@" + settings.Host + ":" + settings.Port + ", key=" + settings.KeyPath);
        }

        private void BrowseKey()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择 SSH 密钥";
            dialog.Filter = "SSH key|id_ed25519;id_rsa;cvb_*;*.pem;*.key|All files|*.*";
            if (File.Exists(keyBox.Text)) dialog.FileName = keyBox.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) keyBox.Text = dialog.FileName;
        }

        private void OpenKeyFolder()
        {
            string dir = Path.GetDirectoryName(keyBox.Text);
            if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start("explorer.exe", dir);
            }
        }

        private void BrowseLocalFile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "All files|*.*";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                localFileBox.Text = dialog.FileName;
                if (remoteFileBox.Text.Trim().Length == 0) remoteFileBox.Text = Path.GetFileName(dialog.FileName);
                recursiveBox.Checked = false;
            }
        }

        private void BrowseLocalFolder()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                localFileBox.Text = dialog.SelectedPath;
                if (remoteFileBox.Text.Trim().Length == 0) remoteFileBox.Text = Path.GetFileName(dialog.SelectedPath);
                recursiveBox.Checked = true;
            }
        }

        private void TestConnection()
        {
            string command = "whoami; hostname -I";
            NativeResult result = RunSsh(command, 20000);
            EnsureSuccess(result, "测试连接");
            AppendLog(result.Output.Trim());
        }

        private void SendText()
        {
            string text = GetTextThreadSafe(textBox);
            string tempFile = Path.Combine(Path.GetTempPath(), "cvbridge_clipboard_upload.txt");
            File.WriteAllText(tempFile, text, new UTF8Encoding(false));

            PrepareRemoteDir();
            string remoteClip = JoinRemote(settings.RemoteDir, "clipboard.txt");
            NativeResult upload = RunScp(new List<string> { tempFile, settings.Target + ":" + remoteClip }, false, 60000);
            EnsureSuccess(upload, "上传文本");

            string setup = RemoteDirSetup(settings.RemoteDir);
            string command = setup
                + "mkdir -p \"$dir/history/items\"; "
                + "clip=\"$dir/clipboard.txt\"; [ -f \"$clip\" ] || : > \"$clip\"; "
                + "id=$(date '+%Y%m%d_%H%M%S')_$$; item=\"$dir/history/items/$id.txt\"; cp \"$clip\" \"$item\"; "
                + "ts=$(date '+%Y-%m-%d %H:%M:%S'); bytes=$(wc -c < \"$clip\" | tr -d ' '); "
                + "preview=$(head -c 120 \"$clip\" | tr '\\r\\n\\t' '   '); "
                + "printf '%s\\t%s\\t%s\\t%s\\n' \"$id\" \"$ts\" \"$bytes\" \"$preview\" >> \"$dir/history/index.tsv\"; "
                + "tail -n 200 \"$dir/history/index.tsv\" > \"$dir/history/index.tmp\" && mv \"$dir/history/index.tmp\" \"$dir/history/index.tsv\"; "
                + DesktopClipboardWriteScript();
            NativeResult result = RunSsh(command, 30000);
            EnsureSuccess(result, "更新历史");
            RefreshHistory();
        }

        private void FetchText()
        {
            string setup = RemoteDirSetup(settings.RemoteDir);
            string command = setup
                + "mkdir -p \"$dir\"; clip=\"$dir/clipboard.txt\"; "
                + DesktopClipboardReadScript()
                + "[ -f \"$clip\" ] || : > \"$clip\"";
            NativeResult ensure = RunSsh(command, 30000);
            EnsureSuccess(ensure, "准备读取");

            string tempFile = Path.Combine(Path.GetTempPath(), "cvbridge_clipboard_download.txt");
            string remoteClip = JoinRemote(settings.RemoteDir, "clipboard.txt");
            NativeResult download = RunScp(new List<string> { settings.Target + ":" + remoteClip, tempFile }, false, 60000);
            EnsureSuccess(download, "下载文本");
            string text = File.ReadAllText(tempFile, new UTF8Encoding(false));
            BeginInvoke(new Action(delegate { textBox.Text = text; }));
        }

        private void RefreshHistory()
        {
            string command = RemoteDirSetup(settings.RemoteDir) + "index=\"$dir/history/index.tsv\"; if [ -f \"$index\" ]; then tail -n 200 \"$index\"; fi";
            NativeResult result = RunSsh(command, 30000);
            EnsureSuccess(result, "刷新历史");
            string[] lines = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Array.Reverse(lines);

            BeginInvoke(new Action(delegate
            {
                historyList.BeginUpdate();
                historyList.Items.Clear();
                foreach (string line in lines)
                {
                    string[] parts = line.Split(new[] { '\t' }, 4);
                    if (parts.Length < 4) continue;
                    ListViewItem item = new ListViewItem(parts[1]);
                    item.SubItems.Add(parts[2]);
                    item.SubItems.Add(parts[3]);
                    item.Tag = parts[0];
                    historyList.Items.Add(item);
                }
                historyList.EndUpdate();
            }));
        }

        private void LoadSelectedHistory()
        {
            string text = ReadSelectedHistory();
            BeginInvoke(new Action(delegate { textBox.Text = text; }));
        }

        private void CopySelectedHistory()
        {
            string text = ReadSelectedHistory();
            BeginInvoke(new Action(delegate
            {
                Clipboard.SetText(text);
            }));
        }

        private string ReadSelectedHistory()
        {
            string id = null;
            Invoke(new Action(delegate
            {
                if (historyList.SelectedItems.Count > 0) id = Convert.ToString(historyList.SelectedItems[0].Tag);
            }));

            if (String.IsNullOrEmpty(id)) throw new InvalidOperationException("请先选择一条历史记录。");
            if (!IsSafeId(id)) throw new InvalidOperationException("历史记录 ID 不合法。");

            string tempFile = Path.Combine(Path.GetTempPath(), "cvbridge_history_" + id + ".txt");
            string remoteItem = JoinRemote(settings.RemoteDir, "history/items/" + id + ".txt");
            NativeResult download = RunScp(new List<string> { settings.Target + ":" + remoteItem, tempFile }, false, 60000);
            EnsureSuccess(download, "下载历史");
            return File.ReadAllText(tempFile, new UTF8Encoding(false));
        }

        private bool IsSafeId(string id)
        {
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) return false;
            }
            return true;
        }

        private string SafeName(string value)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
                else sb.Append('_');
            }
            if (sb.Length == 0) sb.Append("user");
            return sb.ToString();
        }

        private void UploadFile()
        {
            string local = GetTextThreadSafe(localFileBox).Trim();
            string remote = GetTextThreadSafe(remoteFileBox).Trim();
            if (local.Length == 0 || !File.Exists(local) && !Directory.Exists(local)) throw new InvalidOperationException("本地路径不存在。");
            if (remote.Length == 0) throw new InvalidOperationException("请填写服务器路径。");

            string command = RemotePathSetup(remote) + "parent=$(dirname \"$path\"); mkdir -p \"$parent\"";
            NativeResult prep = RunSsh(command, 30000);
            EnsureSuccess(prep, "准备服务器路径");

            bool recursive = Directory.Exists(local) || GetCheckedThreadSafe(recursiveBox);
            NativeResult upload = RunScp(new List<string> { local, settings.Target + ":" + remote }, recursive, 120000);
            EnsureSuccess(upload, "上传文件");
            AppendLog("上传完成: " + local + " -> " + remote);
        }

        private void DownloadFile()
        {
            string local = GetTextThreadSafe(localFileBox).Trim();
            string remote = GetTextThreadSafe(remoteFileBox).Trim();
            if (remote.Length == 0) throw new InvalidOperationException("请填写服务器路径。");
            if (local.Length == 0) throw new InvalidOperationException("请填写本地保存路径。");

            string parent = Path.GetDirectoryName(local);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            NativeResult download = RunScp(new List<string> { settings.Target + ":" + remote, local }, GetCheckedThreadSafe(recursiveBox), 120000);
            EnsureSuccess(download, "下载文件");
            AppendLog("下载完成: " + remote + " -> " + local);
        }

        private void PrepareRemoteDir()
        {
            string command = RemoteDirSetup(settings.RemoteDir) + "mkdir -p \"$dir/history/items\"";
            NativeResult result = RunSsh(command, 30000);
            EnsureSuccess(result, "准备服务器目录");
        }

        private NativeResult RunSsh(string remoteCommand, int timeoutMs)
        {
            string id = Guid.NewGuid().ToString("N");
            string localScript = Path.Combine(Path.GetTempPath(), "cvbridge_" + id + ".sh");
            string remoteScript = "/tmp/cvbridge_" + SafeName(settings.User) + "_" + id + ".sh";
            string script = "#!/usr/bin/env bash\n"
                + "__cvbridge_self=\"$0\"\n"
                + "trap 'rm -f \"$__cvbridge_self\"' EXIT\n"
                + remoteCommand + "\n";

            File.WriteAllText(localScript, script.Replace("\r\n", "\n"), new UTF8Encoding(false));

            WriteLog("SSH SCRIPT UPLOAD: " + remoteScript);
            NativeResult upload = RunScp(new List<string> { localScript, settings.Target + ":" + remoteScript }, false, 60000);
            try { File.Delete(localScript); } catch { }
            if (upload.ExitCode != 0)
            {
                WriteLog("SSH SCRIPT UPLOAD FAIL: " + TrimForLog(upload.Output));
                return upload;
            }

            List<string> args = BaseSshArgs(false);
            args.Add(settings.Target);
            args.Add("bash " + remoteScript);
            NativeResult result = NativeRunner.Run("ssh", args, timeoutMs);
            if (result.ExitCode == 0) WriteLog("SSH OK: " + remoteScript);
            else WriteLog("SSH FAIL: " + remoteScript + Environment.NewLine + TrimForLog(result.Output));
            return result;
        }

        private NativeResult RunScp(List<string> operands, bool recursive, int timeoutMs)
        {
            List<string> args = BaseSshArgs(true);
            if (recursive) args.Add("-r");
            args.AddRange(operands);
            WriteLog("SCP START: " + SummarizeOperands(operands));
            NativeResult result = NativeRunner.Run("scp", args, timeoutMs);
            if (result.ExitCode == 0) WriteLog("SCP OK: " + SummarizeOperands(operands));
            else WriteLog("SCP FAIL: " + SummarizeOperands(operands) + Environment.NewLine + TrimForLog(result.Output));
            return result;
        }

        private string SummarizeOperands(IList<string> operands)
        {
            if (operands == null || operands.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < operands.Count; i++)
            {
                if (i > 0) sb.Append(" -> ");
                sb.Append(ShortPathForLog(operands[i]));
            }
            return sb.ToString();
        }

        private string ShortPathForLog(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            string temp = Path.GetTempPath();
            if (value.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
            {
                return "%TEMP%\\" + Path.GetFileName(value);
            }
            return value;
        }

        private string TrimForLog(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            value = value.Trim();
            if (value.Length <= 4000) return value;
            return value.Substring(0, 4000) + "...";
        }

        private List<string> BaseSshArgs(bool scp)
        {
            ValidateSettings();
            List<string> args = new List<string>();
            args.Add(scp ? "-P" : "-p");
            args.Add(settings.Port);
            args.Add("-i");
            args.Add(settings.KeyPath);
            args.Add("-o");
            args.Add("BatchMode=yes");
            args.Add("-o");
            args.Add("IdentitiesOnly=yes");
            args.Add("-o");
            args.Add("ConnectTimeout=12");
            args.Add("-o");
            args.Add("StrictHostKeyChecking=accept-new");
            return args;
        }

        private void ValidateSettings()
        {
            if (settings.Host.Length == 0) throw new InvalidOperationException("请填写服务器地址。");
            if (settings.User.Length == 0) throw new InvalidOperationException("请填写用户名。");
            if (settings.Port.Length == 0) throw new InvalidOperationException("请填写端口。");
            if (settings.KeyPath.Length == 0 || !File.Exists(settings.KeyPath)) throw new InvalidOperationException("密钥文件不存在。");
            if (settings.RemoteDir.Length == 0) settings.RemoteDir = ".server_clipboard_bridge";
        }

        private void EnsureSuccess(NativeResult result, string action)
        {
            if (result.ExitCode == 0) return;
            string output = result.Output == null ? "" : result.Output.Trim();
            if (output.Length == 0) output = "没有返回详细错误。";
            throw new InvalidOperationException(action + "失败。\r\n" + output);
        }

        private string RemoteDirSetup(string remoteDir)
        {
            string dir = NormalizeRemote(remoteDir);
            return "dir=" + QuoteSh(dir) + "; case \"$dir\" in /*) ;; \\~/*) dir=\"$HOME/${dir#~/}\" ;; *) dir=\"$HOME/$dir\" ;; esac; ";
        }

        private string RemotePathSetup(string remotePath)
        {
            string path = NormalizeRemote(remotePath);
            return "path=" + QuoteSh(path) + "; case \"$path\" in /*) ;; \\~/*) path=\"$HOME/${path#~/}\" ;; *) path=\"$HOME/$path\" ;; esac; ";
        }

        private string DesktopClipboardWriteScript()
        {
            return @"
try_gui_clipboard_write() {
  clip_file=""$1""
  cv_find_gui_env() {
    if [ -n ""${DISPLAY:-}"" ]; then
      CV_DISPLAY=""$DISPLAY""
      CV_XAUTHORITY=""${XAUTHORITY:-$HOME/.Xauthority}""
      return 0
    fi
    for envf in /proc/[0-9]*/environ; do
      [ -r ""$envf"" ] || continue
      envtxt=$(tr '\000' '\n' < ""$envf"" 2>/dev/null || true)
      d=$(printf '%s\n' ""$envtxt"" | sed -n 's/^DISPLAY=//p' | head -n 1)
      [ -n ""$d"" ] || continue
      xa=$(printf '%s\n' ""$envtxt"" | sed -n 's/^XAUTHORITY=//p' | head -n 1)
      [ -n ""$xa"" ] || xa=""$HOME/.Xauthority""
      CV_DISPLAY=""$d""
      CV_XAUTHORITY=""$xa""
      return 0
    done
    return 1
  }

  if cv_find_gui_env; then
    if command -v xclip >/dev/null 2>&1 && DISPLAY=""$CV_DISPLAY"" XAUTHORITY=""$CV_XAUTHORITY"" xclip -selection clipboard < ""$clip_file"" >/dev/null 2>&1; then return 0; fi
    if command -v xsel >/dev/null 2>&1 && DISPLAY=""$CV_DISPLAY"" XAUTHORITY=""$CV_XAUTHORITY"" xsel --clipboard --input < ""$clip_file"" >/dev/null 2>&1; then return 0; fi

    py=$(command -v python3 2>/dev/null || command -v python 2>/dev/null || true)
    if [ -n ""$py"" ]; then
      helper=""$dir/cvbridge_x11_clipboard.py""
      pidfile=""$dir/cvbridge_x11_clipboard.pid""
      cat > ""$helper"" <<'PY'
from __future__ import print_function
import ctypes
import ctypes.util
import os
import sys
import time
import traceback

X11_NAME = ctypes.util.find_library('X11') or 'libX11.so.6'
x11 = ctypes.CDLL(X11_NAME)

c_int = ctypes.c_int
c_uint = ctypes.c_uint
c_ulong = ctypes.c_ulong
c_long = ctypes.c_long
c_void_p = ctypes.c_void_p
c_char_p = ctypes.c_char_p
c_ubyte = ctypes.c_ubyte

Window = c_ulong
Atom = c_ulong
Time = c_ulong
Bool = c_int

SelectionRequest = 30
SelectionNotify = 31
PropModeReplace = 0
CurrentTime = 0

class XSelectionRequestEvent(ctypes.Structure):
    _fields_ = [
        ('type', c_int),
        ('serial', c_ulong),
        ('send_event', Bool),
        ('display', c_void_p),
        ('owner', Window),
        ('requestor', Window),
        ('selection', Atom),
        ('target', Atom),
        ('property', Atom),
        ('time', Time),
    ]

class XSelectionEvent(ctypes.Structure):
    _fields_ = [
        ('type', c_int),
        ('serial', c_ulong),
        ('send_event', Bool),
        ('display', c_void_p),
        ('requestor', Window),
        ('selection', Atom),
        ('target', Atom),
        ('property', Atom),
        ('time', Time),
    ]

class XEvent(ctypes.Union):
    _fields_ = [
        ('type', c_int),
        ('xselectionrequest', XSelectionRequestEvent),
        ('xselection', XSelectionEvent),
        ('pad', c_long * 24),
    ]

x11.XOpenDisplay.argtypes = [c_char_p]
x11.XOpenDisplay.restype = c_void_p
x11.XDefaultRootWindow.argtypes = [c_void_p]
x11.XDefaultRootWindow.restype = Window
x11.XCreateSimpleWindow.argtypes = [c_void_p, Window, c_int, c_int, c_uint, c_uint, c_uint, c_ulong, c_ulong]
x11.XCreateSimpleWindow.restype = Window
x11.XInternAtom.argtypes = [c_void_p, c_char_p, Bool]
x11.XInternAtom.restype = Atom
x11.XSetSelectionOwner.argtypes = [c_void_p, Atom, Window, Time]
x11.XGetSelectionOwner.argtypes = [c_void_p, Atom]
x11.XGetSelectionOwner.restype = Window
x11.XChangeProperty.argtypes = [c_void_p, Window, Atom, Atom, c_int, c_int, ctypes.POINTER(c_ubyte), c_int]
x11.XSendEvent.argtypes = [c_void_p, Window, Bool, c_long, ctypes.POINTER(XEvent)]
x11.XFlush.argtypes = [c_void_p]
x11.XPending.argtypes = [c_void_p]
x11.XPending.restype = c_int
x11.XNextEvent.argtypes = [c_void_p, ctypes.POINTER(XEvent)]

path = sys.argv[1]
display_name = os.environ.get('DISPLAY')
dpy = x11.XOpenDisplay(display_name.encode('ascii') if display_name else None)
if not dpy:
    raise RuntimeError('XOpenDisplay failed for DISPLAY=%r' % display_name)

root = x11.XDefaultRootWindow(dpy)
win = x11.XCreateSimpleWindow(dpy, root, 0, 0, 1, 1, 0, 0, 0)

def atom(name):
    return x11.XInternAtom(dpy, name.encode('ascii'), False)

CLIPBOARD = atom('CLIPBOARD')
TARGETS = atom('TARGETS')
UTF8_STRING = atom('UTF8_STRING')
TEXT = atom('TEXT')
STRING = atom('STRING')
ATOM_TYPE = atom('ATOM')

last_key = None
current_bytes = b''

def read_bytes():
    try:
        return open(path, 'rb').read()
    except Exception:
        return b''

def refresh_owner(force=False):
    global last_key, current_bytes
    try:
        st = os.stat(path)
        key = (st.st_mtime, st.st_size)
    except Exception:
        key = None
    if force or key != last_key:
        current_bytes = read_bytes()
        last_key = key
        x11.XSetSelectionOwner(dpy, CLIPBOARD, win, CurrentTime)
        x11.XFlush(dpy)

def change_property(requestor, prop, typ, fmt, data, count):
    x11.XChangeProperty(dpy, requestor, prop, typ, fmt, PropModeReplace, ctypes.cast(data, ctypes.POINTER(c_ubyte)), count)

def notify(req, prop):
    ev = XEvent()
    ev.xselection.type = SelectionNotify
    ev.xselection.display = req.display
    ev.xselection.requestor = req.requestor
    ev.xselection.selection = req.selection
    ev.xselection.target = req.target
    ev.xselection.property = prop
    ev.xselection.time = req.time
    x11.XSendEvent(dpy, req.requestor, False, 0, ctypes.byref(ev))
    x11.XFlush(dpy)

def handle_request(req):
    prop = req.property or req.target
    try:
        if req.target == TARGETS:
            targets = (c_ulong * 4)(TARGETS, UTF8_STRING, STRING, TEXT)
            change_property(req.requestor, prop, ATOM_TYPE, 32, targets, 4)
            notify(req, prop)
        elif req.target == UTF8_STRING:
            buf = ctypes.create_string_buffer(current_bytes)
            change_property(req.requestor, prop, UTF8_STRING, 8, buf, len(current_bytes))
            notify(req, prop)
        elif req.target == STRING or req.target == TEXT:
            buf = ctypes.create_string_buffer(current_bytes)
            change_property(req.requestor, prop, STRING, 8, buf, len(current_bytes))
            notify(req, prop)
        else:
            notify(req, 0)
    except Exception:
        traceback.print_exc()
        try:
            notify(req, 0)
        except Exception:
            pass

refresh_owner(True)
event = XEvent()
while True:
    while x11.XPending(dpy):
        x11.XNextEvent(dpy, ctypes.byref(event))
        if event.type == SelectionRequest:
            handle_request(event.xselectionrequest)
    refresh_owner(False)
    time.sleep(0.2)

PY
      if [ -f ""$pidfile"" ] && kill -0 ""$(cat ""$pidfile"")"" 2>/dev/null; then
        return 0
      fi
      DISPLAY=""$CV_DISPLAY"" XAUTHORITY=""$CV_XAUTHORITY"" nohup ""$py"" ""$helper"" ""$clip_file"" > ""$dir/cvbridge_x11_clipboard.log"" 2>&1 &
      echo $! > ""$pidfile""
      sleep 1
    fi
  fi
  return 0
}
try_gui_clipboard_write ""$clip"";
";
        }

        private string DesktopClipboardReadScript()
        {
            return @"
try_gui_clipboard_read() {
  clip_file=""$1""
  tmp_file=""$clip_file.gui.tmp""
  cv_find_gui_env() {
    if [ -n ""${DISPLAY:-}"" ]; then
      CV_DISPLAY=""$DISPLAY""
      CV_XAUTHORITY=""${XAUTHORITY:-$HOME/.Xauthority}""
      return 0
    fi
    for envf in /proc/[0-9]*/environ; do
      [ -r ""$envf"" ] || continue
      envtxt=$(tr '\000' '\n' < ""$envf"" 2>/dev/null || true)
      d=$(printf '%s\n' ""$envtxt"" | sed -n 's/^DISPLAY=//p' | head -n 1)
      [ -n ""$d"" ] || continue
      xa=$(printf '%s\n' ""$envtxt"" | sed -n 's/^XAUTHORITY=//p' | head -n 1)
      [ -n ""$xa"" ] || xa=""$HOME/.Xauthority""
      CV_DISPLAY=""$d""
      CV_XAUTHORITY=""$xa""
      return 0
    done
    return 1
  }

  if cv_find_gui_env; then
    if command -v xclip >/dev/null 2>&1 && DISPLAY=""$CV_DISPLAY"" XAUTHORITY=""$CV_XAUTHORITY"" xclip -selection clipboard -o > ""$tmp_file"" 2>/dev/null; then mv ""$tmp_file"" ""$clip_file""; return 0; fi
    if command -v xsel >/dev/null 2>&1 && DISPLAY=""$CV_DISPLAY"" XAUTHORITY=""$CV_XAUTHORITY"" xsel --clipboard --output > ""$tmp_file"" 2>/dev/null; then mv ""$tmp_file"" ""$clip_file""; return 0; fi

    py=$(command -v python3 2>/dev/null || command -v python 2>/dev/null || true)
    if [ -n ""$py"" ]; then
      reader=""$dir/cvbridge_tk_read.py""
      cat > ""$reader"" <<'PY'
from __future__ import print_function
import sys

try:
    import tkinter as tk
except Exception:
    import Tkinter as tk

path = sys.argv[1]
root = tk.Tk()
root.withdraw()
try:
    data = root.clipboard_get()
except Exception:
    data = ''
try:
    raw = data.encode('utf-8')
except Exception:
    raw = data
open(path, 'wb').write(raw)
root.destroy()
PY
      DISPLAY=""$CV_DISPLAY"" XAUTHORITY=""$CV_XAUTHORITY"" ""$py"" ""$reader"" ""$clip_file"" >/dev/null 2>&1 || true
    fi
  fi
  rm -f ""$tmp_file""
  return 0
}
try_gui_clipboard_read ""$clip"";
";
        }

        private string NormalizeRemote(string path)
        {
            string value = (path ?? "").Trim().Replace('\\', '/');
            if (value.Length == 0) return ".server_clipboard_bridge";
            while (value.Length > 1 && value.EndsWith("/")) value = value.Substring(0, value.Length - 1);
            return value;
        }

        private string JoinRemote(string basePath, string child)
        {
            string b = NormalizeRemote(basePath);
            string c = child.TrimStart('/');
            if (b.EndsWith("/")) return b + c;
            return b + "/" + c;
        }

        private string QuoteSh(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private string GetTextThreadSafe(TextBox box)
        {
            string value = "";
            Invoke(new Action(delegate { value = box.Text; }));
            return value;
        }

        private bool GetCheckedThreadSafe(CheckBox box)
        {
            bool value = false;
            Invoke(new Action(delegate { value = box.Checked; }));
            return value;
        }

        private void AppendLog(string text)
        {
            WriteLog("UI LOG: " + text);
            BeginInvoke(new Action(delegate
            {
                if (logBox == null) return;
                if (logBox.TextLength > 0) logBox.AppendText(Environment.NewLine);
                logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            }));
        }

        private void LoadRecentLogIntoPanel()
        {
            try
            {
                if (liveLogBox == null) return;

                string text = "";
                if (File.Exists(logPath))
                {
                    string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                    int start = Math.Max(0, lines.Length - 200);
                    StringBuilder sb = new StringBuilder();
                    for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                    text = sb.ToString();
                }

                if (liveLogBox.InvokeRequired)
                {
                    liveLogBox.BeginInvoke(new Action(delegate
                    {
                        liveLogBox.Text = text;
                        liveLogBox.SelectionStart = liveLogBox.TextLength;
                        liveLogBox.ScrollToCaret();
                    }));
                }
                else
                {
                    liveLogBox.Text = text;
                    liveLogBox.SelectionStart = liveLogBox.TextLength;
                    liveLogBox.ScrollToCaret();
                }
            }
            catch
            {
            }
        }

        private void WriteLog(string message)
        {
            try
            {
                lock (logLock)
                {
                    RotateLogIfNeeded();
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine;
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                    AppendLiveLogLine(line);
                }
            }
            catch
            {
                // Logging must never break clipboard/file operations.
            }
        }

        private void RotateLogIfNeeded()
        {
            try
            {
                FileInfo info = new FileInfo(logPath);
                if (!info.Exists || info.Length < 2 * 1024 * 1024) return;

                string oldPath = logPath + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(logPath, oldPath);
            }
            catch
            {
            }
        }

        private void AppendLiveLogLine(string line)
        {
            try
            {
                if (liveLogBox == null || liveLogBox.IsDisposed) return;
                if (liveLogBox.InvokeRequired)
                {
                    liveLogBox.BeginInvoke(new Action(delegate { AppendLiveLogLine(line); }));
                    return;
                }

                if (liveLogBox.TextLength > 120000)
                {
                    LoadRecentLogIntoPanel();
                    return;
                }

                liveLogBox.AppendText(line);
                liveLogBox.SelectionStart = liveLogBox.TextLength;
                liveLogBox.ScrollToCaret();
            }
            catch
            {
            }
        }
    }

    internal sealed class LabelStripItem : ToolStripStatusLabel
    {
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
