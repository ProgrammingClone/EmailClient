using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace EmailClient
{
    public sealed class EmailDatabase : IDisposable
    {
        private readonly sqlite3 db;
        public bool IsValidDatabase { get; private set; } = false;

        public EmailDatabase(string dbName, string password = "", bool hideErrors = false)
        {
            if (string.IsNullOrEmpty(dbName)) return;

            bool useEncryption = !string.IsNullOrEmpty(password);
            string path = Form1.GetFilePath("db");

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            path = Form1.GetFilePath("db\\" + dbName + ".db");
            Batteries_V2.Init();

            if (!File.Exists(path))
            {   // Check if the database file exists, create it if not
                File.Create(path).Dispose();
                Console.WriteLine("Created New Db");
            }

            Console.WriteLine("Path:" + path + " UseEncryption:" + useEncryption);

            // Open the database connection
            int rc = raw.sqlite3_open(path, out db);
            if (rc != raw.SQLITE_OK)
            {
                if (!hideErrors) MessageBox.Show("Failed to open database. " + rc, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (useEncryption && !string.IsNullOrEmpty(password))
            {
                rc = raw.sqlite3_exec(db, $"PRAGMA key = '{password}';", null, null, out _);
                if (rc != raw.SQLITE_OK)
                {
                    if(!hideErrors) MessageBox.Show("Incorrect or missing password for encrypted database.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // Try to read from the database as a test to validate password correctness
            rc = raw.sqlite3_exec(db, "SELECT count(*) FROM sqlite_master;", null, null, out _);
            if (rc != raw.SQLITE_OK)
            {
                if (!hideErrors) MessageBox.Show("Incorrect password or corrupted database.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            const string sql = @"
                CREATE TABLE IF NOT EXISTS Emails (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Key TEXT UNIQUE,
                    Sender TEXT,
                    Receiver TEXT,
                    Subject TEXT,
                    Body TEXT,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                -- Create an index on the 'Key' column to improve lookup performance
                CREATE INDEX IF NOT EXISTS idx_Emails_Key ON Emails (Key);
                ";
            rc = raw.sqlite3_exec(db, sql, null, null, out _);
            if (rc != raw.SQLITE_OK)
            {
                if (!hideErrors) MessageBox.Show("Failed to create table or index in the database.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            IsValidDatabase = true;
        }

        public bool EmailExists(string key)
        {
            try
            {
                string sql = "SELECT 1 FROM Emails WHERE Key = ?;";
                sqlite3_stmt stmt;
                raw.sqlite3_prepare_v2(db, sql, out stmt);
                raw.sqlite3_bind_text(stmt, 1, key);  // Bind the key to the query

                bool exists = raw.sqlite3_step(stmt) == raw.SQLITE_ROW;

                raw.sqlite3_finalize(stmt);
                return exists;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding emails: {ex.Message}");
                return true; // Returning true since true means it wont be added
            }
        }

        /// <summary>
        ///     This db will preserve its entry order meaning the newest emails will be stored at the end. Becuase of this we will need to retreive
        ///         emails from the end which is why we use `ORDER BY Id DESC` to reverse the order such that we return emails from newest to oldest.
        /// </summary>
        /// <param name="limit"> The amount of emails we should return </param>
        /// <param name="offset"> How many emails we should skip </param>
        /// <returns> The list of emails queried from newest to oldest. </returns>
        public List<Email> GetEmails(int limit, int offset)
        {
            try
            {
                List<Email> emails = new List<Email>();
                // Modified SQL to order by Id in descending order
                string sql = $"SELECT Id, Key, Sender, Receiver, Subject, Body, Timestamp FROM Emails ORDER BY Id DESC LIMIT {limit} OFFSET {offset};";
                sqlite3_stmt stmt;
                raw.sqlite3_prepare_v2(db, sql, out stmt);

                while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
                {
                    emails.Add(new Email
                    {
                        Id = raw.sqlite3_column_int(stmt, 0),
                        Key = raw.sqlite3_column_text(stmt, 1).utf8_to_string(),
                        Sender = raw.sqlite3_column_text(stmt, 2).utf8_to_string(),
                        Receiver = raw.sqlite3_column_text(stmt, 3).utf8_to_string(),
                        Subject = raw.sqlite3_column_text(stmt, 4).utf8_to_string(),
                        Body = raw.sqlite3_column_text(stmt, 5).utf8_to_string(),
                        Date = DateTime.ParseExact(raw.sqlite3_column_text(stmt, 6).utf8_to_string(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    });
                }
                raw.sqlite3_finalize(stmt);
                return emails;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" GetEmails Error: {ex.Message}");
                return new List<Email>();
            }
        }

        public int GetEmailCount()
        {
            string sql = "SELECT COUNT(*) FROM Emails;";
            sqlite3_stmt stmt;
            int rowCount = 0;

            // Prepare the statement
            int rc = raw.sqlite3_prepare_v2(db, sql, out stmt);
            if (rc == raw.SQLITE_OK)
            {
                // Execute the query
                if (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
                {
                    rowCount = raw.sqlite3_column_int(stmt, 0);
                }
                // Finalize to release the statement
                raw.sqlite3_finalize(stmt);
            }

            return rowCount;
        }


        public bool AddEmail(Email email)
        {
            string sql = @"
                INSERT OR IGNORE INTO Emails (Key, Sender, Receiver, Subject, Body, Timestamp) 
                VALUES (?, ?, ?, ?, ?, ?);";

            sqlite3_stmt stmt;
            raw.sqlite3_prepare_v2(db, sql, out stmt);
            raw.sqlite3_bind_text(stmt, 1, email.Key);
            raw.sqlite3_bind_text(stmt, 2, email.Sender);
            raw.sqlite3_bind_text(stmt, 3, email.Receiver);
            raw.sqlite3_bind_text(stmt, 4, email.Subject);
            raw.sqlite3_bind_text(stmt, 5, email.Body);
            raw.sqlite3_bind_text(stmt, 6, email.Date.ToString("dd-MM-yyyy HH:mm:ss"));  

            bool completed = raw.sqlite3_step(stmt) == raw.SQLITE_DONE;

            raw.sqlite3_finalize(stmt);
            return completed;
        }

        public void AddEmails(List<Email> emails)
        {
            try
            {
                string sql = @"
                INSERT OR IGNORE INTO Emails (Key, Sender, Receiver, Subject, Body, Timestamp) 
                VALUES (?, ?, ?, ?, ?, ?);";

                // Begin transaction
                raw.sqlite3_exec(db, "BEGIN TRANSACTION;", null, null, out _);

                foreach (Email email in emails)
                {
                    sqlite3_stmt stmt;
                    raw.sqlite3_prepare_v2(db, sql, out stmt);
                    raw.sqlite3_bind_text(stmt, 1, email.Key);
                    raw.sqlite3_bind_text(stmt, 2, email.Sender);
                    raw.sqlite3_bind_text(stmt, 3, email.Receiver);
                    raw.sqlite3_bind_text(stmt, 4, email.Subject);
                    raw.sqlite3_bind_text(stmt, 5, email.Body);
                    raw.sqlite3_bind_text(stmt, 6, email.Date.ToString("yyyy-MM-dd HH:mm:ss"));

                    raw.sqlite3_step(stmt);
                    raw.sqlite3_finalize(stmt);
                }

                // Commit transaction
                raw.sqlite3_exec(db, "COMMIT;", null, null, out _);
            } 
            catch (Exception) { }
        }


        public bool DeleteEmailFromDatabase(string emailKey)
        {
            try
            {
                string sql = "DELETE FROM Emails WHERE Key = ?;";
                sqlite3_stmt stmt;
                raw.sqlite3_prepare_v2(db, sql, out stmt);
                raw.sqlite3_bind_text(stmt, 1, emailKey);  // Bind the key to the SQL command
                bool completed = raw.sqlite3_step(stmt) == raw.SQLITE_DONE;

                raw.sqlite3_finalize(stmt);
                return completed;
            } catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if(db != null) raw.sqlite3_close(db);
            IsValidDatabase = false;
        }
    }
}
