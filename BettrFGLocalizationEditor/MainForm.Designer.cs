namespace TranslationEditor
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.topPanel = new Panel();
            this.btnOpen = new Button();
            this.btnSave = new Button();
            this.btnSaveAs = new Button();
            this.btnAddColumn = new Button();
            this.btnAddRow = new Button();
            this.btnExportLang = new Button();
            this.btnImportLang = new Button();
            this.lblSearch = new Label();
            this.txtSearch = new TextBox();
            this.lblStatus = new Label();
            this.btnTheme = new Button();
            this.grid = new DataGridView();
            this.topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grid)).BeginInit();
            this.SuspendLayout();

            this.topPanel.Dock = DockStyle.Top;
            this.topPanel.Height = 84;
            this.topPanel.Padding = new Padding(8);
            this.topPanel.Controls.Add(this.btnTheme);
            this.topPanel.Controls.Add(this.lblStatus);
            this.topPanel.Controls.Add(this.txtSearch);
            this.topPanel.Controls.Add(this.lblSearch);
            this.topPanel.Controls.Add(this.btnAddRow);
            this.topPanel.Controls.Add(this.btnAddColumn);
            this.topPanel.Controls.Add(this.btnSaveAs);
            this.topPanel.Controls.Add(this.btnSave);
            this.topPanel.Controls.Add(this.btnOpen);
            this.topPanel.Controls.Add(this.btnExportLang);
            this.topPanel.Controls.Add(this.btnImportLang);

            this.btnOpen.Text = "Open";
            this.btnOpen.Location = new Point(8, 10);
            this.btnOpen.Size = new Size(80, 27);
            this.btnOpen.Click += new EventHandler(this.btnOpen_Click);

            this.btnSave.Text = "Save";
            this.btnSave.Location = new Point(94, 10);
            this.btnSave.Size = new Size(80, 27);
            this.btnSave.Click += new EventHandler(this.btnSave_Click);

            this.btnSaveAs.Text = "Save as...";
            this.btnSaveAs.Location = new Point(180, 10);
            this.btnSaveAs.Size = new Size(110, 27);
            this.btnSaveAs.Click += new EventHandler(this.btnSaveAs_Click);

            this.btnAddColumn.Text = "+ Add language";
            this.btnAddColumn.Location = new Point(298, 10);
            this.btnAddColumn.Size = new Size(130, 27);
            this.btnAddColumn.Click += new EventHandler(this.btnAddColumn_Click);

            this.btnAddRow.Text = "+ Add Row";
            this.btnAddRow.Location = new Point(434, 10);
            this.btnAddRow.Size = new Size(100, 27);
            this.btnAddRow.Click += new EventHandler(this.btnAddRow_Click);

            this.btnExportLang.Text = "Export language...";
            this.btnExportLang.Location = new Point(8, 46);
            this.btnExportLang.Size = new Size(150, 27);
            this.btnExportLang.Click += new EventHandler(this.btnExportLang_Click);

            this.btnImportLang.Text = "Import language...";
            this.btnImportLang.Location = new Point(164, 46);
            this.btnImportLang.Size = new Size(150, 27);
            this.btnImportLang.Click += new EventHandler(this.btnImportLang_Click);

            this.lblSearch.Text = "Search:";
            this.lblSearch.Location = new Point(546, 15);
            this.lblSearch.AutoSize = true;

            this.txtSearch.Location = new Point(596, 11);
            this.txtSearch.Size = new Size(260, 23);
            this.txtSearch.TextChanged += new EventHandler(this.txtSearch_TextChanged);

            this.lblStatus.Text = "";
            this.lblStatus.Location = new Point(868, 15);
            this.lblStatus.AutoSize = true;
            this.lblStatus.ForeColor = SystemColors.GrayText;

            this.btnTheme.Text = "Dark";
            this.btnTheme.Location = new Point(1042, 8);
            this.btnTheme.Size = new Size(100, 30);
            this.btnTheme.Click += new EventHandler(this.btnTheme_Click);

            this.grid.Dock = DockStyle.Fill;
            this.grid.AllowUserToAddRows = false;
            this.grid.AllowUserToDeleteRows = true;
            this.grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            this.grid.RowHeadersWidth = 30;


            this.ClientSize = new Size(1150, 680);
            this.Text = "Translation Editor BettrFG";
            using (var iconStream = System.Reflection.Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("TranslationEditor.App.ico"))
            {
                if (iconStream != null) this.Icon = new Icon(iconStream);
            }
            this.Controls.Add(this.grid);
            this.Controls.Add(this.topPanel);
            this.MinimumSize = new Size(700, 400);

            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grid)).EndInit();
            this.ResumeLayout(false);
        }

        private Panel topPanel;
        private Button btnOpen;
        private Button btnSave;
        private Button btnSaveAs;
        private Button btnAddColumn;
        private Button btnAddRow;
        private Button btnExportLang;
        private Button btnImportLang;
        private Label lblSearch;
        private TextBox txtSearch;
        private Label lblStatus;
        private Button btnTheme;
        private DataGridView grid;
    }
}
