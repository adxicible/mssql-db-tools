using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Database_Tools
{
    public partial class frmMain : Form
    {
        private IList<string> _databases;

        private string _directory = Directory.GetCurrentDirectory() + "\\backups";

        public frmMain()
        {
            InitializeComponent();

            _databases = new List<string>();

            txtHostname.Text = ".\\SQLEXPRESS";

            if (!Directory.Exists(_directory))
                Directory.CreateDirectory(_directory);

            txtOutputDirectory.Text = _directory;

            LoadDatabase();
        }

        public void LoadDatabase()
        {
            var proc = ExecuteProcess("sp_databases");
            
            _databases.Clear();
            
            int i = 0;
            string input = null;

            Regex reg = new Regex("[\\d\\w-_]+(\\s[\\d\\w-_]+)?");
            Regex notNeeded = new Regex("master|model|tempdb|msdb");

            while ((input = proc.StandardOutput.ReadLine()) != null)
            {
                if (i > 1 && !notNeeded.IsMatch(input))
                    _databases.Add(reg.Match(input).Value);

                i++;
            }

            cboDatabases.DataSource = _databases;
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            LoadDatabase();
        }

        private void btnBackupFilePath_Click(object sender, EventArgs e)
        {
            var dir = txtOutputDirectory.Text;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

                folderBrowserDialog.SelectedPath = dir;

            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                txtOutputDirectory.Text = folderBrowserDialog.SelectedPath;
        }

        private void btnBackup_Click(object sender, EventArgs e)
        {
            var filename = $"{cboDatabases.SelectedValue}_{DateTime.Now.ToString("yyyyMMddHHmm")}.bak";

            var proc = ExecuteProcess($"BACKUP DATABASE [{cboDatabases.SelectedValue}] TO DISK='{txtOutputDirectory.Text}\\{filename}'");

            var str = proc.StandardOutput.ReadToEnd();

            var strs = str.Split('\n');

            notifyIcon.BalloonTipText = strs[strs.Length - 2];
            notifyIcon.BalloonTipTitle = "Backup Complete";
            notifyIcon.ShowBalloonTip(3000);
        }

        private Process ExecuteProcess(string command)
        {
            Process proc = new Process();
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.FileName = "sqlcmd";
            proc.StartInfo.Arguments = $"-S {txtHostname.Text} -Q \"{command}\"";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();

            return proc;
        }

        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            openFileDialog.InitialDirectory = _directory;
            openFileDialog.Filter = "Backup Files|*.bak";
            openFileDialog.Title = "Select Backup File";

            if(openFileDialog.ShowDialog(this) == DialogResult.OK)
                txtRestoreFile.Text = openFileDialog.FileName;
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(txtRestoreFile.Text))
                MessageBox.Show(this, "Restore file not selected!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if(!txtRestoreFile.Text.Contains( cboDatabases.SelectedValue.ToString() ))
                MessageBox.Show(this, "Wrong restore file selected!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
            {
                var reg = new Regex(@"([\d\w-_]+(\s[\d\w-_]+)?)_[\d]+\.bak");

                var match = reg.Match(txtRestoreFile.Text);
                var database = match.Groups[1];

                var proc = ExecuteProcess($"RESTORE DATABASE [{database}] FROM DISK='{txtRestoreFile.Text}'");

                var str = proc.StandardOutput.ReadToEnd();
                
                var strs = str.Split('\n');

                notifyIcon.BalloonTipText = strs[strs.Length - 2];
                notifyIcon.BalloonTipTitle = "Restore Complete";
                notifyIcon.ShowBalloonTip(3000);
            }
        }
       
        private void btnSearch_Click(object sender, EventArgs e)
        {
            var storeprocs = new List<string>();

            var proc = ExecuteProcess($"USE [{cboDatabases.SelectedValue}]; SELECT ROUTINE_NAME FROM information_schema.routines where ROUTINE_TYPE = 'PROCEDURE'");

            string responses;
            int index = 0;

            while ((responses = proc.StandardOutput.ReadLine()) != null)
            {
                if (index > 2 && !String.IsNullOrEmpty(responses))
                    storeprocs.Add(responses.Trim());
                index++;
            }

            storeprocs.RemoveAt(storeprocs.Count - 1);
            proc.Dispose();
            
            proc = new Process();
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.FileName = "cmd";
            proc.StartInfo.Arguments = $"/C \"dir /S /B {txtSolutionPath.Text}\\*.vb\"";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();

            treeView.Nodes.Clear();

            var rootNode = new TreeNode(txtSolutionPath.Text);

            while((responses = proc.StandardOutput.ReadLine()) != null)
            {
                if (!String.IsNullOrEmpty(responses))
                {
                    var filename = responses.Trim();
                    var sp = FindStoreProceduresInFile(storeprocs, filename);

                    if (sp.Count > 0)
                    {
                        var parent = new TreeNode(filename.Substring(txtSolutionPath.Text.Trim().Length + 1));

                        foreach (var item in sp)
                            parent.Nodes.Add(item);

                        rootNode.Nodes.Add(parent);
                    }
                }
            }

            rootNode.ExpandAll();
            treeView.Nodes.Add(rootNode);

            notifyIcon.BalloonTipText = $"Search Complete.\n{rootNode.GetNodeCount(false)} files found!";
            notifyIcon.BalloonTipTitle = "Search";
            notifyIcon.ShowBalloonTip(3000);
        }

        private void btnOpenSolutionPathDialog_Click(object sender, EventArgs e)
        {
            if (solutionBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                txtSolutionPath.Text = solutionBrowserDialog.SelectedPath;
            }
        }

        private List<string> FindStoreProceduresInFile(IList<string> sp, string file)
        {
            var found = new List<string>();


            var input = File.ReadAllText(file);

            foreach (var item in sp)
                if (input.Contains(item))
                    found.Add(item);

            return found;
        }

        private void treeViewNode_MouseDoubleClicked(object sender, TreeNodeMouseClickEventArgs e)
        {
            var node = treeView.SelectedNode;

            if (node.LastNode == null)
                node = node.Parent;

            Process.Start(node.FullPath);
        }
    }
}
