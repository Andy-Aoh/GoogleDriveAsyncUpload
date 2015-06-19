﻿/*
Copyright 2013 Google Inc

Licensed under the Apache License, Version 2.0(the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v2;
using Google.Apis.Drive.v2.Data;
using Google.Apis.Logging;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace GDriveSync1
{
    /// <summary>
    /// A sample for the Drive API. This samples demonstrates resumable media upload and media download.
    /// See https://developers.google.com/drive/ for more details regarding the Drive API.
    /// </summary>
    class Program
    {
        public static string GetContentType(string getfileName)
        {
            string contentType = "application/octetstream";
            string ext = System.IO.Path.GetExtension(getfileName).ToLower();

            Microsoft.Win32.RegistryKey registryKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext);

            if (registryKey != null && registryKey.GetValue("Content Type") != null)

                contentType = registryKey.GetValue("Content Type").ToString();

            return contentType;
        }
        public static long getFileSize(string FilePath)
        {
            System.IO.FileStream file = new System.IO.FileStream(FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            return file.Length;
        }
        static Program()
        {
            // initialize the log instance
            ApplicationContext.RegisterLogger(new Log4NetLogger());
            Logger = ApplicationContext.Logger.ForType<ResumableUpload<Program>>();
        }
        static public long currentfullFileSize;
        #region Consts

        private const int KB = 0x400;
        private const int DownloadChunkSize = 256 * KB;

        // CHANGE THIS with full path to the file you want to upload
        private const string UploadDirName = @"C:\Upload\";

        // CHANGE THIS with a download directory
        private const string DownloadDirectoryName = @"C:\Download\";

        // CHANGE THIS if you upload a file type other than a jpg
        //private const string ContentType="application/octetstream";

        #endregion

        /// <summary>The logger instance.</summary>
        private static readonly ILogger Logger;

        /// <summary>The Drive API scopes.</summary>
        private static readonly string[] Scopes = new[] { DriveService.Scope.DriveFile, DriveService.Scope.Drive };

        /// <summary>
        /// The file which was uploaded. We will use its download Url to download it using our media downloader object.
        /// </summary>
        private static File uploadedFile;

        static void Main(string[] args)
        {
            Console.WriteLine("Google Drive API Sample");
            try
            {
                new Program().Run().Wait();
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.WriteLine("ERROR: " + e.Message);
                }
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private async Task Run()
        {
            GoogleWebAuthorizationBroker.Folder = "Drive.Sample";
            UserCredential credential;
            using (var stream = new System.IO.FileStream("client_secrets.json",
                System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets, Scopes, "user", CancellationToken.None);
            }
            Console.WriteLine("-1");
            // Create the service.
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Drive API Sample",
            });

            /*
            Console.WriteLine("-2");
            string parent = GetFolderID(service, "C",null);
            Console.WriteLine(parent);

            File fp = GetFolderInParents(service, "1", parent);
            Console.WriteLine(fp==null?"does not exists":fp.Title);

            fp = GetFolderInParents(service, "2", fp.Id);
            Console.WriteLine(fp == null ? "does not exists" : fp.Title);
            */

            
          //  await UploadFileAsync(service, @"C:\Temp\GAPI\2.txt");

            await EachUploadDirectory(service, UploadDirName, UploadDirName, "*.*", "*");
           return;

            // uploaded succeeded
          //  Console.WriteLine("\"{0}\" was uploaded successfully", uploadedFile.Title);
          //  await DownloadFile(service, uploadedFile==null?null:uploadedFile.DownloadUrl);
      //      await DeleteFile(service, uploadedFile);
        }
        public async Task EachUploadDirectory(DriveService service, string DirRoot, string UploadDirName, string FileFilter, string DirFilter)
        {
            foreach (string file in System.IO.Directory.GetFiles(UploadDirName, "*.*"))
            {
                string lg = file + ";" + "start:" + DateTime.Now.ToString("dd.MM.yyyy hh:mm:ss");
                Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy hh:mm:ss"));
                Console.WriteLine(file);
                currentfullFileSize = getFileSize(file);
                if (currentfullFileSize > 0)
                {
                    lg += ";" + "comment:";
                    await CheckFileAsync(service, file, DirRoot);
                }
                else
                {
                    lg += ";" + "comment:file empty";
                }
                lg += ";" + "end:" + DateTime.Now.ToString("dd.MM.yyyy hh:mm:ss");
                System.IO.StreamWriter sw = new System.IO.StreamWriter("synclog.log", true);
                sw.WriteLine(lg);
                sw.Close();
            }
            foreach (string dir in System.IO.Directory.GetDirectories(UploadDirName, DirFilter))
            {
                await EachUploadDirectory(service, DirRoot, dir, FileFilter, DirFilter);
            }
        }

        public File GetFolderInParents(DriveService service, string FolderTitle,string ParentID)
        {
            ChildrenResource.ListRequest request = service.Children.List(ParentID);

            request.Q = "'" + ParentID + "' in parents";

            do
            {
                try
                {
                    ChildList children = request.Execute();

                    foreach (ChildReference child in children.Items)
                    {
                        File f =GetFileByID(child.Id,service);
                        if (f.MimeType.Equals("application/vnd.google-apps.folder")&&f.Title.Equals(FolderTitle))
                        {
                            return f;
                        }
                    }

                    request.PageToken = children.NextPageToken;

                }
                catch
                {
                    request.PageToken = null;
                }
            } while (!String.IsNullOrEmpty(request.PageToken));
            return null;
        }
        public File GetFileInParents(DriveService service, string FolderTitle, string ParentID)
        {
            ChildrenResource.ListRequest request = service.Children.List(ParentID);

            request.Q = "'" + ParentID + "' in parents";

            do
            {
                try
                {
                    ChildList children = request.Execute();

                    foreach (ChildReference child in children.Items)
                    {
                        File f = GetFileByID(child.Id, service);
                        if (!f.MimeType.Equals("application/vnd.google-apps.folder") && f.Title.Equals(FolderTitle))
                        {
                            return f;
                        }
                    }

                    request.PageToken = children.NextPageToken;

                }
                catch
                {
                    request.PageToken = null;
                }
            } while (!String.IsNullOrEmpty(request.PageToken));
            return null;
        }
        private File GetFileByID(string fileID, DriveService service)
        {
            File file = service.Files.Get(fileID).Execute();
            if (file.ExplicitlyTrashed == null)
                return file;
            return null;
        }
        public string GetFolderID(DriveService service, string FolderName,string parent)
        {
            FilesResource.ListRequest request = service.Files.List();
            if (string.IsNullOrEmpty(parent))
            {
                request.Q = "mimeType='application/vnd.google-apps.folder' and trashed=false and title = '" + FolderName + "'";
            }
            else { 
                request.Q = "mimeType='application/vnd.google-apps.folder' and trashed=false and title = '" + FolderName + "' and parent='"+parent+"'"; 
            }
            FileList files = request.Execute();
            return files.Items[0].Id;
        }
        public File CreateFolder(DriveService service, string FolderName, string FolderDescription, string parentId)
        {
            File body = new File();
            body.Title = FolderName;
            body.Description = FolderDescription;
            body.MimeType = "application/vnd.google-apps.folder";
            body.Parents = new List<ParentReference> { new ParentReference() { Id = parentId } };
            try
            {
                FilesResource.InsertRequest request = service.Files.Insert(body);
                return request.Execute();                
            }
            catch (Exception e)
            {
                Console.WriteLine("An error occurred: " + e.Message);
                Console.WriteLine(e.ToString());
            }
            return null;
        }
        public string GetFolderIdORCreateIfNotExists(DriveService service, string fullPath, string name)
        {
            FilesResource.ListRequest request = service.Files.List();
            request.MaxResults = 1000;
            request.Q = "mimeType='application/vnd.google-apps.folder' and trashed=false and title='" + name + "'";
            List<File> result = new List<File>();
            do
            {
                try
                {
                    FileList files = request.Execute();
                    result.AddRange(files.Items);
                    request.PageToken = files.NextPageToken;
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred: " + e.Message);
                    request.PageToken = null;
                }
            } while (!String.IsNullOrEmpty(request.PageToken));
            return null;

            /*
              
               Console.WriteLine("-3");
            // File's metadata.
            File body = new File();
            body.Title = "NewDirectory2";
            body.Description = "Test Directory";
            body.MimeType = "application/vnd.google-apps.folder";
            body.Parents = new List<ParentReference> { new ParentReference() { Id = "root" } };
            string idparentdir = null;
            Console.WriteLine("-4");
            try
            {
                FilesResource.InsertRequest request = service.Files.Insert(body);
                idparentdir = (request.Execute()).Id;
                Console.WriteLine(idparentdir);
                Console.WriteLine("-5");
            }
            catch (Exception e)
            {
                Console.WriteLine("An error occurred: " + e.Message);
                Console.WriteLine(e.ToString());
            }


            Console.WriteLine("-6");
              
              
              
              */
        }
        private async Task CheckFileAsync(DriveService service, string UploadFileName, string DirRoot)
        {

            string idparentdir = GetFolderID(service, "C", null);

            if (UploadFileName.LastIndexOf('\\') + 1 > DirRoot.Length)
            {
                foreach (string parentpath in UploadFileName.Substring(DirRoot.Length, UploadFileName.LastIndexOf('\\') - DirRoot.Length).Split('\\'))
                {

                    File fp = GetFolderInParents(service, parentpath, idparentdir);
                    if (fp == null)
                    {
                        File crDir = CreateFolder(service, parentpath, "", idparentdir);
                        if (crDir != null)
                        {
                            idparentdir = crDir.Id;
                            Console.WriteLine("cr:"+crDir.Title);
                        }
                        else
                        {
                            throw new EntryPointNotFoundException("Error Creation Directory: " + parentpath + " in " + idparentdir);
                        }
                    }
                    else
                    {
                        idparentdir = fp.Id;
                        Console.WriteLine("ex:"+fp.Title);
                    }

                }
            }


            string title = UploadFileName;
            if (title.LastIndexOf('\\') != -1)
            {
                title = title.Substring(title.LastIndexOf('\\') + 1);
            }


            if (title.IndexOf("part") != -1 && title.EndsWith(".rar"))
            {
                title = title.Substring(0, title.IndexOf("part")) + title.Substring(title.IndexOf("part") + 4, (title.LastIndexOf(".") - (title.IndexOf("part") + 4)));// + title.Substring(title.Length-4));
            }
            File existsFile = GetFileInParents(service, title, idparentdir);

            if (existsFile == null)
            {
                Console.WriteLine("FILE: '" + UploadFileName + "'; title: "+title);
                await UploadFileAsync(service, UploadFileName, title, idparentdir);
            }
            else
            {
                Console.WriteLine("FILE '" + UploadFileName + "' Exists");
            }
        }
        /// <summary>Uploads file asynchronously.</summary>
        private Task<IUploadProgress> UploadFileAsync(DriveService service, string UploadFileName, string _Title, string DirRootId)
        {
           
            var uploadStream = new System.IO.FileStream(UploadFileName, System.IO.FileMode.Open,
                System.IO.FileAccess.Read);

           
            FilesResource.InsertMediaUpload insert;


            insert = service.Files.Insert(new File { Title = _Title, Parents = new List<ParentReference> { new ParentReference() { Id = DirRootId } } }, uploadStream, GetContentType(UploadFileName));
          
            
            var task=insert.UploadAsync();
            
            insert.ChunkSize = FilesResource.InsertMediaUpload.MinimumChunkSize * 2;
                insert.ProgressChanged += Upload_ProgressChanged;
                insert.ResponseReceived += Upload_ResponseReceived;


                task.ContinueWith(t =>
                {
                    // NotOnRanToCompletion - this code will be called if the upload fails
                    Console.WriteLine("Upload Filed. " + t.Exception);
                }, TaskContinuationOptions.NotOnRanToCompletion);
                task.ContinueWith(t =>
                {
                    Logger.Debug("Closing the stream");
                    uploadStream.Dispose();
                    Logger.Debug("The stream was closed");
                });
            
            return task;

        }

        /// <summary>Downloads the media from the given URL.</summary>
        private async Task DownloadFile(DriveService service, string url)
        {
            List<File> result = new List<File>();
            FilesResource.ListRequest request = service.Files.List();
            request.Q = "title='test.test' and trashed=false";
            do
            {
                try
                {
                    FileList files = request.Execute();
                    result.AddRange(files.Items);
                    request.PageToken = files.NextPageToken;
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred: " + e.Message);
                    request.PageToken = null;
                }
            } while (!String.IsNullOrEmpty(request.PageToken));
            //Loop though the files returned.



            foreach (Google.Apis.Drive.v2.Data.File file in result)
            {
                try
                {
                    // FilesResource.GetRequest getFile = service.Files.Get(file.Id);
                    //  System.IO.Stream stream = getFile.ExecuteAsStream();
                    Console.WriteLine(file.Title);
                    Console.WriteLine(file.DownloadUrl);




                    var downloader = new MediaDownloader(service);
                    downloader.ChunkSize = DownloadChunkSize;
                    // add a delegate for the progress changed event for writing to console on changes
                    downloader.ProgressChanged += Download_ProgressChanged;

                    // figure out the right file type base on UploadFileName extension
                  //  var lastDot = file.LastIndexOf('.');
                    var fileName = DownloadDirectoryName + @"\Download\" + file.Title;
                     //   (lastDot != -1 ? "." + UploadFileName.Substring(lastDot + 1) : "");
                    using (var fileStream = new System.IO.FileStream(fileName,
                        System.IO.FileMode.Create, System.IO.FileAccess.Write))
                    {
                        var progress = await downloader.DownloadAsync(file.DownloadUrl, fileStream);
                        if (progress.Status == DownloadStatus.Completed)
                        {
                            Console.WriteLine(fileName + " was downloaded successfully");
                        }
                        else
                        {
                            Console.WriteLine("Download {0} was interpreted in the middle. Only {1} were downloaded. ",
                                fileName, progress.BytesDownloaded);
                        }
                    }
                }
                catch (Exception e)
                {
                    // An error occurred.
                    Console.WriteLine("An error occurred: " + e.Message);
                }
            }
        }

        /// <summary>Deletes the given file from drive (not the file system).</summary>
        private async Task DeleteFile(DriveService service, File file)
        {
            Console.WriteLine("Deleting file '{0}'...", file.Id);
            await service.Files.Delete(file.Id).ExecuteAsync();
            Console.WriteLine("File was deleted successfully");
        }

        #region Progress and Response changes

        static void Download_ProgressChanged(IDownloadProgress progress)
        {
            Console.WriteLine(progress.Status + " " + progress.BytesDownloaded);
        }

        static void Upload_ProgressChanged(IUploadProgress progress)
        {
            Console.WriteLine(progress.Status + " " + progress.BytesSent + "(" + (progress.BytesSent * 100) / currentfullFileSize + "%)");
        }

        static void Upload_ResponseReceived(File file)
        {
            uploadedFile = file;
        }

        #endregion
    }
}