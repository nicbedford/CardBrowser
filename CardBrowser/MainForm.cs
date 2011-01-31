using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Mono.Security;
using PCSC;
using System.Diagnostics;

namespace CardBrowser
{
    public partial class MainForm : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string partList);

        private PCSCReader cardReader;
        private Thread readThread = null;
        private XDocument tagsDocument = null;

        // For cross-thread opertions
        private delegate void UpdateTreeViewDelegate(TreeView treeView);
        private delegate void UpdateStatusLabelDelegate(string status);
        private delegate void UpdateHourglassDelegate(bool status);
        private delegate void ClearTreeViewDelegate();

        public MainForm()
        {
            InitializeComponent();
            SetWindowTheme(treeViewData.Handle, "explorer", null);

            Assembly a = Assembly.GetExecutingAssembly();
            string[] resourceNames = a.GetManifestResourceNames();
            Stream stream = a.GetManifestResourceStream("CardBrowser.TagList.xml");

            XmlTextReader reader = new XmlTextReader(stream);
            tagsDocument = XDocument.Load(reader);

            lblTag.Text = String.Empty;
            lblDescription.Text = String.Empty;
            lblLength.Text = String.Empty;

            // Start update check timer
            timerUpdateCheck.Enabled = true;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            cardReader = new PCSCReader();

            cardReader.CardInserted += new PCSCReader.CardInsertedEventHandler(cardReader_CardInserted);
            cardReader.CardRemoved += new PCSCReader.CardRemovedEventHandler(cardReader_CardRemoved);

            foreach (string reader in cardReader.Readers)
            {
                toolStripComboBoxReaders.Items.Add(reader);
            }

            try
            {
                toolStripComboBoxReaders.SelectedIndex = 0;
            }
            catch(ArgumentOutOfRangeException)
            {
                // No smart card readers!
            }
        }

        void cardReader_CardRemoved(string reader)
        {
            UpdateStatusLabel("Card removed from reader: " + reader);
            ClearTreeView();
        }

        void cardReader_CardInserted(string reader, byte[] atr)
        {
            StringBuilder sb = new StringBuilder();

            foreach (byte b in atr)
            {
                sb.AppendFormat("{0:X2}", b);
            }

            UpdateStatusLabel("Card inserted in reader: " + reader);
        }

        private void AddRecordNodes(ASN1 asn, TreeNode parentNode)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in asn.Tag)
            {
                sb.AppendFormat("{0:X2}", b);
            }

            TreeNode node = new TreeNode(sb.ToString());
            node.Tag = asn;
            node.ImageIndex = 6;
            node.SelectedImageIndex = 6;
            parentNode.Nodes.Add(node);

            if (asn.Count > 0)
            {
                foreach (ASN1 a in asn)
                {
                    AddRecordNodes(a, node);
                }
            }
        }

        private void treeViewData_AfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeNode node = ((TreeView)sender).SelectedNode;
            txtASCII.Text = String.Empty;
            ASN1 asn = node.Tag as ASN1;

            if (asn != null)
            {
                StringBuilder sb = new StringBuilder();

                foreach (byte b in asn.Tag)
                {
                    sb.AppendFormat("{0:X2}", b);
                }

                // Get tag description
                XElement tagElement = tagsDocument.Descendants().Where(el => el.Attributes().Any(a => a.Name == "Tag" && a.Value == sb.ToString())).FirstOrDefault();

                lblTag.Text = sb.ToString();

                if (tagElement != null)
                {
                    lblDescription.Text = tagElement.Attribute("Description").Value;
                }
                else
                {
                    lblDescription.Text = "None";
                }

                sb = new StringBuilder();

                lblLength.Text = asn.Length.ToString();

                foreach (byte b in asn.Value)
                {
                    sb.AppendFormat("{0:X2}", b);
                }

                txtData.Text = sb.ToString();
            }
            else
            {
                lblTag.Text = String.Empty;
                lblDescription.Text = String.Empty;
                lblLength.Text = String.Empty;
                txtData.Text = String.Empty;
            }
        }

        private void btnASCII_Click(object sender, EventArgs e)
        {
            ASCIIEncoding encoding = new ASCIIEncoding();

            byte[] result = new byte[txtData.Text.Length / 2];

            for (int i = 0; i < txtData.Text.Length; i += 2)
            {
                result[i / 2] = byte.Parse(txtData.Text.Substring(i, 2), NumberStyles.HexNumber);
            }

            txtASCII.Text = encoding.GetString(result);
        }

        private void toolStripButtonReadCard_Click(object sender, EventArgs e)
        {
            ClearTreeView();
            readThread = new Thread(ReadCard);
            readThread.Start(toolStripComboBoxReaders.SelectedItem.ToString());
        }

        private void toolStripButtonSave_Click(object sender, EventArgs e)
        {
            XDocument saveDocument = new XDocument();
            XElement rootElement = null;

            saveDocument.Add(rootElement = new XElement("card"));

            TreeNodeCollection nodes = treeViewData.Nodes;

            foreach (TreeNode n in nodes)
            {
                BuildDocument(n, ref rootElement);
            }

            SaveFileDialog sfd = new SaveFileDialog();

            if (sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                saveDocument.Save(sfd.FileName);
            }
        }

        private void ReadCard(object o)
        {
            UseHourglass(true);
            string selectedReader = o as string;
            UpdateStatusLabel("Reading card...");

            TreeView treeView = new TreeView();
            TreeNode cardNode = new TreeNode("Card");
            cardNode.ImageIndex = 0;
            cardNode.SelectedImageIndex = 0;
            treeView.Nodes.Add(cardNode);

            // Tree nodes
            TreeNode pseNode = null;
            TreeNode fciNode = null;
            ASN1 fci = null;
            List<byte[]> pseIdentifiers = new List<byte[]>();
            List<byte[]> applicationIdentifiers = new List<byte[]>();
            ASCIIEncoding encoding = new ASCIIEncoding();
            APDUCommand apdu = null;
            APDUResponse response = null;
            bool pseFound = false;

            if (!skipPSEToolStripMenuItem.Checked)
            {
                pseIdentifiers.Add(encoding.GetBytes("1PAY.SYS.DDF01"));
                pseIdentifiers.Add(encoding.GetBytes("2PAY.SYS.DDF01"));
            }

            try
            {
                // Now lets process all Payment System Environments
                if (pseIdentifiers.Count > 0)
                {
                    cardReader.Connect(selectedReader);

                    foreach (byte[] pse in pseIdentifiers)
                    {
                        apdu = new APDUCommand(0x00, 0xA4, 0x04, 0x00, pse, (byte)pse.Length);
                        response = cardReader.Transmit(apdu);

                        // Get response nescesary
                        if (response.SW1 == 0x61)
                        {
                            apdu = new APDUCommand(0x00, 0xC0, 0x00, 0x00, null, response.SW2);
                            response = cardReader.Transmit(apdu);
                        }

                        // PSE application read found ok
                        if (response.SW1 == 0x90)
                        {
                            pseFound = true;

                            pseNode = new TreeNode(String.Format("Application {0}", encoding.GetString(pse)));
                            pseNode.ImageIndex = 1;
                            pseNode.SelectedImageIndex = 1;
                            pseNode.Tag = pse;
                            cardNode.Nodes.Add(pseNode);

                            fciNode = new TreeNode("File Control Information");
                            fciNode.ImageIndex = 3;
                            fciNode.SelectedImageIndex = 3;
                            fciNode.Tag = "fci";
                            pseNode.Nodes.Add(fciNode);

                            fci = new ASN1(response.Data);
                            AddRecordNodes(fci, fciNode);

                            byte sfi = new ASN1(response.Data).Find(0x88).Value[0];
                            byte recordNumber = 0x01;
                            byte p2 = (byte)((sfi << 3) | 4);

                            TreeNode efDirNode = new TreeNode(String.Format("EF Directory - {0:X2}", sfi));
                            efDirNode.ImageIndex = 2;
                            efDirNode.SelectedImageIndex = 2;
                            efDirNode.Tag = sfi;
                            pseNode.Nodes.Add(efDirNode);


                            while (response.SW1 != 0x6A && response.SW2 != 0x83)
                            {
                                apdu = new APDUCommand(0x00, 0xB2, recordNumber, p2, null, 0x00);
                                response = cardReader.Transmit(apdu);

                                // Retry with correct length
                                if (response.SW1 == 0x6C)
                                {
                                    apdu = new APDUCommand(0x00, 0xB2, recordNumber, p2, null, response.SW2);
                                    response = cardReader.Transmit(apdu);
                                }

                                if (response.SW1 == 0x61)
                                {
                                    apdu = new APDUCommand(0x00, 0xC0, 0x00, 0x00, null, response.SW2);
                                    response = cardReader.Transmit(apdu);
                                }

                                if (response.Data != null)
                                {
                                    TreeNode recordNode = new TreeNode(String.Format("Record - {0:X2}", recordNumber));
                                    recordNode.ImageIndex = 4;
                                    recordNode.SelectedImageIndex = 4;
                                    recordNode.Tag = recordNumber;
                                    efDirNode.Nodes.Add(recordNode);

                                    ASN1 aef = new ASN1(response.Data);
                                    AddRecordNodes(aef, recordNode);

                                    foreach (ASN1 appTemplate in aef)
                                    {
                                        // Check we really have an Application Template
                                        if (appTemplate.Tag[0] == 0x61)
                                        {
                                            applicationIdentifiers.Add(appTemplate.Find(0x4f).Value);
                                        }
                                    }
                                }

                                recordNumber++;
                            }
                        }

                        if (pseFound)
                            break;
                    }

                    cardReader.Disconnect();
                }

                // We couldn't read the AID's from the PSE, so we'll just try querying all ADI's we know about
                if (!pseFound)
                {
                    // From http://www.darkc0de.com/others/ChAP.py
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000003"));         // VISA
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000031010"));     // VISA Debit/Credit
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000003101001"));   // VISA Credit
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000003101002"));   // VISA Debit
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000032010"));     // VISA Electron
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000033010"));     // VISA Interlink
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000038010"));     // VISA Plus
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000003999910"));   // VISA ATM

                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000041010"));     // Mastercard
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000048010"));     // Cirrus
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000043060"));     // Maestro
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000050001"));     // Maestro UK
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A00000002401"));       // Self Service
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000025"));         // American Express
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000025010104"));   // American Express
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A000000025010701"));   // ExpressPay
                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000291010"));     // Link

                    applicationIdentifiers.Add(Helpers.HexStringToBytes("B012345678"));         // Maestro TEST

                    applicationIdentifiers.Add(Helpers.HexStringToBytes("A0000000651010"));     // JCB
                }

                // Now lets process all of the AID's we found
                if (applicationIdentifiers.Count > 0)
                {
                    foreach (byte[] AID in applicationIdentifiers)
                    {
                        List<ApplicationFileLocator> applicationFileLocators = new List<ApplicationFileLocator>();
                        StringBuilder sb = new StringBuilder();

                        cardReader.Connect(selectedReader);

                        // Select AID
                        apdu = new APDUCommand(0x00, 0xA4, 0x04, 0x00, AID, (byte)AID.Length);
                        response = cardReader.Transmit(apdu);

                        // Get response nescesary
                        if (response.SW1 == 0x61)
                        {
                            apdu = new APDUCommand(0x00, 0xC0, 0x00, 0x00, null, response.SW2);
                            response = cardReader.Transmit(apdu);
                        }

                        // Application not found
                        if (response.SW1 == 0x6A && response.SW2 == 0x82)
                            continue;

                        if (response.SW1 == 0x90)
                        {
                            foreach (byte b in AID)
                            {
                                sb.AppendFormat("{0:X2}", b);
                            }

                            TreeNode applicationNode = new TreeNode(String.Format("Application {0}", sb.ToString()));
                            applicationNode.ImageIndex = 1;
                            applicationNode.SelectedImageIndex = 1;
                            applicationNode.Tag = AID;
                            cardNode.Nodes.Add(applicationNode);

                            fciNode = new TreeNode("File Control Information");
                            fciNode.ImageIndex = 3;
                            fciNode.SelectedImageIndex = 3;
                            fciNode.Tag = "fci";
                            applicationNode.Nodes.Add(fciNode);

                            fci = new ASN1(response.Data);
                            AddRecordNodes(fci, fciNode);

                            // Get processing options (with empty PDOL)
                            apdu = new APDUCommand(0x80, 0xA8, 0x00, 0x00, new byte[] { 0x83, 0x00 }, 0x02);
                            response = cardReader.Transmit(apdu);

                            // Get response nescesary
                            if (response.SW1 == 0x61)
                            {
                                apdu = new APDUCommand(0x00, 0xC0, 0x00, 0x00, null, response.SW2);
                                response = cardReader.Transmit(apdu);
                            }

                            if (response.SW1 == 0x90)
                            {
                                ASN1 template = new ASN1(response.Data);
                                ASN1 aip = null;
                                ASN1 afl = null;

                                // Primative response (Template Format 1)
                                if (template.Tag[0] == 0x80)
                                {
                                    byte[] tempAIP = new byte[2];
                                    Buffer.BlockCopy(template.Value, 0, tempAIP, 0, 2);
                                    aip = new ASN1(0x82, tempAIP);

                                    byte[] tempAFL = new byte[template.Length - 2];
                                    Buffer.BlockCopy(template.Value, 2, tempAFL, 0, template.Length - 2);
                                    afl = new ASN1(0x94, tempAFL);
                                }

                                // constructed data object response (Template Format 2)
                                if (template.Tag[0] == 0x77)
                                {
                                    aip = template.Find(0x82);
                                    afl = template.Find(0x94);
                                }

                                // Chop up AFL's
                                for (int i = 0; i < afl.Length; i += 4)
                                {
                                    byte[] AFL = new byte[4];
                                    Buffer.BlockCopy(afl.Value, i, AFL, 0, 4);

                                    ApplicationFileLocator fileLocator = new ApplicationFileLocator(AFL);
                                    applicationFileLocators.Add(fileLocator);
                                }

                                TreeNode aipaflNode = new TreeNode("Application Interchange Profile - Application File Locator");
                                aipaflNode.ImageIndex = 3;
                                aipaflNode.SelectedImageIndex = 3;
                                aipaflNode.Tag = "aip";
                                applicationNode.Nodes.Add(aipaflNode);

                                ASN1 aipafl = new ASN1(response.Data);
                                AddRecordNodes(aipafl, aipaflNode);

                                foreach (ApplicationFileLocator file in applicationFileLocators)
                                {
                                    int r = file.FirstRecord;// +afl.OfflineRecords;     // We'll read SDA records too
                                    int lr = file.LastRecord;

                                    byte p2 = (byte)((file.SFI << 3) | 4);

                                    TreeNode efNode = new TreeNode(String.Format("Elementary File - {0:X2}", file.SFI));
                                    efNode.ImageIndex = 2;
                                    efNode.SelectedImageIndex = 2;
                                    efNode.Tag = file.SFI;
                                    applicationNode.Nodes.Add(efNode);

                                    while (r <= lr)
                                    {
                                        apdu = new APDUCommand(0x00, 0xB2, (byte)r, p2, null, 0x00);
                                        response = cardReader.Transmit(apdu);

                                        // Retry with correct length
                                        if (response.SW1 == 0x6C)
                                        {
                                            apdu = new APDUCommand(0x00, 0xB2, (byte)r, p2, null, response.SW2);
                                            response = cardReader.Transmit(apdu);
                                        }

                                        TreeNode recordNode = new TreeNode(String.Format(" Record - {0:X2}", r));

                                        if (r <= file.OfflineRecords)
                                        {
                                            recordNode.ImageIndex = 5;
                                            recordNode.SelectedImageIndex = 5;
                                        }
                                        else
                                        {
                                            recordNode.ImageIndex = 4;
                                            recordNode.SelectedImageIndex = 4;
                                        }

                                        recordNode.Tag = r;
                                        efNode.Nodes.Add(recordNode);

                                        ASN1 record = new ASN1(response.Data);
                                        AddRecordNodes(record, recordNode);

                                        r++;
                                    }
                                }

                                //IEnumerable<XElement> tags = tagsDocument.Descendants().Where(el => el.Name == "Tag");
                                //foreach (XElement element in tags)
                                //{
                                //    string tag = element.Attribute("Tag").Value;

                                //    // Only try GET_DATA on two byte tags
                                //    if (tag.Length == 4)
                                //    {
                                //        byte p1 = byte.Parse(tag.Substring(0, 2), NumberStyles.HexNumber);
                                //        byte p2 = byte.Parse(tag.Substring(2, 2), NumberStyles.HexNumber);

                                //        apdu = new APDUCommand(0x80, 0xCA, p1, p2, null, 0);
                                //        response = cardReader.Transmit(apdu);

                                //        if (response.SW1 == 0x90)
                                //        {
                                //            Debug.WriteLine(response.ToString());
                                //        }
                                //    }
                                //}

                                apdu = new APDUCommand(0x80, 0xCA, 0x9f, 0x13, null, 0);
                                response = cardReader.Transmit(apdu);
                                Debug.WriteLine(response.ToString());
                                apdu = new APDUCommand(0x80, 0xCA, 0x9f, 0x17, null, 0);
                                response = cardReader.Transmit(apdu);
                                Debug.WriteLine(response.ToString());
                                apdu = new APDUCommand(0x80, 0xCA, 0x9f, 0x36, null, 0);
                                response = cardReader.Transmit(apdu);
                                Debug.WriteLine(response.ToString());
                            }
                            else
                            {
                                // Unexpected status word
                                UpdateStatusLabel(String.Format("Unexpected response from GET PROCESSING OPTIONS command: 0x{0:X2}{1:X2}", response.SW1, response.SW2));
                            }
                        }
                        else
                        {
                            // Unexpected status word
                            UpdateStatusLabel(String.Format("Unexpected response from SELECT command: 0x{0:X2}{1:X2}", response.SW1, response.SW2));
                        }

                        cardReader.Disconnect();
                    }
                }

                treeViewData.Invoke(new UpdateTreeViewDelegate(UpdateTreeView), new object[] { treeView });
            }
            catch (PCSCException ex)
            {
                UpdateStatusLabel(ex.Message);
                return;
            }
            finally
            {
                UseHourglass(false);
                UpdateStatusLabel("Ready");
            }
        }

        private void UpdateTreeView(TreeView treeView)
        {
            foreach (TreeNode node in treeView.Nodes)
            {
                TreeNode clonedNode = (TreeNode)node.Clone();
                treeViewData.Nodes.Add(clonedNode);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (readThread != null)
            {
                readThread.Abort();
            }
        }

        private void BuildDocument(TreeNode treeNode, ref XElement element)
        {
            XElement childElement = null;
            ASN1 tlv = treeNode.Tag as ASN1;

            if (tlv == null)
            {
                // Need to special case the non TLV data

                // Use icon as index to type of special case node!
                switch (treeNode.ImageIndex)
                {
                    case 0:
                        // Card - this is the root node
                        childElement = element;
                        break;
                    case 1:
                        //Application
                        byte[] aid = treeNode.Tag as byte[];
                        StringBuilder sb = new StringBuilder();

                        foreach (byte b in aid)
                        {
                            sb.AppendFormat("{0:X2}", b);
                        }

                        element.Add(childElement = new XElement("application", new XAttribute("aid", sb.ToString())));
                        break;

                    case 2:
                        // Elementary File
                        element.Add(childElement = new XElement("elementaryFile", new XAttribute("sfi", treeNode.Tag.ToString())));
                        break;

                    case 3:
                        // File Control Information
                        // or AIPAFL!
                        string identifier = treeNode.Tag as string;

                        switch (identifier)
                        {
                            case "fci":
                                element.Add(childElement = new XElement("fileControlInformation"));
                                break;

                            case "aip":
                                element.Add(childElement = new XElement("applicationInterchangeProfile"));
                                break;
                        }

                        break;

                    case 4:
                        {
                            // Record
                            element.Add(childElement = new XElement("record", new XAttribute("number", treeNode.Tag.ToString())));
                        }
                        break;

                    case 5:
                        {
                            // SDA Record
                            element.Add(childElement = new XElement("record", new XAttribute("number", treeNode.Tag.ToString()), new XAttribute("staticDataAuthentication", true)));
                        }
                        break;
                }
            }
            else
            {
                StringBuilder tag = new StringBuilder();
                StringBuilder value = new StringBuilder();

                if (tlv != null)
                {
                    foreach (byte b in tlv.Tag)
                    {
                        tag.AppendFormat("{0:X2}", b);
                    }

                    foreach (byte b in tlv.Value)
                    {
                        value.AppendFormat("{0:X2}", b);
                    }
                }

                XElement tagElement = tagsDocument.Descendants().Where(el => el.Attributes().Any(a => a.Name == "Tag" && a.Value == tag.ToString())).FirstOrDefault();
                string description = String.Empty;

                if (tagElement != null)
                {
                    description = tagElement.Attribute("Description").Value;
                }

                element.Add(childElement = new XElement("tlv", new XAttribute("tag", tag.ToString()), new XAttribute("length", tlv.Length), new XAttribute("value", value.ToString()), new XAttribute("description", description)));
            }

            // Build each node recursively.
            foreach (TreeNode tn in treeNode.Nodes)
            {
                BuildDocument(tn, ref childElement);
            }
        }

        private void UpdateStatusLabel(string status)
        {
            if (this.InvokeRequired) 
            {
                this.BeginInvoke(new UpdateStatusLabelDelegate(UpdateStatusLabel), status);
            }
            else 
            {
                lblStatus.Text = status; 
            }
        }

        private void UseHourglass(bool status)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new UpdateHourglassDelegate(UseHourglass), status);
            }
            else
            {
                this.UseWaitCursor = status;
                Application.DoEvents();
            }
        }
        private void ClearTreeView()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new ClearTreeViewDelegate(ClearTreeView));
            }
            else
            {
                treeViewData.Nodes.Clear();
            }
        }

        private void timerUpdateCheck_Tick(object sender, EventArgs e)
        {
            timerUpdateCheck.Enabled = false;

            SelfUpdater su = new SelfUpdater(this);
            su.CheckForUpdate("http://nicbedford.co.uk/versions.xml", "EMVCardBrowser");
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox ab = new AboutBox();
            ab.ShowDialog();
        }

    }
}
