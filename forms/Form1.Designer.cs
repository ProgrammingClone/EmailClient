
using EmailClient.forms;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace EmailClient
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private readonly RichTextBox textBoxEmail = new RichTextBox();
        private readonly ToolStripDropDownButton bucketButton = new ToolStripDropDownButton("Buckets");
        private readonly ContextMenuStrip bucketContextMenu = new ContextMenuStrip();

        private void InitializeComponent2()
        {
            textBoxEmail.Multiline = true;
            textBoxEmail.Dock = DockStyle.Fill;
            textBoxEmail.Visible = false;  // Start with TextBox hidden
            textBoxEmail.BackColor = Color.Gray;
            textBoxEmail.ReadOnly = true;
            textBoxEmail.ForeColor = Color.Black;
            splitContainer1.Panel2.Controls.Add(textBoxEmail);

            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.Gray;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dataGridView.DefaultCellStyle = dataGridViewCellStyle2;

            this.dataGridView.EnableHeadersVisualStyles = false;
            this.dataGridView.AllowUserToAddRows = false;
            this.dataGridView.EditMode = DataGridViewEditMode.EditOnEnter;
            this.dataGridView.Columns["Date"].Frozen = true;
            this.dataGridView.Columns["Contents"].Frozen = false;
            this.dataGridView.Columns["Contents"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            this.dataGridView.Dock = DockStyle.Fill;
            this.dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView.PerformLayout();

            this.dataGridView.CellDoubleClick += DataGridView_CellDoubleClick;
            this.dataGridView.CellContentClick += DataGridView_CellContentClick;
            this.dataGridView.CellValueNeeded += DataGridView_CellValueNeeded;

            this.dataGridView.Columns["Checked"].Resizable = DataGridViewTriState.False;
            this.dataGridView.Columns["Date"].Resizable = DataGridViewTriState.False;

            ToolTip queryEmailsTip = GetToolTip();
            queryEmailsTip.SetToolTip(queryEmailsButton, "Query all emails from your S3 bucket and save them locally.");

            ToolTip backTip = GetToolTip();
            backTip.SetToolTip(grayBackArrowButton, "Return to view all emails.");

            ToolTip s3DeleteTip = GetToolTip();
            s3DeleteTip.SetToolTip(s3DeleteButton, "Will only delete this email from your S3 bucket while keep a copy locally.");

            ToolTip deleteTip = GetToolTip();
            deleteTip.SetToolTip(s3DeleteButton, "Deletes the email locally and from your S3 bucket.");

            this.MinimumSize = new Size(625, 200);

            foreach (EmailServer server in settings.EmailServers.Values)
            {
                string bucket = server.Bucket;
                ToolStripMenuItem bucketItem = new ToolStripMenuItem(bucket);
                bucketItem.Click += BucketButton_Click;
                bucketContextMenu.Items.Add(bucketItem);
            }

            bucketContextMenu.Items.Add(new ToolStripSeparator());

            // Add button
            ToolStripMenuItem addItem = new ToolStripMenuItem("Add Bucket");
            addItem.Click += AddBucketItem_Click;
            bucketContextMenu.Items.Add(addItem);

            SetDefaultCheckedItem(currentEmailServer.Bucket);

            bucketButton.DropDown = bucketContextMenu;
            toolStrip.Items.Add(bucketButton);
        }

        #region Bucket context menu
        private void SetDefaultCheckedItem(string defaultBucket)
        {
            foreach (ToolStripMenuItem item in bucketContextMenu.Items)
            {
                if (item.Text == defaultBucket)
                {
                    item.Checked = true;
                    break;
                }
            }
        }

        private void AddBucketItem_Click(object sender, EventArgs e)
        {
            EmailServer emailServer = new EmailServer();

            using AwsConfigForm form = new AwsConfigForm(emailServer);
            if (form.ShowDialog() == DialogResult.OK)
            {
                string newBucketName = emailServer.Bucket;

                if (string.IsNullOrEmpty(newBucketName)) return;

                foreach (EmailServer server in settings.EmailServers.Values)
                {   // Ensure the user is not trying to add a duplicate
                    string bucket = server.Bucket;
                    if (newBucketName == bucket) return;
                }

                ToolStripMenuItem bucketItem = new ToolStripMenuItem(newBucketName);
                bucketItem.Click += BucketButton_Click;

                // Find the position of the separator or the Add Account button
                int separatorIndex = bucketContextMenu.Items.Count - 2;

                // Insert the new bucket item just before the separator or at the end if no separator found
                bucketContextMenu.Items.Insert(separatorIndex, bucketItem);

                settings.EmailServers.Add(newBucketName, emailServer);
                SaveToJson(settings);
            }
        }

        private void BucketButton_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem clickedItem = sender as ToolStripMenuItem;
            if (clickedItem != null)
            {
                if (clickedItem.Text == currentEmailServer.Bucket || clickedItem.Checked)
                {
                    Console.WriteLine("Same bucket item time to return");
                    return;
                }

                bool valid = settings.EmailServers.TryGetValue(clickedItem.Text, out EmailServer newServer);
                if (!valid) return;

                EmailDatabase newDb = new EmailDatabase(newServer.Bucket, "", true);
                bool success = newDb.IsValidDatabase;

                while (!newDb.IsValidDatabase || !success)
                {  
                    success = RequestDetails(newServer, false, false);
                    newDb.Dispose(); 
                    newDb = new EmailDatabase(newServer.Bucket, newServer.Password);
                    Console.WriteLine("BucketButton_Click Inside Valid DB:" + db.IsValidDatabase);
                }

                newServer.Password = ""; 
                currentEmailServer = newServer;
                db.Dispose();
                db = newDb;

                dbEmailCount = newDb.GetEmailCount();
                emailList = newDb.GetEmails(settings.EmailsPerPage, 0); // Populate the list with our newest archived emails
                Console.WriteLine($"BucketButton_Click EmailListSize:{emailList.Count} DbCount:{dbEmailCount}");

                s3Manager = new S3Manager(newServer.RegionEndpoint, newDb);

                dataGridView.RowCount = Math.Min(settings.EmailsPerPage, emailList.Count);
                pageNumberLabel.Text = $"Page 1/{Math.Ceiling(dbEmailCount / (double)settings.EmailsPerPage)}";

                foreach (ToolStripItem genericItem in bucketContextMenu.Items)
                {   // Uncheck the other item
                    if (genericItem is ToolStripMenuItem item) item.Checked = false;
                }

                clickedItem.Checked = true;
            }
        }
        #endregion

        private void DataGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            int row = e.RowIndex;

            if (row < 0 || row >= settings.EmailsPerPage || row >= emailList.Count) return;

            Console.WriteLine($"Double Clicked:{e.RowIndex}");

            Email email = emailList[row];

            string text = $"From: {email.Sender}\n" +
              $"To: {email.Receiver}\n" +
              $"Date: {email.Date.ToString("dddd, MMMM d, yyyy h:mm tt")}\n" +
              $"Key: {email.Key}\n" +
              $"Subject: {email.Subject}\n\n" +
              $"{email.Body}";

            textBoxEmail.Text = text;

            dataGridView.Visible = false;
            textBoxEmail.Visible = true;
        }

        private void GrayBackArrowButton_Click(object sender, EventArgs e)
        {
            dataGridView.Visible = true; 
            textBoxEmail.Visible = false;
            textBoxEmail.Text = "";
        }

        private void DataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            int row = e.RowIndex;

            if (row < 0 || row >= settings.EmailsPerPage || row >= emailList.Count) return;

            if (e.ColumnIndex == dataGridView.Columns["Checked"].Index)
            {
                Email email = emailList[row];

                var cell = dataGridView.Rows[e.RowIndex].Cells["Checked"];
                if (cell.Value == DBNull.Value || cell.Value == null) 
                {
                    cell.Value = false; 
                    email.Checked = false;
                }
                else
                {
                    bool isChecked = (bool)cell.Value;
                    cell.Value = !isChecked; 
                    email.Checked = !isChecked;
                }
            }
        }

        private void DataGridView_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            int row = e.RowIndex;
            //Console.WriteLine($"Row:{row} EmailsPerPage:{settings.EmailsPerPage} emailListCount:{emailList.Count}");

            if (row < 0 || row >= settings.EmailsPerPage || row >= emailList.Count) return;

            try
            {
                Email email = emailList[row];

                switch (e.ColumnIndex)
                {
                    case 0:
                        e.Value = email.Checked;
                        break;
                    case 1:
                        e.Value = email.Sender;
                        break;
                    case 2:
                        e.Value = email.Body;
                        break;
                    case 3:
                        e.Value = email.Date.ToString("M/d/yy");
                        break;
                }

                if (email.New) dataGridView.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 52, 171, 240); 
                else dataGridView.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.Gray;
            }
            catch (Exception ee)
            {
                Console.WriteLine(ee.StackTrace + "  -  " + ee.Message);
            }
        }


        #region Windows Form Designer generated code
        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.olderButton = new System.Windows.Forms.ToolStripButton();
            this.newerButton = new System.Windows.Forms.ToolStripButton();
            this.pageNumberLabel = new System.Windows.Forms.ToolStripLabel();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.s3DeleteButton = new System.Windows.Forms.Button();
            this.deleteButton = new System.Windows.Forms.Button();
            this.grayBackArrowButton = new System.Windows.Forms.Button();
            this.queryEmailsButton = new System.Windows.Forms.Button();
            this.dataGridView = new System.Windows.Forms.DataGridView();
            this.Checked = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Sender = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Contents = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Date = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.toolStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView)).BeginInit();
            this.SuspendLayout();
            // 
            // toolStrip
            // 
            this.toolStrip.BackColor = System.Drawing.Color.Gray;
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.olderButton,
            this.newerButton,
            this.pageNumberLabel});
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(800, 25);
            this.toolStrip.TabIndex = 0;
            // 
            // olderButton
            // 
            this.olderButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.olderButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.olderButton.Image = global::EmailClient.resources.GrayRightArrow.RightArrow;
            this.olderButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.olderButton.Name = "olderButton";
            this.olderButton.Size = new System.Drawing.Size(23, 22);
            this.olderButton.Text = "olderButton";
            this.olderButton.ToolTipText = "Next page";
            this.olderButton.Click += new System.EventHandler(this.OlderButton_Click);
            // 
            // newerButton
            // 
            this.newerButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.newerButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.newerButton.Image = global::EmailClient.resources.GrayLeftArrow.LeftArrow;
            this.newerButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.newerButton.Name = "newerButton";
            this.newerButton.Size = new System.Drawing.Size(23, 22);
            this.newerButton.Text = "newerPage";
            this.newerButton.ToolTipText = "Previous page";
            this.newerButton.Click += new System.EventHandler(this.NewerButton_Click);
            // 
            // pageNumberLabel
            // 
            this.pageNumberLabel.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.pageNumberLabel.Name = "pageNumberLabel";
            this.pageNumberLabel.Size = new System.Drawing.Size(53, 22);
            this.pageNumberLabel.Text = "Page 1/1";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Cursor = System.Windows.Forms.Cursors.VSplit;
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer1.Location = new System.Drawing.Point(0, 25);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.BackColor = System.Drawing.Color.Gray;
            this.splitContainer1.Panel1.Controls.Add(this.s3DeleteButton);
            this.splitContainer1.Panel1.Controls.Add(this.deleteButton);
            this.splitContainer1.Panel1.Controls.Add(this.grayBackArrowButton);
            this.splitContainer1.Panel1.Controls.Add(this.queryEmailsButton);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.dataGridView);
            this.splitContainer1.Size = new System.Drawing.Size(800, 425);
            this.splitContainer1.SplitterDistance = 132;
            this.splitContainer1.TabIndex = 1;
            // 
            // s3DeleteButton
            // 
            this.s3DeleteButton.Location = new System.Drawing.Point(13, 63);
            this.s3DeleteButton.Name = "s3DeleteButton";
            this.s3DeleteButton.Size = new System.Drawing.Size(75, 23);
            this.s3DeleteButton.TabIndex = 2;
            this.s3DeleteButton.Text = "S3 Delete";
            this.s3DeleteButton.UseVisualStyleBackColor = true;
            this.s3DeleteButton.Click += new System.EventHandler(this.S3DeleteButton_Click);
            // 
            // deleteButton
            // 
            this.deleteButton.Location = new System.Drawing.Point(12, 92);
            this.deleteButton.Name = "deleteButton";
            this.deleteButton.Size = new System.Drawing.Size(75, 23);
            this.deleteButton.TabIndex = 1;
            this.deleteButton.Text = "Delete";
            this.deleteButton.UseVisualStyleBackColor = true;
            this.deleteButton.Click += new System.EventHandler(this.DeleteButton_Click);
            // 
            // grayBackArrowButton
            // 
            this.grayBackArrowButton.BackgroundImage = global::EmailClient.resources.GrayBackArrow.BackArrow;
            this.grayBackArrowButton.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.grayBackArrowButton.FlatAppearance.BorderSize = 0;
            this.grayBackArrowButton.FlatAppearance.MouseDownBackColor = System.Drawing.SystemColors.Control;
            this.grayBackArrowButton.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.Control;
            this.grayBackArrowButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.grayBackArrowButton.Location = new System.Drawing.Point(12, 4);
            this.grayBackArrowButton.Name = "grayBackArrowButton";
            this.grayBackArrowButton.Size = new System.Drawing.Size(43, 24);
            this.grayBackArrowButton.TabIndex = 0;
            this.grayBackArrowButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.grayBackArrowButton.UseVisualStyleBackColor = false;
            this.grayBackArrowButton.Click += new System.EventHandler(this.GrayBackArrowButton_Click);
            // 
            // queryEmailsButton
            // 
            this.queryEmailsButton.Location = new System.Drawing.Point(12, 34);
            this.queryEmailsButton.Name = "queryEmailsButton";
            this.queryEmailsButton.Size = new System.Drawing.Size(106, 23);
            this.queryEmailsButton.TabIndex = 0;
            this.queryEmailsButton.Text = "Query Emails";
            this.queryEmailsButton.UseVisualStyleBackColor = true;
            this.queryEmailsButton.Click += new System.EventHandler(this.QueryEmailsButton_Click);
            // 
            // dataGridView
            // 
            this.dataGridView.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView.BackgroundColor = System.Drawing.Color.Gray;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.Gray;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Checked,
            this.Sender,
            this.Contents,
            this.Date});
            this.dataGridView.DefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView.Location = new System.Drawing.Point(0, 0);
            this.dataGridView.Name = "dataGridView";
            this.dataGridView.RowHeadersVisible = false;
            this.dataGridView.RowTemplate.Height = 25;
            this.dataGridView.Size = new System.Drawing.Size(664, 425);
            this.dataGridView.TabIndex = 0;
            this.dataGridView.VirtualMode = true;
            // 
            // Checked
            // 
            this.Checked.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Checked.FillWeight = 15F;
            this.Checked.Frozen = true;
            this.Checked.HeaderText = "";
            this.Checked.MinimumWidth = 20;
            this.Checked.Name = "Checked";
            this.Checked.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.Checked.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.Checked.Width = 20;
            // 
            // Sender
            // 
            this.Sender.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Sender.FillWeight = 40F;
            this.Sender.Frozen = true;
            this.Sender.HeaderText = "Sender";
            this.Sender.Name = "Sender";
            this.Sender.ReadOnly = true;
            this.Sender.Width = 160;
            // 
            // Contents
            // 
            this.Contents.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Contents.HeaderText = "Contents";
            this.Contents.MinimumWidth = 200;
            this.Contents.Name = "Contents";
            this.Contents.ReadOnly = true;
            this.Contents.Width = 401;
            // 
            // Date
            // 
            this.Date.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Date.FillWeight = 20F;
            this.Date.HeaderText = "Date";
            this.Date.MinimumWidth = 70;
            this.Date.Name = "Date";
            this.Date.ReadOnly = true;
            this.Date.Width = 70;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Gray;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.toolStrip);
            this.Name = "Form1";
            this.Text = "Form1";
            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.DataGridView dataGridView;
        private Button queryEmailsButton;
        private DataGridViewCheckBoxColumn Checked;
        private DataGridViewTextBoxColumn Sender;
        private DataGridViewTextBoxColumn Contents;
        private DataGridViewTextBoxColumn Date;
        private Button grayBackArrowButton;
        private Button deleteButton;
        private Button s3DeleteButton;

        private ToolTip GetToolTip()
        {
            ToolTip tool = new ToolTip
            {
                AutoPopDelay = int.MaxValue,  // Keep the tooltip visible indefinitely while hovering
                InitialDelay = 500,  // Delay before the tooltip appears
                ReshowDelay = 500,  // Delay when the pointer re-enters the control
                ShowAlways = true  // Force the tooltip to appear even if the form is not active
            };
            return tool;
        }

        private ToolStripButton olderButton;
        private ToolStripButton newerButton;
        private ToolStripLabel pageNumberLabel;
    }
}

