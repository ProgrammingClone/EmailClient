using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace EmailClient
{
    public sealed class S3Manager
    {
        private readonly AmazonS3Client s3Client;
        private readonly EmailDatabase db;

        public S3Manager(RegionEndpoint regionEndpoint, EmailDatabase db)
        {
            s3Client = new AmazonS3Client(regionEndpoint);
            this.db = db;
        }

        public async Task<List<Email>> ListAndDownloadEmails(string bucketName)
        {   
            try
            {
                ListObjectsV2Response response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = bucketName
                }).ConfigureAwait(false);

                List<Task<Email>> tasks = new List<Task<Email>>();
                foreach (var obj in response.S3Objects)
                {
                    Console.WriteLine("Key:" + obj.Key);
                    Task<Email> task = Task.Run(async () =>
                    {   // Since we are I/O bound running each Db check and then reading the object will preform much better in parallel then waiting with a single thread
                        try
                        {
                            if (!db.EmailExists(obj.Key))
                            {
                                return await ReadEmailContent(bucketName, obj.Key);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"ListAndDownloadEmailsError Message:{e.Message}  StackTrace:{e.StackTrace}");
                        }
                        return null;
                    });

                    tasks.Add(task);
                }
                // Waits for all tasks to complete, auto filters out null Task's. Then ensures each email is not null before ordering them in ascending order (oldest to newest)
                List<Email> newEmails = (await Task.WhenAll(tasks)).Where(email => email != null).OrderBy(email => email.Date).ToList();
                db.AddEmails(newEmails); // Db must be ordered from oldest to newest 

                newEmails.Reverse(); // Now reverse the array so that newest emails come first
                return newEmails;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred in ListAndDownloadEmails: {ex.Message}");
                return new List<Email>();  // Return an empty list or handle as appropriate
            }
        }

        private async Task<Email> ReadEmailContent(string bucketName, string keyName)
        {
            try
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName
                };

                using GetObjectResponse response = await s3Client.GetObjectAsync(request);
                using Stream responseStream = response.ResponseStream;
                using MimeMessage mimeMessage = MimeMessage.Load(responseStream);

                string body = "";

                if (!string.IsNullOrEmpty(mimeMessage.HtmlBody))
                {
                    HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(mimeMessage.HtmlBody);

                    var paragraphNodes = doc.DocumentNode.SelectNodes("//p");
                    if (paragraphNodes != null)
                    {
                        foreach (var node in paragraphNodes)
                        {
                            body += WebUtility.HtmlDecode(node.InnerText);
                        }
                    }
                    else
                    {   // Extracting all text nodes to ensure some data is retrieved.
                        var allTextNodes = doc.DocumentNode.SelectNodes("//text()");
                        if (allTextNodes != null)
                        {
                            foreach (var node in allTextNodes)
                            {
                                string text = WebUtility.HtmlDecode(node.InnerText);
                                if (!string.IsNullOrWhiteSpace(text)) body += text;
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(mimeMessage.TextBody))
                {   // If HtmlBody is null, fallback to TextBody
                    body = mimeMessage.TextBody;
                }

                return new Email
                {
                    New = true,
                    Key = keyName,
                    Sender = string.Join("; ", mimeMessage.From.Mailboxes.Select(mb => mb.Address)),
                    Receiver = string.Join("; ", mimeMessage.To.Mailboxes.Select(mb => mb.Address)),
                    Subject = mimeMessage.Subject,
                    Body = body,
                    Date = mimeMessage.Date.DateTime
                };

            } catch (Exception e) { Console.WriteLine($"Failed ReadEmailContent Key:{keyName} Message:{e.Message} "); }
            return null;
        }

        public async Task DeleteEmailFromS3Bucket(string bucketName, string emailKey)
        {
            DeleteObjectRequest deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = emailKey
            };

            try 
            {   // https://docs.aws.amazon.com/AmazonS3/latest/userguide/DeletingObjectVersions.html
                var response = await s3Client.DeleteObjectAsync(deleteObjectRequest);
                Console.WriteLine("Email deleted successfully from S3 bucket.");
            }
            catch (AmazonS3Exception e)
            {
                Console.WriteLine("Error encountered on server. Message:'{0}' when writing an object", e.Message);
            }
        }


        private async Task DownloadFile(string bucketName, string keyName)
        {
            string localPath = Path.Combine("C:\\Users\\yozma\\Downloads", keyName);
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = keyName
            };

            using GetObjectResponse response = await s3Client.GetObjectAsync(request);
            using Stream responseStream = response.ResponseStream;
            using FileStream fileStream = File.Create(localPath);
            responseStream.CopyTo(fileStream);
        }

    }

    public sealed class Email
    {
        public bool Checked { get; set; } = false;
        public int Id { get; set; }
        public string Key { get; set; }
        public string Sender { get; set; }
        public string Receiver { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public DateTime Date { get; set; }

        public bool New { get; set; } = false;
    }
}
