using System.Data;
using System.Text;
using System.Text.Json;

namespace TranslationEditor
{
    public partial class MainForm : Form
    {
        private DataTable _table = new DataTable();
        private DataView _view = null!;
        private string? _currentPath;
        private char _delimiter = '\t';

        public MainForm()
        {
            InitializeComponent();
            EnableDoubleBuffering();
            NewEmptyTable();
            LoadSettings();
            ApplyTheme();

            topPanel.SizeChanged += (s, e) => PositionThemeButton();
            PositionThemeButton();
        }

        private void PositionThemeButton()
        {
            btnTheme.Left = topPanel.ClientSize.Width - btnTheme.Width - 12;
            btnTheme.Top = 8;
        }

        private void EnableDoubleBuffering()
        {
            typeof(DataGridView).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, grid, new object[] { true });

            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.RowTemplate.Height = 40;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
        }


        private void NewEmptyTable()
        {
            _table = new DataTable();
            _table.Columns.Add("key");
            _table.Columns.Add("en");
            _view = new DataView(_table);
            grid.DataSource = _view;
            _currentPath = null;
            UpdateStatus();
        }

        private void BindTable()
        {
            _view = new DataView(_table);
            grid.DataSource = _view;
            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable; 
                col.MinimumWidth = 150;
                col.Width = 280;
                col.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            lblStatus.Text = _currentPath == null
                ? $"Unsaved - {_table.Rows.Count} rows, {_table.Columns.Count} columns"
                : $"{Path.GetFileName(_currentPath)} - {_table.Rows.Count} rows, {_table.Columns.Count} columns";
        }


        private void btnOpen_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Translation tables (.bak)|*.tsv;*.csv;*.txt;*.bak|All files(*.*)|*.*",
                Title = "Open translation table"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                LoadFile(ofd.FileName);
                _currentPath = ofd.FileName;
                BindTable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open file:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadFile(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            lines = lines.Where((l, i) => !(i == lines.Length - 1 && l.Length == 0)).ToArray();
            if (lines.Length == 0) throw new Exception("The name cannot be empty.");

            _delimiter = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? ',' : '\t';

            var headerFields = SplitLine(lines[0], _delimiter);

            int dataColumnCount = headerFields.Length;
            if (lines.Length > 1)
            {
                dataColumnCount = Math.Max(dataColumnCount, SplitLine(lines[1], _delimiter).Length);
            }
            int missingLeadingColumns = dataColumnCount - headerFields.Length;
            var effectiveHeaderFields = new List<string>();
            if (missingLeadingColumns > 0)
            {
                effectiveHeaderFields.Add("key");
                for (int m = 1; m < missingLeadingColumns; m++) effectiveHeaderFields.Add($"col{m}");
            }
            effectiveHeaderFields.AddRange(headerFields);

            var table = new DataTable();
            var usedNames = new HashSet<string>();
            for (int i = 0; i < effectiveHeaderFields.Count; i++)
            {
                var name = effectiveHeaderFields[i].Trim();
                if (string.IsNullOrEmpty(name)) name = i == 0 ? "key" : $"col{i}";
                var baseName = name;
                int suffix = 1;
                while (!usedNames.Add(name))
                {
                    name = $"{baseName}_{suffix++}";
                }
                table.Columns.Add(name, typeof(string));
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0 && i == lines.Length - 1) continue;
                var fields = SplitLine(lines[i], _delimiter);
                var row = table.NewRow();
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    row[c] = c < fields.Length ? fields[c] : "";
                }
                table.Rows.Add(row);
            }

            _table = table;
        }

        private static string[] SplitLine(string line, char delimiter)
        {
            if (delimiter != ',')
            {
                return line.Split(delimiter);
            }

            // hi
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == delimiter) { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private void btnSave_Click(object? sender, EventArgs e)
        {
            if (_currentPath == null) { btnSaveAs_Click(sender, e); return; }
            var path = EnsureBakExtension(_currentPath);
            SaveFile(path);
            _currentPath = path;
        }

        private void btnSaveAs_Click(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Fall Guys translation table (*.bak)|*.bak",
                DefaultExt = "bak",
                AddExtension = true,
                FileName = _currentPath != null
                    ? Path.ChangeExtension(Path.GetFileName(_currentPath), ".bak")
                    : "localization.bak"
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            var path = EnsureBakExtension(sfd.FileName);
            SaveFile(path);
            _currentPath = path;
            UpdateStatus();
        }

        private static string EnsureBakExtension(string path)
        {
            return Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, ".bak");
        }

        private void SaveFile(string path)
        {
            try
            {
                const char saveDelimiter = '\t';
                var sb = new StringBuilder();
                var colNames = _table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
                sb.AppendLine(string.Join(saveDelimiter, colNames.Select(n => EscapeField(n, saveDelimiter))));

                foreach (DataRow row in _table.Rows)
                {
                    var fields = colNames.Select(n => EscapeField(row[n]?.ToString() ?? "", saveDelimiter));
                    sb.AppendLine(string.Join(saveDelimiter, fields));
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                UpdateStatus();
                MessageBox.Show(this, "Saved successfully", "Ready",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "It could not be saved:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string EscapeField(string value, char delimiter)
        {
            if (delimiter == ',' && (value.Contains(',') || value.Contains('"') || value.Contains('\n')))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }


        private void btnAddColumn_Click(object? sender, EventArgs e)
        {
            using var dlg = new PromptDialog("Add language", "Column name (ex: fr, de, pt-br):");
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var name = dlg.Value.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "The name cannot be empty.", "Warning");
                return;
            }
            if (_table.Columns.Contains(name))
            {
                MessageBox.Show(this, "A column with that name already exists.", "Warning");
                return;
            }

            _table.Columns.Add(name, typeof(string));
            foreach (DataRow row in _table.Rows) row[name] = "";
            BindTable();
        }

        private void btnAddRow_Click(object? sender, EventArgs e)
        {
            var row = _table.NewRow();
            foreach (DataColumn col in _table.Columns) row[col] = "";
            _table.Rows.Add(row);
            UpdateStatus();
        }

        // exports just [key, chosen-language] so a translator gets a small, focused file instead of
        // the whole table - handing them every other language's column is noise they can't use.
        private void btnExportLang_Click(object? sender, EventArgs e)
        {
            var keyCol = _table.Columns[0].ColumnName;
            var langs = _table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)
                .Where(n => n != keyCol).ToArray();
            if (langs.Length == 0)
            {
                MessageBox.Show(this, "No language columns to export yet.", "Warning");
                return;
            }

            using var pick = new LanguagePickDialog("Export language", "Language column to export:", langs);
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            var lang = pick.Value;
            if (string.IsNullOrEmpty(lang)) return;

            using var sfd = new SaveFileDialog
            {
                Filter = "Language export (*.bak)|*.bak",
                DefaultExt = "bak",
                AddExtension = true,
                FileName = $"{lang}.bak",
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join("\t", EscapeField(keyCol, '\t'), EscapeField(lang, '\t')));
                foreach (DataRow row in _table.Rows)
                {
                    sb.AppendLine(string.Join("\t",
                        EscapeField(row[keyCol]?.ToString() ?? "", '\t'),
                        EscapeField(row[lang]?.ToString() ?? "", '\t')));
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(false));
                MessageBox.Show(this, $"Exported {_table.Rows.Count} rows for '{lang}'.", "Ready");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // imports a [key, language] file (a translator's handoff) and merges it into the loaded table:
        // updates that one language column for matching keys, adds brand-new keys if present, never
        // touches any other column or row. safe to run on top of ongoing dev work without losing data.
        private void btnImportLang_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Language file (.bak/.tsv/.csv)|*.bak;*.tsv;*.csv;*.txt|All files(*.*)|*.*",
                Title = "Import language file",
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var text = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                    .Where(l => l.Length > 0).ToArray();
                if (lines.Length < 2)
                {
                    MessageBox.Show(this, "That file has no rows to import.", "Warning");
                    return;
                }

                char delimiter = Path.GetExtension(ofd.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? ',' : '\t';
                var header = SplitLine(lines[0], delimiter);
                if (header.Length < 2)
                {
                    MessageBox.Show(this, "Expected at least a key column and a language column.", "Warning");
                    return;
                }
                string suggestedLang = header[header.Length - 1].Trim();

                using var pick = new PromptDialog("Import language", "Import into language column:");
                pick.Value = suggestedLang;
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                string lang = pick.Value.Trim();
                if (string.IsNullOrEmpty(lang))
                {
                    MessageBox.Show(this, "The language column name cannot be empty.", "Warning");
                    return;
                }

                string keyCol = _table.Columns[0].ColumnName;
                if (!_table.Columns.Contains(lang)) _table.Columns.Add(lang, typeof(string));

                var byKey = _table.Rows.Cast<DataRow>().ToDictionary(r => r[keyCol]?.ToString() ?? "", r => r);
                int updated = 0, added = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    var fields = SplitLine(lines[i], delimiter);
                    if (fields.Length < 2) continue;
                    string key = fields[0].Trim();
                    if (key.Length == 0) continue;
                    string value = fields[fields.Length - 1];

                    if (byKey.TryGetValue(key, out var row))
                    {
                        row[lang] = value;
                        updated++;
                    }
                    else
                    {
                        var newRow = _table.NewRow();
                        foreach (DataColumn col in _table.Columns) newRow[col] = "";
                        newRow[keyCol] = key;
                        newRow[lang] = value;
                        _table.Rows.Add(newRow);
                        byKey[key] = newRow;
                        added++;
                    }
                }

                BindTable();
                MessageBox.Show(this, $"Imported into '{lang}': {updated} updated, {added} new rows.", "Ready");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not import:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void txtSearch_TextChanged(object? sender, EventArgs e)
        {
            var term = txtSearch.Text.Trim();
            if (term.Length == 0)
            {
                _view.RowFilter = "";
                return;
            }

            var escaped = term.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]");
            var parts = _table.Columns.Cast<DataColumn>()
                .Select(c => $"[{c.ColumnName}] LIKE '%{escaped}%'");
            _view.RowFilter = string.Join(" OR ", parts);
        }


        private bool _darkMode = false;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TranslationEditor", "settings.json");

        private class AppSettings
        {
            public bool DarkMode { get; set; }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) _darkMode = settings.DarkMode;
                }
            }
            catch
            {
                _darkMode = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new AppSettings { DarkMode = _darkMode });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
            }
        }

        private void btnTheme_Click(object? sender, EventArgs e)
        {
            _darkMode = !_darkMode;
            ApplyTheme();
            SaveSettings();
        }

        private void ApplyTheme()
        {
            var buttons = new[] { btnOpen, btnSave, btnSaveAs, btnAddColumn, btnAddRow, btnExportLang, btnImportLang, btnTheme };

            if (_darkMode)
            {
                var bg = Color.FromArgb(30, 30, 30);
                var panelBg = Color.FromArgb(45, 45, 48);
                var controlBg = Color.FromArgb(62, 62, 66);
                var text = Color.FromArgb(224, 224, 224);
                var gridBg = Color.FromArgb(37, 37, 38);
                var gridAlt = Color.FromArgb(45, 45, 48);
                var headerBg = Color.FromArgb(51, 51, 55);
                var accent = Color.FromArgb(0, 122, 204);

                this.BackColor = bg;
                topPanel.BackColor = panelBg;
                foreach (var btn in buttons)
                {
                    btn.BackColor = controlBg;
                    btn.ForeColor = text;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
                }
                txtSearch.BackColor = controlBg;
                txtSearch.ForeColor = text;
                lblSearch.ForeColor = text;
                lblStatus.ForeColor = Color.FromArgb(160, 160, 160);

                grid.BackgroundColor = gridBg;
                grid.GridColor = Color.FromArgb(63, 63, 70);
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = headerBg;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = headerBg;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = text;
                grid.RowHeadersDefaultCellStyle.BackColor = panelBg;
                grid.RowHeadersDefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.BackColor = gridBg;
                grid.DefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.SelectionBackColor = accent;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.AlternatingRowsDefaultCellStyle.BackColor = gridAlt;
                grid.AlternatingRowsDefaultCellStyle.ForeColor = text;

                btnTheme.Text = "Light";
            }
            else
            {
                this.BackColor = SystemColors.Control;
                topPanel.BackColor = SystemColors.Control;
                foreach (var btn in buttons)
                {
                    btn.BackColor = SystemColors.Control;
                    btn.ForeColor = SystemColors.ControlText;
                    btn.FlatStyle = FlatStyle.Standard;
                }
                txtSearch.BackColor = SystemColors.Window;
                txtSearch.ForeColor = SystemColors.WindowText;
                lblSearch.ForeColor = SystemColors.ControlText;
                lblStatus.ForeColor = SystemColors.GrayText;

                grid.BackgroundColor = SystemColors.Window;
                grid.GridColor = SystemColors.ControlDark;
                grid.EnableHeadersVisualStyles = true;
                grid.DefaultCellStyle.BackColor = SystemColors.Window;
                grid.DefaultCellStyle.ForeColor = SystemColors.WindowText;
                grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                grid.AlternatingRowsDefaultCellStyle.BackColor = SystemColors.Window;
                grid.AlternatingRowsDefaultCellStyle.ForeColor = SystemColors.WindowText;
                grid.RowHeadersDefaultCellStyle.BackColor = SystemColors.Control;
                grid.RowHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;

                btnTheme.Text = "Dark";
            }
        }
    }

    public class PromptDialog : Form
    {
        private readonly TextBox _input = new TextBox();
        public string Value
        {
            get => _input.Text;
            set => _input.Text = value;
        }

        public PromptDialog(string title, string label)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(320, 110);

            var lbl = new Label { Text = label, Location = new Point(12, 12), AutoSize = true };
            _input.Location = new Point(12, 36);
            _input.Size = new Size(296, 23);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(152, 72), Size = new Size(75, 27) };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(233, 72), Size = new Size(75, 27) };

            Controls.Add(lbl);
            Controls.Add(_input);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    public class LanguagePickDialog : Form
    {
        private readonly ComboBox _combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        public string Value => _combo.SelectedItem as string ?? "";

        public LanguagePickDialog(string title, string label, string[] options)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(320, 110);

            var lbl = new Label { Text = label, Location = new Point(12, 12), AutoSize = true };
            _combo.Location = new Point(12, 36);
            _combo.Size = new Size(296, 23);
            _combo.Items.AddRange(options);
            if (options.Length > 0) _combo.SelectedIndex = 0;

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(152, 72), Size = new Size(75, 27) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(233, 72), Size = new Size(75, 27) };

            Controls.Add(lbl);
            Controls.Add(_combo);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
