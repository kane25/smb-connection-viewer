using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SmbConnectionViewer
{
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

    internal sealed class MainForm : Form
    {
        private readonly TreeView tree = new TreeView();
        private readonly Label statusLabel = new Label();
        private readonly Button refreshButton = new Button();
        private readonly Button kickButton = new Button();
        private readonly Button saveNoteButton = new Button();
        private readonly CheckBox autoRefreshBox = new CheckBox();
        private readonly NumericUpDown intervalBox = new NumericUpDown();
        private readonly TextBox noteBox = new TextBox();
        private readonly Label displayNameValue = new Label();
        private readonly Label rawAddressValue = new Label();
        private readonly Label userValue = new Label();
        private readonly Label stateValue = new Label();
        private readonly Label sessionIdValue = new Label();
        private readonly Label openCountValue = new Label();
        private readonly TextBox filePathValue = new TextBox();
        private readonly Timer timer = new Timer();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> resolvedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string notesPath;
        private bool hasLoadedTreeOnce;
        private bool isRefreshing;
        private SmbSession selectedSession;
        private SmbOpenFile selectedFile;

        public MainForm()
        {
            notesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smb-connection-notes.json");
            Text = "SMB连接可视化工具";
            Width = 1280;
            Height = 720;
            MinimumSize = new Size(980, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 250, 252);

            BuildLayout();
            LoadNotes();

            timer.Interval = 10000;
            timer.Tick += delegate
            {
                if (autoRefreshBox.Checked)
                {
                    RefreshData();
                }
            };

            Shown += delegate
            {
                RefreshData();
                timer.Start();
            };
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);

            var topBar = new FlowLayoutPanel();
            topBar.Dock = DockStyle.Fill;
            topBar.FlowDirection = FlowDirection.LeftToRight;
            topBar.WrapContents = false;
            topBar.Padding = new Padding(0, 2, 0, 0);
            root.Controls.Add(topBar, 0, 0);

            var title = new Label();
            title.Text = "SMB连接可视化工具";
            title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            title.AutoSize = true;
            title.Margin = new Padding(0, 3, 18, 0);
            topBar.Controls.Add(title);

            ConfigureButton(refreshButton, "刷新", 82, Color.White, Color.FromArgb(51, 65, 85));
            refreshButton.Click += delegate { RefreshData(); };
            topBar.Controls.Add(refreshButton);

            autoRefreshBox.Text = "自动刷新";
            autoRefreshBox.Checked = true;
            autoRefreshBox.AutoSize = true;
            autoRefreshBox.Margin = new Padding(8, 8, 8, 0);
            topBar.Controls.Add(autoRefreshBox);

            var intervalLabel = new Label();
            intervalLabel.Text = "间隔";
            intervalLabel.AutoSize = true;
            intervalLabel.Margin = new Padding(0, 9, 6, 0);
            topBar.Controls.Add(intervalLabel);

            intervalBox.Minimum = 2;
            intervalBox.Maximum = 3600;
            intervalBox.Value = 10;
            intervalBox.Width = 58;
            intervalBox.Margin = new Padding(0, 4, 6, 0);
            intervalBox.ValueChanged += delegate
            {
                timer.Interval = Convert.ToInt32(intervalBox.Value) * 1000;
                statusLabel.Text = "自动刷新间隔已设置为 " + intervalBox.Value + " 秒";
            };
            topBar.Controls.Add(intervalBox);

            var secondsLabel = new Label();
            secondsLabel.Text = "秒";
            secondsLabel.AutoSize = true;
            secondsLabel.Margin = new Padding(0, 9, 18, 0);
            topBar.Controls.Add(secondsLabel);

            ConfigureButton(kickButton, "踢出所选连接", 128, Color.FromArgb(220, 38, 38), Color.White);
            kickButton.Enabled = false;
            kickButton.Click += delegate { KickSelectedSession(); };
            topBar.Controls.Add(kickButton);

            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterWidth = 12;
            split.SplitterDistance = 820;
            root.Controls.Add(split, 0, 1);

            var left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.RowCount = 2;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.BackColor = Color.White;
            left.Padding = new Padding(1);
            split.Panel1.Controls.Add(left);

            var header = new Label();
            header.Dock = DockStyle.Fill;
            header.Text = ColumnLine("状态", "显示名称", "原始地址", "用户", "打开数", "空闲", "备注");
            header.BackColor = Color.FromArgb(226, 232, 240);
            header.ForeColor = Color.FromArgb(51, 65, 85);
            header.Font = new Font("Consolas", 9F, FontStyle.Bold);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Padding = new Padding(8, 0, 0, 0);
            left.Controls.Add(header, 0, 0);

            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.Font = new Font("Consolas", 9F);
            tree.HideSelection = false;
            tree.AfterSelect += delegate { UpdateDetailsFromSelection(); };
            left.Controls.Add(tree, 0, 1);

            var detail = new TableLayoutPanel();
            detail.Dock = DockStyle.Fill;
            detail.BackColor = Color.White;
            detail.Padding = new Padding(14);
            detail.ColumnCount = 2;
            detail.RowCount = 10;
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 8; i++)
            {
                detail.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 0 ? 40 : 30));
            }
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            split.Panel2.Controls.Add(detail);

            var detailsTitle = new Label();
            detailsTitle.Text = "所选详情";
            detailsTitle.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            detailsTitle.Dock = DockStyle.Fill;
            detailsTitle.TextAlign = ContentAlignment.MiddleLeft;
            detail.Controls.Add(detailsTitle, 0, 0);
            detail.SetColumnSpan(detailsTitle, 2);

            AddDetailRow(detail, 1, "显示名称", displayNameValue);
            AddDetailRow(detail, 2, "原始地址", rawAddressValue);
            AddDetailRow(detail, 3, "用户", userValue);
            AddDetailRow(detail, 4, "状态", stateValue);
            AddDetailRow(detail, 5, "SessionId", sessionIdValue);
            AddDetailRow(detail, 6, "打开文件数", openCountValue);

            var fileLabel = DetailLabel("文件路径");
            detail.Controls.Add(fileLabel, 0, 7);
            filePathValue.ReadOnly = true;
            filePathValue.BorderStyle = BorderStyle.None;
            filePathValue.Multiline = true;
            filePathValue.Dock = DockStyle.Fill;
            filePathValue.BackColor = Color.White;
            detail.Controls.Add(filePathValue, 1, 7);

            var notePanel = new TableLayoutPanel();
            notePanel.Dock = DockStyle.Fill;
            notePanel.RowCount = 2;
            notePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            notePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detail.Controls.Add(notePanel, 0, 8);
            detail.SetColumnSpan(notePanel, 2);

            var noteLabel = DetailLabel("显示名称/备注");
            noteLabel.Dock = DockStyle.Fill;
            notePanel.Controls.Add(noteLabel, 0, 0);
            noteBox.Dock = DockStyle.Fill;
            noteBox.Multiline = true;
            noteBox.ScrollBars = ScrollBars.Vertical;
            noteBox.Enabled = false;
            notePanel.Controls.Add(noteBox, 0, 1);

            ConfigureButton(saveNoteButton, "保存显示名称/备注", 160, Color.White, Color.FromArgb(51, 65, 85));
            saveNoteButton.Dock = DockStyle.Fill;
            saveNoteButton.Enabled = false;
            saveNoteButton.Click += delegate { SaveSelectedNote(); };
            detail.Controls.Add(saveNoteButton, 0, 9);
            detail.SetColumnSpan(saveNoteButton, 2);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.BackColor = Color.FromArgb(226, 232, 240);
            statusLabel.ForeColor = Color.FromArgb(51, 65, 85);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(10, 0, 0, 0);
            statusLabel.Text = "准备就绪";
            root.Controls.Add(statusLabel, 0, 2);
        }

        private static void ConfigureButton(Button button, string text, int width, Color backColor, Color foreColor)
        {
            button.Text = text;
            button.Width = width;
            button.Height = 30;
            button.Margin = new Padding(0, 4, 8, 0);
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        }

        private static Label DetailLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.ForeColor = Color.FromArgb(100, 116, 139);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static void AddDetailRow(TableLayoutPanel panel, int row, string labelText, Label valueLabel)
        {
            panel.Controls.Add(DetailLabel(labelText), 0, row);
            valueLabel.Text = "-";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.AutoEllipsis = true;
            panel.Controls.Add(valueLabel, 1, row);
        }

        private static string ColumnLine(string state, string displayName, string rawAddress, string user, string opens, string idle, string note)
        {
            return Pad(state, 16) + Pad(displayName, 18) + Pad(rawAddress, 31) + Pad(user, 21) + Pad(opens, 10) + Pad(idle, 12) + note;
        }

        private static string Pad(string value, int width)
        {
            value = value ?? "";
            if (value.Length >= width)
            {
                return value.Substring(0, width - 1) + " ";
            }
            return value.PadRight(width);
        }

        private void LoadNotes()
        {
            notes.Clear();
            if (!File.Exists(notesPath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(notesPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var loaded = serializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded == null)
                {
                    return;
                }

                foreach (var pair in loaded)
                {
                    notes[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("备注文件读取失败，将以空备注启动。\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveNotes()
        {
            try
            {
                var json = serializer.Serialize(notes);
                File.WriteAllText(notesPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show("备注保存失败。\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshData()
        {
            if (isRefreshing)
            {
                return;
            }

            isRefreshing = true;
            try
            {
                statusLabel.Text = "正在刷新...";
                var expanded = GetExpansionState();
                var snapshot = ReadSmbSnapshot();
                tree.BeginUpdate();
                tree.Nodes.Clear();

                var activeRoot = NewCategoryNode("已连接（活动中）", snapshot.ActiveSessions.Count, Color.FromArgb(22, 163, 74), "active");
                var idleRoot = NewCategoryNode("已连接（未打开文件）", snapshot.IdleSessions.Count, Color.FromArgb(71, 85, 105), "idle");
                activeRoot.Expand();
                idleRoot.Expand();

                if (expanded.ContainsKey("category:active"))
                {
                    SetExpanded(activeRoot, expanded["category:active"]);
                }
                else if (hasLoadedTreeOnce)
                {
                    activeRoot.Collapse();
                }

                if (expanded.ContainsKey("category:idle"))
                {
                    SetExpanded(idleRoot, expanded["category:idle"]);
                }
                else if (hasLoadedTreeOnce)
                {
                    idleRoot.Collapse();
                }

                foreach (var row in snapshot.ActiveSessions)
                {
                    activeRoot.Nodes.Add(NewSessionNode(row, expanded));
                }

                foreach (var row in snapshot.IdleSessions)
                {
                    idleRoot.Nodes.Add(NewSessionNode(row, expanded));
                }

                tree.Nodes.Add(activeRoot);
                tree.Nodes.Add(idleRoot);
                tree.EndUpdate();
                hasLoadedTreeOnce = true;

                if (snapshot.Sessions.Count == 0)
                {
                    ClearDetails();
                    statusLabel.Text = "暂无SMB连接";
                }
                else
                {
                    statusLabel.Text = string.Format("共 {0} 个连接，活动中 {1} 个；最后刷新：{2}", snapshot.Sessions.Count, snapshot.ActiveSessions.Count, DateTime.Now.ToString("HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "刷新失败：" + ex.Message;
                MessageBox.Show("读取SMB连接失败。请用管理员身份运行，并确认当前系统支持 SMB PowerShell cmdlet。\r\n\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private TreeNode NewCategoryNode(string text, int count, Color color, string key)
        {
            var node = new TreeNode(text + "  " + count);
            node.ForeColor = color;
            node.BackColor = key == "active" ? Color.FromArgb(220, 252, 231) : Color.FromArgb(226, 232, 240);
            node.Tag = new NodeTag { Type = "Category", Key = key };
            return node;
        }

        private TreeNode NewSessionNode(SessionRow row, Dictionary<string, bool> expanded)
        {
            var session = row.Session;
            var openCount = row.OpenFiles.Count;
            var isActive = openCount > 0;
            var state = isActive ? "已连接(活动中)" : "已连接(未打开)";
            var note = GetNote(session);
            var displayName = ResolveDisplayName(session);
            var line = ColumnLine(state, displayName, session.ClientComputerName, session.ClientUserName, "打开:" + openCount, FormatSeconds(session.SecondsIdle), string.IsNullOrWhiteSpace(note) ? "-" : note);
            var node = new TreeNode(line);
            node.ForeColor = isActive ? Color.FromArgb(22, 163, 74) : Color.FromArgb(51, 65, 85);
            node.BackColor = isActive ? Color.FromArgb(236, 253, 245) : Color.FromArgb(248, 250, 252);
            node.Tag = new NodeTag { Type = "Session", Session = session };

            foreach (var file in row.OpenFiles)
            {
                var path = string.IsNullOrWhiteSpace(file.Path) ? file.ShareRelativePath : file.Path;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = "(未知路径)";
                }

                var fileNode = new TreeNode("打开文件  " + path);
                fileNode.ForeColor = Color.FromArgb(180, 83, 9);
                fileNode.BackColor = Color.FromArgb(255, 251, 235);
                fileNode.Tag = new NodeTag { Type = "OpenFile", Session = session, OpenFile = file };
                node.Nodes.Add(fileNode);
            }

            var key = "session:" + session.SessionId;
            if (expanded.ContainsKey(key))
            {
                SetExpanded(node, expanded[key]);
            }
            return node;
        }

        private static void SetExpanded(TreeNode node, bool expanded)
        {
            if (expanded)
            {
                node.Expand();
            }
            else
            {
                node.Collapse();
            }
        }

        private Dictionary<string, bool> GetExpansionState()
        {
            var state = new Dictionary<string, bool>();
            foreach (TreeNode root in tree.Nodes)
            {
                var rootTag = root.Tag as NodeTag;
                if (rootTag != null && rootTag.Type == "Category")
                {
                    state["category:" + rootTag.Key] = root.IsExpanded;
                }

                foreach (TreeNode child in root.Nodes)
                {
                    var childTag = child.Tag as NodeTag;
                    if (childTag != null && childTag.Type == "Session" && childTag.Session != null)
                    {
                        state["session:" + childTag.Session.SessionId] = child.IsExpanded;
                    }
                }
            }
            return state;
        }

        private SmbSnapshot ReadSmbSnapshot()
        {
            var command = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$sessions = @(Get-SmbSession | ForEach-Object { [pscustomobject]@{ ClientComputerName = $_.ClientComputerName; ClientUserName = $_.ClientUserName; SessionId = [string]$_.SessionId; NumOpens = $_.NumOpens; SecondsIdle = $_.SecondsIdle } })
$files = @(Get-SmbOpenFile | ForEach-Object { [pscustomobject]@{ SessionId = [string]$_.SessionId; Path = $_.Path; ShareRelativePath = $_.ShareRelativePath } })
[pscustomobject]@{ Sessions = $sessions; OpenFiles = $files } | ConvertTo-Json -Compress -Depth 5
";
            var json = RunPowerShell(command);
            var dto = serializer.Deserialize<SmbSnapshotDto>(json);
            var snapshot = new SmbSnapshot();
            if (dto == null)
            {
                return snapshot;
            }

            if (dto.Sessions != null)
            {
                snapshot.Sessions.AddRange(dto.Sessions);
            }

            var filesBySession = new Dictionary<string, List<SmbOpenFile>>();
            if (dto.OpenFiles != null)
            {
                foreach (var file in dto.OpenFiles)
                {
                    var key = Convert.ToString(file.SessionId);
                    if (!filesBySession.ContainsKey(key))
                    {
                        filesBySession[key] = new List<SmbOpenFile>();
                    }
                    filesBySession[key].Add(file);
                }
            }

            snapshot.Sessions.Sort(delegate(SmbSession a, SmbSession b)
            {
                var nameCompare = string.Compare(a.ClientComputerName, b.ClientComputerName, StringComparison.OrdinalIgnoreCase);
                return nameCompare != 0 ? nameCompare : string.Compare(a.ClientUserName, b.ClientUserName, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var session in snapshot.Sessions)
            {
                var key = Convert.ToString(session.SessionId);
                var files = filesBySession.ContainsKey(key) ? filesBySession[key] : new List<SmbOpenFile>();
                files.Sort(delegate(SmbOpenFile a, SmbOpenFile b)
                {
                    return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                });

                var row = new SessionRow { Session = session, OpenFiles = files };
                if (files.Count > 0)
                {
                    snapshot.ActiveSessions.Add(row);
                }
                else
                {
                    snapshot.IdleSessions.Add(row);
                }
            }

            return snapshot;
        }

        private string RunPowerShell(string command)
        {
            var info = new ProcessStartInfo();
            info.FileName = "powershell.exe";
            info.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteArgument(command);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            using (var process = Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "PowerShell命令执行失败。" : error.Trim());
                }
                return output.Trim();
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "; ") + "\"";
        }

        private static string QuotePowerShellLiteral(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        private void UpdateDetailsFromSelection()
        {
            var tag = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as NodeTag;
            if (tag == null || tag.Session == null)
            {
                ClearDetails();
                return;
            }

            selectedSession = tag.Session;
            selectedFile = tag.OpenFile;
            displayNameValue.Text = ResolveDisplayName(selectedSession);
            rawAddressValue.Text = selectedSession.ClientComputerName;
            userValue.Text = selectedSession.ClientUserName;
            stateValue.Text = selectedSession.NumOpens > 0 ? "已连接（活动中）" : "已连接（未打开文件）";
            sessionIdValue.Text = Convert.ToString(selectedSession.SessionId);
            openCountValue.Text = Convert.ToString(selectedSession.NumOpens);
            filePathValue.Text = selectedFile == null ? "-" : (string.IsNullOrWhiteSpace(selectedFile.Path) ? selectedFile.ShareRelativePath : selectedFile.Path);
            noteBox.Text = GetNote(selectedSession);
            noteBox.Enabled = true;
            saveNoteButton.Enabled = true;
            kickButton.Enabled = true;
        }

        private void ClearDetails()
        {
            selectedSession = null;
            selectedFile = null;
            displayNameValue.Text = "-";
            rawAddressValue.Text = "-";
            userValue.Text = "-";
            stateValue.Text = "-";
            sessionIdValue.Text = "-";
            openCountValue.Text = "-";
            filePathValue.Text = "-";
            noteBox.Text = "";
            noteBox.Enabled = false;
            saveNoteButton.Enabled = false;
            kickButton.Enabled = false;
        }

        private void SaveSelectedNote()
        {
            if (selectedSession == null)
            {
                return;
            }

            var key = GetNoteKey(selectedSession);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var note = noteBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(note))
            {
                notes.Remove(key);
            }
            else
            {
                notes[key] = note;
            }
            SaveNotes();
            RefreshData();
            statusLabel.Text = "显示名称/备注已保存";
        }

        private void KickSelectedSession()
        {
            if (selectedSession == null)
            {
                return;
            }

            var answer = MessageBox.Show("确定要踢出 " + selectedSession.ClientComputerName + " 的连接吗？\r\nSessionId: " + selectedSession.SessionId, "踢出SMB连接", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                var command = "Close-SmbSession -SessionId " + QuotePowerShellLiteral(selectedSession.SessionId) + " -Force";
                RunPowerShell(command);
                statusLabel.Text = "已踢出连接：" + selectedSession.ClientComputerName;
                RefreshData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("踢出连接失败。请用管理员身份运行。\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string ResolveDisplayName(SmbSession session)
        {
            var note = GetNote(session);
            if (!string.IsNullOrWhiteSpace(note))
            {
                return note;
            }

            var cleanName = NormalizeClientName(session.ClientComputerName);
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                return "-";
            }

            IPAddress address;
            if (!IPAddress.TryParse(cleanName, out address))
            {
                return cleanName;
            }

            if (resolvedNames.ContainsKey(cleanName))
            {
                return resolvedNames[cleanName];
            }

            var resolved = cleanName;
            try
            {
                var entry = Dns.GetHostEntry(cleanName);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.HostName))
                {
                    resolved = entry.HostName.Split('.')[0];
                }
            }
            catch
            {
                resolved = cleanName;
            }
            resolvedNames[cleanName] = resolved;
            return resolved;
        }

        private string GetNote(SmbSession session)
        {
            var key = GetNoteKey(session);
            return !string.IsNullOrWhiteSpace(key) && notes.ContainsKey(key) ? notes[key] : "";
        }

        private static string GetNoteKey(SmbSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ClientComputerName))
            {
                return "";
            }
            return session.ClientComputerName.Trim().ToLowerInvariant();
        }

        private static string NormalizeClientName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "" : name.Trim().Trim('[', ']');
        }

        private static string FormatSeconds(double seconds)
        {
            if (seconds < 60)
            {
                return string.Format("{0:N0}秒", seconds);
            }
            if (seconds < 3600)
            {
                return string.Format("{0:N1}分钟", seconds / 60);
            }
            return string.Format("{0:N1}小时", seconds / 3600);
        }
    }

    internal sealed class NodeTag
    {
        public string Type { get; set; }
        public string Key { get; set; }
        public SmbSession Session { get; set; }
        public SmbOpenFile OpenFile { get; set; }
    }

    internal sealed class SmbSnapshot
    {
        public readonly List<SmbSession> Sessions = new List<SmbSession>();
        public readonly List<SessionRow> ActiveSessions = new List<SessionRow>();
        public readonly List<SessionRow> IdleSessions = new List<SessionRow>();
    }

    internal sealed class SessionRow
    {
        public SmbSession Session { get; set; }
        public List<SmbOpenFile> OpenFiles { get; set; }
    }

    public sealed class SmbSnapshotDto
    {
        public SmbSession[] Sessions { get; set; }
        public SmbOpenFile[] OpenFiles { get; set; }
    }

    public sealed class SmbSession
    {
        public string ClientComputerName { get; set; }
        public string ClientUserName { get; set; }
        public string SessionId { get; set; }
        public int NumOpens { get; set; }
        public double SecondsIdle { get; set; }
    }

    public sealed class SmbOpenFile
    {
        public string SessionId { get; set; }
        public string Path { get; set; }
        public string ShareRelativePath { get; set; }
    }
}
