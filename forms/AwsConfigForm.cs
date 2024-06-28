using Amazon;
using System;
using System.Drawing;
using System.Windows.Forms;
using static EmailClient.Form1;

namespace EmailClient.forms
{
    public partial class AwsConfigForm : Form
    {
        private readonly Label regionLabel;
        private readonly TextBox regionTextBox;
        private readonly Label bucketNameLabel;
        private readonly TextBox bucketNameTextBox;
        private readonly Label dbPasswordLabel;
        private readonly TextBox dbPasswordTextBox;
        private readonly Button submitButton;
        private readonly CheckBox showPasswordCheckBox;

        private readonly EmailServer currentEmailServer;

        public AwsConfigForm(EmailServer currentEmailServer)
        {
            this.currentEmailServer = currentEmailServer;

            InitializeComponent();

            Text = "Config";

            this.BackColor = Color.Gray;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(425, 230); // Adjust the size based on your layout

            regionLabel = new Label { Text = "AWS Region:", Location = new Point(20, 20), Size = new Size(100, 20) };
            regionTextBox = new TextBox { Location = new Point(140, 20), Size = new Size(200, 20), BackColor = Color.LightGray };
            regionTextBox.Text = currentEmailServer.Region;

            bucketNameLabel = new Label { Text = "S3 Bucket Name:", Location = new Point(20, 60), Size = new Size(120, 20) };
            bucketNameTextBox = new TextBox { Location = new Point(140, 60), Size = new Size(200, 20), BackColor = Color.LightGray };
            bucketNameTextBox.Text = currentEmailServer.Bucket;

            dbPasswordLabel = new Label { Text = "Database Password:", Location = new Point(20, 100), Size = new Size(120, 20) };
            dbPasswordTextBox = new TextBox { Location = new Point(140, 100), Size = new Size(200, 20), UseSystemPasswordChar = true, BackColor = Color.LightGray };

            submitButton = new Button { Text = "Submit", Location = new Point(140, 160), Size = new Size(100, 30), BackColor = Color.LightGray };
            submitButton.Click += new EventHandler(SubmitButton_Click);

            showPasswordCheckBox = new CheckBox { Text = "Show Password", Location = new Point(345, 100), Size = new Size(104, 20) };
            showPasswordCheckBox.CheckedChanged += new EventHandler(TogglePasswordVisibility);

            ToolTip dbToolTip = GetToolTip();
            dbToolTip.SetToolTip(dbPasswordTextBox, "Leaving blank will result in no password protection for this local database.");
            dbToolTip.SetToolTip(dbPasswordLabel, "Leaving blank will result in no password protection for this local database.");

            Controls.Add(regionLabel);
            Controls.Add(regionTextBox);
            Controls.Add(bucketNameLabel);
            Controls.Add(bucketNameTextBox);
            Controls.Add(dbPasswordLabel);
            Controls.Add(dbPasswordTextBox);
            Controls.Add(submitButton);
            Controls.Add(showPasswordCheckBox);

            this.Shown += Form_Shown;
        }

        private void Form_Shown(object sender, EventArgs e)
        {   // So the user is auto focusing on the password box for easy relogging
            dbPasswordTextBox.Focus(); 
        }


        private void SubmitButton_Click(object sender, EventArgs e)
        {
            string regionName = regionTextBox.Text;
            currentEmailServer.RegionEndpoint = RegionEndpoint.GetBySystemName(regionName);

            if (currentEmailServer.RegionEndpoint == null)
            {
                MessageBox.Show("Invalid AWS Region", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            currentEmailServer.Bucket = bucketNameTextBox.Text;
            currentEmailServer.Region = regionName;
            currentEmailServer.Password = dbPasswordTextBox.Text;

            this.DialogResult = DialogResult.OK; // Set DialogResult to OK to close the form
            this.Close();
        }

        private void TogglePasswordVisibility(object sender, EventArgs e)
        {
            dbPasswordTextBox.UseSystemPasswordChar = !showPasswordCheckBox.Checked;
        }

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
    }
}
