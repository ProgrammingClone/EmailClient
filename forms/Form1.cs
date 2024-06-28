using Amazon;
using EmailClient.forms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EmailClient
{
    public partial class Form1 : Form
    {
        private readonly Settings settings;
        private readonly EmailDatabase db;
        private readonly S3Manager s3Manager;

        private List<Email> emailList;
        private EmailServer currentEmailServer = new EmailServer();

        /// <summary> Ensure we don't try to query emails while we are arleady in the process of querying emails </summary>
        private bool queryingEmails = false;
        private volatile int dbEmailCount = 0;
        private volatile int currentPage = 1;

        public Form1()
        {
            // First load the settings and ask the user for their details
            settings = LoadFromJson();
            settings.EmailsPerPage = Math.Max(2, Math.Min(int.MaxValue, settings.EmailsPerPage));

            emailList = new List<Email>(settings.EmailsPerPage);

            if (settings.EmailServers.Count > 0 && !string.IsNullOrEmpty(settings.DefaultEmailServer) && settings.EmailServers.ContainsKey(settings.DefaultEmailServer))
            {   // Set the default Email server 
                currentEmailServer = settings.EmailServers[settings.DefaultEmailServer];
                Console.WriteLine("Set Default Email Server");
            }

            db = new EmailDatabase(currentEmailServer.Bucket, "", true); // First try to open the db without a password 
            bool success = db.IsValidDatabase;

            while (!db.IsValidDatabase || !success)
            {   // Maybe the user typed the wrong password in so keep prompting them (pressing the X inside AwsConfigForm will auto close this application)
                success = RequestDetails();
                db = new EmailDatabase(currentEmailServer.Bucket, currentEmailServer.Password);
                Console.WriteLine("Inside Valid DB:" + db.IsValidDatabase);
            }

            currentEmailServer.Password = ""; // We don't need the password anymore so remove it from our current object
            dbEmailCount = db.GetEmailCount();
            emailList = db.GetEmails(settings.EmailsPerPage, 0); // Populate the list with our newest archived emails
            Console.WriteLine($"EmailListSize:{emailList.Count} DbCount:{dbEmailCount}");

            s3Manager = new S3Manager(currentEmailServer.RegionEndpoint, db);

            InitializeComponent();
            InitializeComponent2();

            dataGridView.RowCount = Math.Min(settings.EmailsPerPage, emailList.Count);
            pageNumberLabel.Text = $"Page 1/{Math.Ceiling(dbEmailCount / (double)settings.EmailsPerPage)}";

            this.FormClosing += new FormClosingEventHandler(Form_FormClosing);
        }

        private void NewerButton_Click(object sender, EventArgs e)
        {
            if (queryingEmails) return; // If we are currently updating the Db force the user to wait

            if (currentPage <= 1) return;

            int pageCount = (int)Math.Ceiling(dbEmailCount / (double)settings.EmailsPerPage);

            emailList = db.GetEmails(settings.EmailsPerPage, (--currentPage * settings.EmailsPerPage) - settings.EmailsPerPage);
            dataGridView.RowCount = Math.Min(settings.EmailsPerPage, emailList.Count);
            pageNumberLabel.Text = $"Page {currentPage}/{pageCount}";
            Console.WriteLine($"Newer Click CurrentPage:{currentPage} PageCount:{pageCount} EmailsOnOurPage:{settings.EmailsPerPage} Offset:{(currentPage * settings.EmailsPerPage) - settings.EmailsPerPage}");
            dataGridView.Refresh();
            dataGridView.CurrentCell = dataGridView.Rows[0].Cells[1]; // This focus fixes a weird checked bug that keeps rows checked despite refreshing
        }

        private void OlderButton_Click(object sender, EventArgs e)
        {
            if (queryingEmails) return; // If we are currently updating the Db force the user to wait

            int pageCount = (int) Math.Ceiling(dbEmailCount / (double)settings.EmailsPerPage);
            if (currentPage >= pageCount)
            {
                Console.WriteLine("We are already on the last page.");
                return;
            }

            int emailsOnOurPage = settings.EmailsPerPage;
            if (++currentPage == pageCount)
            {
                int remainder = dbEmailCount % settings.EmailsPerPage;
                emailsOnOurPage = remainder > 0 ? remainder : settings.EmailsPerPage;
            }

            emailList = db.GetEmails(emailsOnOurPage, (currentPage - 1) * settings.EmailsPerPage);
            dataGridView.RowCount = Math.Min(settings.EmailsPerPage, emailList.Count);
            pageNumberLabel.Text = $"Page {currentPage}/{pageCount}";
            Console.WriteLine($"Older Click CurrentPage:{currentPage} PageCount:{pageCount} EmailsOnOurPage:{emailsOnOurPage} Offset:{(currentPage - 1) * settings.EmailsPerPage}");
            dataGridView.Refresh();
            dataGridView.CurrentCell = dataGridView.Rows[0].Cells[1]; // This focus fixes a weird checked bug that keeps rows checked despite refreshing
        }

        private void S3DeleteButton_Click(object sender, EventArgs e)
        {
            if (queryingEmails || !dataGridView.Visible) return; // If we are currently updating the Db force the user to wait

            foreach (Email email in emailList)
            {
                if (!email.Checked) continue;
                Console.WriteLine("Deleted Email:" + email.Key);
                DeleteEmail(email.Key, false);
            }

            dataGridView.Refresh();
        }

        /// <summary>
        ///     This runs another thread because deletions to our SQLite database can't happen concurrently because writes will lock the Db. This will
        ///     cause a bottle neck and we want our UI thread to be responsive thus we offload it to worker threads. 
        /// </summary>
        private void DeleteEmail(string key, bool deleteLocally)
        {
            Task.Run(async () =>
            {
                await s3Manager.DeleteEmailFromS3Bucket(currentEmailServer.Bucket, key);
                if (deleteLocally) db.DeleteEmailFromDatabase(key);
            });
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (queryingEmails || !dataGridView.Visible) return; // If we are currently updating the Db force the user to wait

            for (int i = emailList.Count - 1; i >= 0; i--)
            {
                Email email = emailList[i];
                if (!email.Checked) continue;

                DeleteEmail(email.Key, true); // Fire-and-forget the deletion task
                emailList.RemoveAt(i);
            }

            dataGridView.RowCount = Math.Min(settings.EmailsPerPage, emailList.Count);
            dataGridView.Refresh();
        }


        private void QueryEmailsButton_Click(object sender, EventArgs e)
        {
            if (queryingEmails) return; 

            queryingEmails = true;

            // Start asynchronous operation without awaiting it
            Task.Run(() => s3Manager.ListAndDownloadEmails(currentEmailServer.Bucket)).ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        Console.WriteLine("Error occurred: " + task.Exception.InnerException.Message);
                        return;
                    }

                    List<Email> newEmails = task.Result;

                    if(currentPage != 1 && newEmails.Count < settings.EmailsPerPage)
                    {   // This means we need the other emails to fill up page 1
                        int neededEmails = settings.EmailsPerPage - newEmails.Count;
                        Console.WriteLine($"CurrentPage:{currentPage} NewEmailCount:{newEmails.Count} EmailsPerPage:{settings.EmailsPerPage} NeededEmails:{neededEmails}");
                        newEmails.AddRange(db.GetEmails(neededEmails, settings.EmailsPerPage - neededEmails));
                    }
                    dbEmailCount = db.GetEmailCount();

                    this.Invoke(new Action(() =>
                    {   // Add the emails using Invoke so we can maintain thread safety
                        if(newEmails.Count > settings.EmailsPerPage) newEmails = newEmails.Take(settings.EmailsPerPage).ToList();
                        emailList.InsertRange(0, newEmails);
                        if (emailList.Count > settings.EmailsPerPage) emailList.RemoveRange(settings.EmailsPerPage, emailList.Count - settings.EmailsPerPage); 

                        dataGridView.RowCount = Math.Min(settings.EmailsPerPage, emailList.Count);
                        pageNumberLabel.Text = $"Page 1/{Math.Ceiling(dbEmailCount / (double)settings.EmailsPerPage)}";
                        currentPage = 1; // Reset the page number when the user querys more emails

                        dataGridView.Refresh();
                        Console.WriteLine("EmailSize:" + emailList.Count);
                        queryingEmails = false;
                    }));
                });
        }

        private void DatabaseButton_Click(object sender, EventArgs e)
        {

        }

        /// <summary> This will update 'currentEmailServer' with the users target server and obtain their local db password </summary>
        private bool RequestDetails()
        {
            using AwsConfigForm form = new AwsConfigForm(currentEmailServer);
            if (form.ShowDialog() == DialogResult.OK)
            {
                if (settings.EmailServers.Count == 0)
                {   // Insure theres always a default db
                    settings.DefaultEmailServer = currentEmailServer.Bucket;
                }
                else
                {   // This will insure there is always a valid default db
                    bool pass = false;
                    foreach (KeyValuePair<string, EmailServer> m in settings.EmailServers)
                    {
                        if (m.Key == settings.DefaultEmailServer)
                        {
                            pass = true;
                            break;
                        }
                    }

                    if (!pass) settings.DefaultEmailServer = currentEmailServer.Bucket;
                }

                settings.EmailServers[currentEmailServer.Bucket] = currentEmailServer;

                string bucketName = currentEmailServer.Bucket;
                string dbPassword = currentEmailServer.Password;

                // Now you can use region, bucketName, and dbPassword as needed
                Console.WriteLine($"Region: {currentEmailServer.RegionEndpoint.SystemName}, Bucket: {bucketName}, DB Password: {dbPassword}");

                SaveToJson(settings);
                return true;
            }
            else
            {
                Console.WriteLine("Close application");
                Form_FormClosing(null, null);
                Environment.Exit(0);
            }

            return false;
        }

        private void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            Console.WriteLine("Form Closing!");
            db.Close();
            Thread.Sleep(1000);
        }


        public static string GetFilePath(string fileName)
        {
            string exeFile = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exeFile);
            return Path.Combine(exeDir, fileName);
        }

        #region JsonFileHandler
        public static void SaveToJson(Settings data, string fileName = "Settings.json")
        { 
            string filePath = GetFilePath(fileName);
            string jsonData = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, jsonData);
        }

        public static Settings LoadFromJson(string fileName = "Settings.json")
        {
            string filePath = GetFilePath(fileName);
            if (!File.Exists(filePath))
            {   // Create the file if it does not exist
                Settings newSettings = new Settings();
                SaveToJson(newSettings);
                return newSettings;
            }

            string jsonData = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<Settings>(jsonData);
        }

        public sealed class Settings
        {
            public Dictionary<string, EmailServer> EmailServers { get; set; } = new Dictionary<string, EmailServer>();
            public string DefaultEmailServer { get; set; }
            public int EmailsPerPage { get; set; } = 100;
        }

        public sealed class EmailServer
        {
            public string Region { get; set; } = "";
            public string Bucket { get; set; } = "";

            [JsonIgnore]
            public RegionEndpoint RegionEndpoint { get; set; }

            [JsonIgnore]
            public string Password { get; set; } = "";
        }
        #endregion

    }
}
