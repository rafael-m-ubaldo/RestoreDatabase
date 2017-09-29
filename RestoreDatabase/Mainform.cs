using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.IO;
using System.Windows.Forms;
using System.Configuration;

/* Microsoft SQL Database Restore Utility by Rafael Ubaldo Sr.
 * 9/18/2017 Built using Visual Studio 2017 Community Edition. You can also use Microsoft Code too!
 * Simple no nonsense application that quickly restores a full database backup file to a Microsoft SQL Server Database.
 * The restore covers any combination of restoring a local or remote backup to a local or remote database server.
 * The database backup file is assumed to be a single full database backup file - next version will cover combining multiple files!
 * 
 * To use, the backup folder(s) need to have a file shares. SQL Server (local or remote) must have 
 * sufficient rights to read the shares. In most cases, using Windows Login did helps over SQL Server Login.
 * 
 * To start the restore, enter the connection information then drag and drop the backup file on to this application.
 * Connection information and other settings is saved and restored on next startup.
 * If the restore application (this application) and SQL Server both have sufficient rights, the restore SQL command script appears.
 * It is recommended only view and not alter the script! Next version won't show the script up front.
 * The Original database name appears with and option to change the target database (sweet!). Note the script updates as the target changes.
 * You have extra options to Check how many sessions are currently using the current database about to be restored,
 * a handy Kill all session button (my favorite) which kills all sessions (only for the database!), which is
 * needed to restore the database and, the Drop button that drops the database about to be full restored from the database server.
 * Please note as time permits, I will be fixing some of the async and other exception handling issues and mapped drive support.
 * This is only the first pass that I did in two days.
 */
namespace RestoreDatabase
{
    public partial class Mainform : Form
    {
        string DatabaseBackupFile = string.Empty;
        string LogicalNameData = string.Empty;
        string LogicalNameLog = string.Empty;
        string DataPath = string.Empty;
        string MdfPath = string.Empty;
        string LdfPath = string.Empty;
        bool loading = false;
        List<int> SessionIDs = new List<int>();

        public Mainform()
        {
            InitializeComponent();
            this.AllowDrop = true;
        }

        private void Mainform_Shown(object sender, EventArgs e)
        {
            getSetting("Server", textBoxServer);
            getSetting("Username", textBoxUsername);
            getSetting("Password", textBoxPassword);
            int auth = 0;
            Authentication = auth;
            if (int.TryParse(getSetting("Authentication"), out auth))
            {
                Authentication = auth;
            }
            DatabaseBackupFile = getSetting("DatabaseBackupFilename");
            OriginalDatabase = getSetting("OriginalDataBase");
            DatabaseName = getSetting("TargetDataBase");
        }

        private void Mainform_FormClosed(object sender, FormClosedEventArgs e)
        {
            setSetting("Server", Server);
            setSetting("Username", Username);
            setSetting("Password", Password);
            setSetting("Authentication", Authentication.ToString());
            setSetting("DatabaseBackupFilename", DatabaseBackupFile);
            setSetting("OriginalDataBase", OriginalDatabase);
            setSetting("TargetDataBase", DatabaseName);
        }

        private void Mainform_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                bool canDrop = true;
                string[] filenames = e.Data.GetData(DataFormats.FileDrop) as string[];

                if (filenames.Length != 1)
                {
                    canDrop = false;
                }

                if (canDrop)
                {
                    e.Effect = DragDropEffects.Copy;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
        }

        private void Mainform_DragDrop(object sender, DragEventArgs e)
        {
            String[] filenames = e.Data.GetData(DataFormats.FileDrop) as String[];
            foreach (string file in filenames)
            {
                if (File.Exists(file))
                {
                    GenerateRestoreScript(file);
                }
            }
        }

        string Server
        {
            get { return textBoxServer.Text; }
        }

        int Authentication
        {
            get { return comboBoxAuthentication.SelectedIndex; }
            set { comboBoxAuthentication.SelectedIndex = value; }
        }

        string Username
        {
            get { return textBoxUsername.Text; }
        }

        string Password
        {
            get { return textBoxPassword.Text; }
        }

        //
        string ConnectionString
        {
            get
            {
                SqlConnectionStringBuilder connstr = new SqlConnectionStringBuilder();
                // Build connection string
                connstr["Data Source"] = Server;
                connstr["Persist Security Info"] = "True";
                connstr["Initial Catalog"] = "master";  // Default to master database
                if (Authentication == 0)
                {
                    connstr["User ID"] = Username;
                    connstr["Password"] = Password;
                }
                else
                {
                    connstr["Integrated Security"] = true;
                }
                return connstr.ConnectionString;
            }
        }

        string RestoreDataBaseScript { get; set; }

        string OriginalDatabase { get { return textBoxOriginal.Text; } set { textBoxOriginal.Text = value; } }

        string DatabaseName
        {
            get { return textBoxTarget.Text; }
            set
            {
                textBoxTarget.Text = value;
            }
        }

        string Sessions
        {
            get { return textBoxSessions.Text; }
            set { textBoxSessions.Text = value; }
        }

        private void textBoxTarget_TextChanged(object sender, EventArgs e)
        {
            if (!loading)
            {
                loading = true;
                try
                {
                    GenerateRestoreScript(DatabaseBackupFile);
                }
                finally
                {
                    loading = false;
                }
            }
        }

        private void comboBoxAuthentication_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxAuthentication.SelectedIndex == 0)
            {
                // SQL Authentication
                if (Username == DomainUserName)
                {
                    textBoxUsername.Text = "";
                }
                textBoxPassword.PasswordChar = '*';
                textBoxUsername.ReadOnly = false;
                textBoxPassword.ReadOnly = false;
                checkBoxShow.Enabled = true;
            }
            else
            {
                // Windows Authentication
                textBoxUsername.Text = DomainUserName;
                textBoxPassword.Text = "";
                checkBoxShow.Checked = false;
                checkBoxShow.Enabled = false;
                textBoxUsername.ReadOnly = true;
                textBoxPassword.ReadOnly = true;
            }
        }

        private void checkBoxShow_Click(object sender, EventArgs e)
        {
            if (sender is CheckBox)
            {
                CheckBox cb = (sender as CheckBox);
                if (cb.CheckState == CheckState.Checked)
                {
                    textBoxPassword.PasswordChar = '\0';
                }
                else
                {
                    textBoxPassword.PasswordChar = '*';
                }
            }
        }

        private void buttonCheck_Click(object sender, EventArgs e)
        {
            GetSessionIds(DatabaseName);
        }

        private void buttonKillSession_Click(object sender, EventArgs e)
        {
            Cursor curSave = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                GetSessionIds(DatabaseName);
                foreach (int spid in SessionIDs)
                {
                    KillSession(spid);
                }
                GetSessionIds(DatabaseName);
            }
            finally
            {
                this.Cursor = curSave;
            }
        }

        private async void buttonRestore_Click(object sender, EventArgs e)
        {
            Cursor curSave = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            buttonRestore.Enabled = false;
            try
            {
                int rsp = await ExecuteSQL(RestoreDataBaseScript);
            }
            finally
            {
                buttonRestore.Enabled = true;
                this.Cursor = curSave;
            }
        }

        private async void buttonDropDatabase_Click(object sender, EventArgs e)
        {
            // Drops the database (if it exists)
            // Does not complain if it's not there!
            Cursor curSave = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            buttonDropDatabase.Enabled = false;
            try
            {
                StringBuilder SQL = new StringBuilder();
                SQL.Append("IF EXISTS (SELECT * FROM sys.databases WHERE name = ").Append(Quoted(DatabaseName)).AppendLine(")");
                SQL.AppendLine("BEGIN");
                SQL.Append(" DROP DATABASE ").Append(DatabaseName).AppendLine(";");
                SQL.AppendLine("END;");
                string cmd = SQL.ToString();
                int rsp = await ExecuteSQL(cmd);
            }
            finally
            {
                buttonDropDatabase.Enabled = true;
                this.Cursor = curSave;
            }
        }

        private string TranslatePath(string PathIn)
        {
            // Local to remote translates of the filespec of the backup file used in the Restore Script.
            string RemoteServer = string.Empty; // Remote server in UNC file path.
            string machineName = string.Empty;  // Server entered in the Server text box
            string thisMachine = Environment.MachineName; // This machine; where this app is running.
            string Result = PathIn;

            machineName = Server;
            if (machineName.Equals(".") || machineName.Equals("(local)", StringComparison.CurrentCultureIgnoreCase) || machineName.Equals("localhost", StringComparison.CurrentCultureIgnoreCase))
            {
                // The Server entered is same one this app is running on.
                machineName = Environment.MachineName;  // Get the Computer or machine name.
            }

            if (PathIn.StartsWith(@"\\"))   // Network UNC \\RemoteServer\ShareName\filepath
            {
                int idx = 2;
                int os = PathIn.IndexOf(@"\", idx);
                RemoteServer = PathIn.Substring(idx, os - idx);
                idx = os + 1;
                os = PathIn.IndexOf(@"\", idx);
                string ShareName = PathIn.Substring(idx, os - idx);
                idx = os + 1;
                string filePath = PathIn.Substring(idx);

                if (machineName.Equals(RemoteServer, StringComparison.CurrentCultureIgnoreCase))
                {
                    // The backup file was dropped from a remote share on the remote database server.
                    // Translate the UNC to a path local on the remove server.
                    string RemotePath = ShareNameToPath(RemoteServer, ShareName); // Get path of remote share
                    if (RemotePath != string.Empty) // Share was found.
                    {
                        Result = Path.Combine(RemotePath, filePath); // Translate. Otherwise, take no action.
                    }
                }
            }
            else if (!machineName.Equals(thisMachine, StringComparison.CurrentCultureIgnoreCase))
            {
                // Backup was dropped from folder on this computer to restore on remote SQL server.
                // The local path on this computer is translated to a UNC file path for the remote SQL server to see.
                // This does work provided the remote SQL server has read rights to the share.
                List<List<string>> Shares = GetNetworkShareDetailsUsingWMI(thisMachine);
                Shares.Sort(Sorter); // Just sorts with longest path length first.
                foreach(List<string> Share in Shares)
                {
                    string share = Share[0];
                    string path = Share[1];
                    if (PathIn.StartsWith(path, StringComparison.CurrentCultureIgnoreCase))
                    {
                        if ((PathIn[path.Length] == '\\') || (PathIn[path.Length - 1] == '\\'))
                        {
                            string filepath;
                            if (PathIn[path.Length] == '\\')
                            {
                                filepath = PathIn.Substring(path.Length + 1);
                            }
                            else
                            {
                                filepath = PathIn.Substring(path.Length);
                            }
                            string NetShare = @"\\" + thisMachine + @"\" + share;
                            string newPath = Path.Combine(NetShare, filepath);
                            Result = newPath;
                            break;
                        }
                    }
                }
            }
            return Result;
        }

        private void GenerateRestoreScript(string DbBackupFile)
        {
            // Generates the SQL command for Full Database Restore.
            // Note: This version only cover the backup file being a single full datase backup.
            // Called when a backup is dropped on to the app and when the target datbase name changes
            if (File.Exists(DbBackupFile))
            {
                string LocalDbBackupFile = TranslatePath(DbBackupFile); // Local/Remote path translations.
                if (IsConnectionStringValid())
                {
                    DatabaseBackupFile = DbBackupFile;
                    string original = OriginalDatabase;
                    GetLogicalNames(LocalDbBackupFile, ref LogicalNameData, ref LogicalNameLog, ref original, ref DataPath);
                    OriginalDatabase = original;
                    if (!loading)
                    {
                        loading = true;
                        try
                        {
                            DatabaseName = OriginalDatabase;
                        }
                        finally
                        {
                            loading = false;
                        }
                    }
                    MdfPath = Path.Combine(DataPath, DatabaseName + ".mdf");
                    LdfPath = Path.Combine(DataPath, DatabaseName + "_log.ldf");
                    StringBuilder SQL = new StringBuilder();
                    SQL.Append("RESTORE DATABASE ").AppendLine(DatabaseName);
                    SQL.Append("FROM DISK = ").AppendLine(Quoted(LocalDbBackupFile));
                    SQL.AppendLine("WITH RECOVERY,");
                    SQL.Append("MOVE ").Append(Quoted(LogicalNameData)).Append(" TO ").Append(Quoted(MdfPath)).AppendLine(",");
                    SQL.Append("MOVE ").Append(Quoted(LogicalNameLog)).Append(" TO ").Append(Quoted(LdfPath)).AppendLine(";");
                    RestoreDataBaseScript = SQL.ToString();
                    textBox.Text = RestoreDataBaseScript;
                    GetSessionIds(DatabaseName);
                }
            }
        }

        private void GetLogicalNames(string DbBackupFile, ref string LogicalNameData, ref string LogicalNameLog, ref string OriginalDatabase, ref string DataPath)
        {
            // Reads database backup file to get the logical data and log files names and the original database name.
            // This also grabs the path of the master .mdf file for the target database .mdf and .ldf files.
            // I was looking other solutions to determine the default paths for both the data and log file but, this is
            // the best I could do in two days.
            LogicalNameData = string.Empty;
            LogicalNameLog = string.Empty;
            string queryFileList = "RESTORE FILELISTONLY FROM DISK = " + Quoted(DbBackupFile);
            string queryHeader = "RESTORE HEADERONLY FROM DISK = " + Quoted(DbBackupFile);
            string queryMasterPath = "SELECT physical_name MasterMdfPath FROM sys.master_files WHERE NAME = 'master';";
            DataSet data = DataSetFor(queryFileList);
            DataTable table = data.Tables[0];
            for (int row = 0; row < table.Rows.Count; row++)
            {
                string sName = table.Rows[row]["LogicalName"].ToString();
                string sType = table.Rows[row]["Type"].ToString();
                if (sType.Equals("D", StringComparison.CurrentCultureIgnoreCase))
                {
                    LogicalNameData = sName;
                }
                else if (sType.Equals("L", StringComparison.CurrentCultureIgnoreCase))
                {
                    LogicalNameLog = sName;
                }
            }
            data = DataSetFor(queryHeader);
            table = data.Tables[0];
            if (table.Rows.Count > 0)
            {
                OriginalDatabase = table.Rows[0]["DatabaseName"].ToString();
            }
            data = DataSetFor(queryMasterPath);
            // Could not find something like sp_helpServerInfo with the current default data and log paths!
            // So I thought, for now, getting the path of the master .mdf file was the best alternate.
            table = data.Tables[0];
            if (table.Rows.Count > 0)
            {
                string filespec = table.Rows[0]["MasterMdfPath"].ToString();
                DataPath = Path.GetDirectoryName(filespec);
            }
        }

        private void GetSessionIds(string DatabaseName)
        {
            // Build list of SPIDs for session current using the database about to (DROPPED) and restored.
            string query = "EXEC sp_who";
            DataSet data = DataSetFor(query);
            DataTable table = data.Tables[0];
            SessionIDs.Clear();
            for(int idx = 0; idx < table.Rows.Count; idx++)
            {
                string dbname = table.Rows[idx]["dbname"].ToString();
                if (dbname.Equals(DatabaseName, StringComparison.CurrentCultureIgnoreCase))
                {
                    int spid = Convert.ToInt32(table.Rows[idx]["spid"]);
                    SessionIDs.Add(spid);
                }
            }
            Sessions = SessionIDs.Count.ToString();
        }

        private async void KillSession(int spid)
        {
            string cmd = string.Format("KILL {0}", spid);
            int rsp = await ExecuteSQL(cmd);
        }

        private async Task<int> ExecuteSQL(string SQL)
        {
            // Executes SQL now asynchronously - (makes dragging app during long restors easier!)
            int result = 0;
            if (IsConnectionStringValid())
            {
                SqlConnectionStringBuilder connStr = new SqlConnectionStringBuilder(ConnectionString);
                connStr.ConnectTimeout = 15 * 60;
                connStr.AsynchronousProcessing = true;
                using (SqlConnection conn = new SqlConnection(connStr.ToString()))
                {
                    try
                    {
                        conn.Open();
                        SqlCommand cmd = new SqlCommand(SQL, conn);
                        cmd.CommandTimeout = conn.ConnectionTimeout;
                        result = await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                    finally
                    {
                        conn.Close();
                    }
                }
            }
            return result;
        }

        private DataSet DataSetFor(string query)
        {
            // Perform SQL query and returns a data set of the results.
            DataSet data = new DataSet();
            if (IsConnectionStringValid())
            {
                using (SqlConnection conn = new SqlConnection(ConnectionString))
                {
                    SqlDataAdapter adapter = new SqlDataAdapter();
                    adapter.SelectCommand = new SqlCommand(query, conn);
                    adapter.Fill(data);
                }
            }
            return data;
        }

        bool IsConnectionStringValid(bool IgnoreDatabase = false)
        {
            bool resp = false;
            Cursor curSave = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                String connectionString = ConnectionString;
                Task<bool> CheckConnectionTask = Task<bool>.Run(() =>
                {
                    bool result = false;
                    SqlConnectionStringBuilder connStr = new SqlConnectionStringBuilder(connectionString);
                    connStr.AsynchronousProcessing = true;
                    connStr.ConnectTimeout = 1;
                    if (IgnoreDatabase)
                    {
                        connStr.Remove("Initial Catalog");
                    }
                    using (SqlConnection conn = new SqlConnection(connStr.ToString()))
                    {
                        try
                        {
                            conn.Open();
                            result = true;
                        }
                        catch
                        {
                            result = false;
                        }
                        finally
                        {
                            conn.Close();
                        }
                    }
                    return result;
                });
                CheckConnectionTask.Wait(1500);
                if (CheckConnectionTask.IsCompleted)
                {
                    resp = CheckConnectionTask.Result;
                }
                else
                {
                    resp = false;
                }
            }
            finally
            {
                this.Cursor = curSave;
            }
            return resp;
        }

        public string Quoted(string text)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("'").Append(text.Replace("'", "''")).Append("'");
            return sb.ToString();
        }

        private string DomainUserName
        {
            get { return Environment.UserDomainName + @"\" + Environment.UserName; }
        }

        void getSetting(string key, TextBox textbox)
        {
            string s = getSetting(key);
            if (s != "")
            {
                textbox.Text = s;
            }
        }

        string getSetting(string key)
        {
            string result = "";

            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            AppSettingsSection app = config.AppSettings;
            if (app.Settings[key] != null)
            {
                result = app.Settings[key].Value;
            }
            return result;
        }

        void setSetting(string key, string value)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            AppSettingsSection app = config.AppSettings;
            if (app.Settings[key] != null)
            {
                if (app.Settings[key].Value != value)
                {
                    app.Settings[key].Value = value;
                }
            }
            else
            {
                app.Settings.Add(key, value);
            }
            config.Save(ConfigurationSaveMode.Full);
        }

        // Two WMI methods for handling File Share

        public string ShareNameToPath(string Server, string ShareName, string Username = "", string Password = "")
        {
            string Path = string.Empty;
            // do not use ConnectionOptions to get shares from local machine
            ConnectionOptions connectionOptions = new ConnectionOptions();
            if (Username != string.Empty)
            {
                connectionOptions.Username = Username;
                connectionOptions.Password = Password;
                connectionOptions.Impersonation = ImpersonationLevel.Impersonate;
            }
            ManagementScope scope = new ManagementScope(@"\\" + Server + @"\root\CIMV2", connectionOptions);
            scope.Connect();

            ManagementObjectSearcher worker = new ManagementObjectSearcher(scope,
                new ObjectQuery("select Name,Path from win32_share where Name='" + ShareName + "'"));
            foreach (ManagementObject share in worker.Get())
            {
                Path = share["Path"].ToString();
            }
            return Path;
        }

        public List<List<string>> GetNetworkShareDetailsUsingWMI(string serverName, string Username = "", string Password = "")
        {
            List<List<string>> shares = new List<List<string>>();

            // do not use ConnectionOptions to get shares from local machine
            ConnectionOptions connectionOptions = new ConnectionOptions();
            if (Username != string.Empty)
            {
                connectionOptions.Username = Username;
                connectionOptions.Password = Password;
                connectionOptions.Impersonation = ImpersonationLevel.Impersonate;
            }
            //connectionOptions.Username = @"Domain\Administrator";
            //connectionOptions.Password = "password";
            //connectionOptions.Impersonation = ImpersonationLevel.Impersonate;

            ManagementScope scope = new ManagementScope(@"\\" + serverName + @"\root\CIMV2", connectionOptions);
            scope.Connect();

            ManagementObjectSearcher worker = new ManagementObjectSearcher(scope,
                new ObjectQuery("select Name, Path from win32_share"));
            foreach (ManagementObject share in worker.Get())
            {
                List<string> row = new List<string>();
                string sShare = share["Name"].ToString();
                row.Add(sShare);
                row.Add(share["Path"].ToString());
                shares.Add(row);
            }
            return shares;
        }

        public int Sorter(List<string> A, List<string> B)
        {
            return B[1].Length - A[1].Length;
        }


    }
}
