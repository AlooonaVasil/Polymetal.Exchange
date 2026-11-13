namespace Polymetal.Tekla.ReferenceModels
{
    partial class Form_ReferenceModel
    {
        /// <summary>
        /// Требуется переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Освободить все используемые ресурсы.
        /// </summary>
        /// <param name="disposing">истинно, если управляемый ресурс должен быть удален; иначе ложно.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Код, автоматически созданный конструктором форм Windows

        /// <summary>
        /// Обязательный метод для поддержки конструктора - не изменяйте
        /// содержимое данного метода при помощи редактора кода.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form_ReferenceModel));
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.treeView1 = new MultiSelectTreeview.MultiSelectTreeview();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.button_Update = new System.Windows.Forms.Button();
            this.button_Show = new System.Windows.Forms.Button();
            this.button_Hide = new System.Windows.Forms.Button();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.fileSystemWatcher1 = new System.IO.FileSystemWatcher();
            this.label1 = new System.Windows.Forms.Label();
            this.comboBox_Search = new System.Windows.Forms.ComboBox();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.fileSystemWatcher1)).BeginInit();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.treeView1);
            this.groupBox1.Location = new System.Drawing.Point(12, 41);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(603, 468);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Опорные модели";
            // 
            // treeView1
            // 
            this.treeView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.treeView1.ImageIndex = 0;
            this.treeView1.ImageList = this.imageList1;
            this.treeView1.ItemHeight = 18;
            this.treeView1.Location = new System.Drawing.Point(6, 19);
            this.treeView1.Name = "treeView1";
            this.treeView1.SelectedImageIndex = 0;
            this.treeView1.SelectedNodes = ((System.Collections.Generic.List<System.Windows.Forms.TreeNode>)(resources.GetObject("treeView1.SelectedNodes")));
            this.treeView1.Size = new System.Drawing.Size(590, 443);
            this.treeView1.TabIndex = 0;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            // 
            // imageList1
            // 
            this.imageList1.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList1.ImageStream")));
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList1.Images.SetKeyName(0, "bulb_on.gif");
            this.imageList1.Images.SetKeyName(1, "status-away.png");
            this.imageList1.Images.SetKeyName(2, "status-busy.png");
            this.imageList1.Images.SetKeyName(3, "bulb_off.gif");
            this.imageList1.Images.SetKeyName(4, "site.gif");
            this.imageList1.Images.SetKeyName(5, "zone.gif");
            this.imageList1.Images.SetKeyName(6, "status.png");
            this.imageList1.Images.SetKeyName(7, "status-offline.png");
            // 
            // button_Update
            // 
            this.button_Update.Location = new System.Drawing.Point(12, 12);
            this.button_Update.Name = "button_Update";
            this.button_Update.Size = new System.Drawing.Size(109, 23);
            this.button_Update.TabIndex = 1;
            this.button_Update.Text = "Обновить модель";
            this.button_Update.UseVisualStyleBackColor = true;
            this.button_Update.Click += new System.EventHandler(this.button_Update_Click);
            // 
            // button_Show
            // 
            this.button_Show.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.button_Show.ImageIndex = 0;
            this.button_Show.ImageList = this.imageList1;
            this.button_Show.Location = new System.Drawing.Point(127, 12);
            this.button_Show.Name = "button_Show";
            this.button_Show.Size = new System.Drawing.Size(79, 23);
            this.button_Show.TabIndex = 3;
            this.button_Show.Text = "Показать";
            this.button_Show.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.button_Show.UseVisualStyleBackColor = true;
            this.button_Show.Click += new System.EventHandler(this.button_Show_Click);
            // 
            // button_Hide
            // 
            this.button_Hide.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.button_Hide.ImageIndex = 3;
            this.button_Hide.ImageList = this.imageList1;
            this.button_Hide.Location = new System.Drawing.Point(212, 12);
            this.button_Hide.Name = "button_Hide";
            this.button_Hide.Size = new System.Drawing.Size(68, 23);
            this.button_Hide.TabIndex = 4;
            this.button_Hide.Text = "Скрыть";
            this.button_Hide.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.button_Hide.UseVisualStyleBackColor = true;
            this.button_Hide.Click += new System.EventHandler(this.button_Hide_Click);
            // 
            // progressBar1
            // 
            this.progressBar1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar1.Location = new System.Drawing.Point(18, 509);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(597, 10);
            this.progressBar1.TabIndex = 5;
            // 
            // fileSystemWatcher1
            // 
            this.fileSystemWatcher1.EnableRaisingEvents = true;
            this.fileSystemWatcher1.Filter = "*.stp";
            this.fileSystemWatcher1.IncludeSubdirectories = true;
            this.fileSystemWatcher1.SynchronizingObject = this;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(286, 17);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(42, 13);
            this.label1.TabIndex = 6;
            this.label1.Text = "Поиск:";
            this.label1.Visible = false;
            // 
            // comboBox_Search
            // 
            this.comboBox_Search.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBox_Search.FormattingEnabled = true;
            this.comboBox_Search.Location = new System.Drawing.Point(334, 14);
            this.comboBox_Search.Name = "comboBox_Search";
            this.comboBox_Search.Size = new System.Drawing.Size(281, 21);
            this.comboBox_Search.Sorted = true;
            this.comboBox_Search.TabIndex = 7;
            this.comboBox_Search.Visible = false;
            this.comboBox_Search.SelectionChangeCommitted += new System.EventHandler(this.comboBox_Search_SelectionChangeCommitted);
            this.comboBox_Search.TextUpdate += new System.EventHandler(this.comboBox_Search_TextUpdate);
            // 
            // Form_ReferenceModel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(628, 521);
            this.Controls.Add(this.comboBox_Search);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.button_Hide);
            this.Controls.Add(this.button_Show);
            this.Controls.Add(this.button_Update);
            this.Controls.Add(this.groupBox1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form_ReferenceModel";
            this.Text = "Список опорных моделей";
            this.TopMost = true;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form_ReferenceModel_FormClosing);
            this.Load += new System.EventHandler(this.Form_ReferenceModel_Load);
            this.groupBox1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.fileSystemWatcher1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button button_Update;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.Button button_Show;
        private System.Windows.Forms.Button button_Hide;
        private MultiSelectTreeview.MultiSelectTreeview treeView1;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.IO.FileSystemWatcher fileSystemWatcher1;
        private System.Windows.Forms.ComboBox comboBox_Search;
        private System.Windows.Forms.Label label1;
    }
}

