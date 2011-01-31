using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace CardBrowser
{
    public class SelfUpdater
    {
        private Thread updateQueryThread;
        private IWin32Window owner;
        private string versionsUrl;
        private string applicationName;
        private string downloadUrl;
        private Version currentVersion;
        private Version serverVersion;

        public SelfUpdater(IWin32Window owner)
        {
            this.owner = owner;
        }

        // Delegates called by UpdateQueryThread
        private delegate bool CheckCompleteDelegate(bool foundNewVersion);

        private delegate void DownloadCompleteDelegate(bool downloaded, string filePath);

        public void CheckForUpdate(string versionsUrl, string applicationName)
        {
            // Check if the thread is currently running
            if ((updateQueryThread != null) && updateQueryThread.IsAlive)
            {
                return;
            }

            this.versionsUrl = versionsUrl;
            this.applicationName = applicationName;

            // Start the worker thread
            updateQueryThread = new Thread(new ThreadStart(UpdateQueryThread));
            updateQueryThread.Name = "UpdateQueryThread";
            updateQueryThread.Start();
        }

        private void UpdateQueryThread()
        {
            try
            {
                // Check if a new version is availale
                bool newVersion = CheckForNewVersion();

                // Inform about the status and ask to download the newest version
                // Invoke the request on the Owners GUI thread
                Control c = owner as Control;
                bool download = (bool)c.Invoke(new CheckCompleteDelegate(OnCheckComplete), new object[] { newVersion });

                if (!download)
                {
                    return;
                }

                string filePath;
                bool downloadSuccess = true;

                try
                {
                    filePath = DownloadInstaller();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);

                    filePath = null;
                    downloadSuccess = false;
                }

                // Inform about the status and execute the installer
                c.BeginInvoke(new DownloadCompleteDelegate(OnDownloadComplete), new object[] { downloadSuccess, filePath });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private string DownloadInstaller()
        {
            // Get temp local file name
            string localPath = Path.Combine(Path.GetTempPath(), applicationName + ".msi");
            Debug.WriteLine(String.Format("Downloading file to: {0}", localPath));

            // Function will return the number of bytes processed to the caller. Initialize to 0 here.
            int bytesProcessed = 0;

            // Assign values to these objects here so that they can be referenced in the finally block
            Stream remoteStream = null;
            Stream localStream = null;
            WebResponse response = null;

            // Use a try/catch/finally block as both the WebRequest and Stream classes throw exceptions upon error
            try
            {
                // Create a request for the specified remote file name
                WebRequest request = WebRequest.Create(downloadUrl);

                if (request != null)
                {
                    // Send the request to the server and retrieve the WebResponse object
                    response = request.GetResponse();

                    if (response != null)
                    {
                        // Once the WebResponse object has been retrieved, get the stream object associated with the response's data
                        remoteStream = response.GetResponseStream();

                        // Create the local file
                        localStream = File.Create(localPath);

                        // Allocate a 1k buffer
                        byte[] buffer = new byte[1024];
                        int bytesRead;

                        // Simple do/while loop to read from stream until no bytes are returned
                        do
                        {
                            // Read data (up to 1k) from the stream
                            bytesRead = remoteStream.Read(buffer, 0, buffer.Length);

                            // Write the data to the local file
                            localStream.Write(buffer, 0, bytesRead);

                            // Increment total bytes processed
                            bytesProcessed += bytesRead;
                        }
                        while (bytesRead > 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                // Close the response and streams objects here to make sure they're closed even if an exception is thrown at some point
                if (response != null)
                {
                    response.Close();
                }

                if (remoteStream != null) 
                {
                    remoteStream.Close();
                }

                if (localStream != null)
                {
                    localStream.Close();
                }
            }

            // Return total bytes processed to caller.
            ////return bytesProcessed;

            return localPath;
        }

        private bool OnCheckComplete(bool foundNewVersion)
        {
            // Called after checking for new version is compelted
            // Returns true if the user wants to download the newest version
            if (!foundNewVersion)
            {
                ////MessageBox.Show(owner, "No updates available", "Check for update");
                return false;
            }

            ////return DialogResult.Yes == MessageBox.Show(owner, "Download new version?", "Check for update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            
            // If the user is sure
            ////return TaskDialogResult.Yes == TaskDialog.Show(owner, string.Format("Current version: {0}\nNew version: {1}", currentVersion.ToString(), serverVersion.ToString()), "Would you like to update?", "Check for update", TaskDialogButtons.Yes | TaskDialogButtons.No, TaskDialogIcon.Information);
            return TaskDialogResult.Yes == TaskDialog.Show(owner, "Would you like to perform an update?", "New version available", "Check for update", TaskDialogButtons.Yes | TaskDialogButtons.No, TaskDialogIcon.Information);
        }

        private void OnDownloadComplete(bool downloaded, string filePath)
        {
            // Called after the new version has been downloaded, installs it
            if (!downloaded)
            {
                MessageBox.Show(owner, "Download error", "Downloading new version", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                try
                {
                    Process.Start(filePath);
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);

                    try
                    {
                        File.Delete(filePath);
                    }
                    catch
                    {
                    }

                    MessageBox.Show(owner, "Error executing the installer", "Installing new version", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private bool CheckForNewVersion()
        {
            try
            {
                // Provide the XmlTextReader with the URL of our xml document  
                XmlTextReader reader = new XmlTextReader(versionsUrl);

                // Simply (and easily) skip the junk at the beginning  
                reader.MoveToContent();

                // Internal - as the XmlTextReader moves only forward, we save current xml element name in elementName variable.
                // When we parse a text node, we refer to elementName to check what was the node name  
                string elementName = string.Empty;

                // We check if the xml starts with a proper "Software" root node  
                if ((reader.NodeType == XmlNodeType.Element) && (reader.Name == "Software"))
                {
                    while (reader.Read())
                    {
                        // We check if the xml starts with a proper "ApplicationName" element node  
                        if ((reader.NodeType == XmlNodeType.Element) && (reader.Name == applicationName))
                        {
                            while (reader.Read())
                            {
                                // When we find an element node, we remember its name  
                                if (reader.NodeType == XmlNodeType.Element)
                                {
                                    elementName = reader.Name;
                                }
                                else
                                {
                                    if ((reader.NodeType == XmlNodeType.Text) && reader.HasValue)
                                    {
                                        // We check what the name of the node was  
                                        switch (elementName)
                                        {
                                            case "version":
                                                // Thats why we keep the version info in xxx.xxx.xxx.xxx format  
                                                // the Version class does the parsing for us  
                                                serverVersion = new Version(reader.Value);
                                                break;

                                            case "url":
                                                downloadUrl = reader.Value;
                                                break;
                                        }
                                    }
                                    if (reader.NodeType == XmlNodeType.EndElement && reader.Name == applicationName)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                reader.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            // Get the running version  
            currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            Debug.WriteLine(String.Format("Current version: {0}, Server version: {1}", currentVersion.ToString(), serverVersion.ToString()));
                
            // Compare the versions  
            if (currentVersion.CompareTo(serverVersion) < 0)
            {
                return true;
            }

            return false;
        }
    }
}
