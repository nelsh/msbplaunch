using System;
using System.Collections;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Mime;
using System.Net.Mail;
using System.Text;
using NLog;
using IniParser;

namespace msbplaunch
{
	class Program
	{
		// Create instance for Nlog
		static public Logger log = LogManager.GetLogger("msbplaunch");
		// struct for store program settings
		static public ProgramSettings currentSettings;
		// struct for store backup settings
		static public BackupSettings currentBackups;
		// create variables for report
		static double TotalBackupSize = 0;
		static int TotalBackupSuccess = 0;
		static SortedList ReportDatabases = new SortedList();


		static void Main(string[] args)
		{
			log.Info("MSBPLaunch start");

			// Read and check program settings
			currentSettings = new ProgramSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
				System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".ini"));
			// Get list active databases, exclude system databases
			ArrayList aDatabases = getDatabasesList();
			// Set current backup settings
			currentBackups = new BackupSettings(DateTime.Now);


			// Run backup command for every database
			foreach (String db in aDatabases)
			{
				runDatabaseBackup(db);
			}
			// Send summary report
			if (currentSettings.SendReport) sendSummaryReport(aDatabases.Count);

			log.Info(String.Format("MSBPLaunch successfully: databases {0}/{1}, total size {2} Mb", 
				TotalBackupSuccess, aDatabases.Count, Math.Round(TotalBackupSize / 1024 / 1024)));
		}
		
		/// <summary>
		/// Get list active databases, exclude system databases
		/// </summary>
		/// <returns>ArrayList of Databases</returns>
		static ArrayList getDatabasesList()
		{
			SqlConnection connection = new SqlConnection("Persist Security Info=False;Trusted_Connection=True;database=master;server=(local)");
			SqlCommand command = new SqlCommand(
				"SELECT name FROM sysdatabases WHERE (name NOT IN('master', 'model', 'msdb', 'tempdb', 'distribution')) AND (CONVERT(bit, status & 512)=0) ORDER BY name",
				connection);
			SqlDataReader reader = null;
			try
			{
				connection.Open();
				reader = command.ExecuteReader();
			}
			catch (Exception ex)
			{
				reader.Close();
				connection.Close();
				Program.log.Fatal(String.Format("MSBPLaunch stop: Could not get databases list. Stack: {0}", ex));
				Environment.Exit(2);
			}
			ArrayList aDatabases = new ArrayList();
			if (reader.HasRows)
				while (reader.Read())
					aDatabases.Add(reader.GetString(0));
			reader.Close();
			connection.Close();
			// If databaselist is empty - exit
			if (aDatabases.Count == 0)
			{
				Program.log.Warn("MSBPLaunch stop: Databases for backup not found");
				Environment.Exit(2);
			}
			else
				log.Info("Found " + aDatabases.Count + " databases for backup");
			return aDatabases;
		}

		/// <summary>
		/// Run database backup
		/// - check path for store database backup
		/// - construct msbp.exe call
		/// - run msbp.exe process and check output 
		/// </summary>
		/// <param name="db">database name</param>
		static void runDatabaseBackup(String db)
		{
			log.Info(String.Format("Backup start for: {0}", db));

			// Check path for backup
			string backupPath = Path.Combine(currentSettings.BackupPath, db.ToString());
			if (!Directory.Exists(backupPath))
			{
				Program.log.Info(String.Format("... not exist directory \"{0}\". Create it.", backupPath));
				try
				{
					Directory.CreateDirectory(backupPath);
				}
				catch
				{
					Program.log.Error(String.Format("... backup stop with error: Cannot create directory {0}.", backupPath));
					return;
				}
			}

			// Construct argument string
			string backupFile = Path.Combine(backupPath,String.Format("{0}_{1}{2}.zip", db, currentBackups.Id, currentBackups.CurrentDateString));
			string arg = String.Format("backup db(database={0};backuptype={1}) zip64(level=5) file:///{2}",
				db, currentBackups.Method, backupFile);
			log.Debug("... run backup: " + currentSettings.MsbpExe + " " + arg);
	
			// Create and run external process
			Process p = new Process();
			p.StartInfo.FileName = currentSettings.MsbpExe;
			p.StartInfo.Arguments = arg;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.LoadUserProfile = false;
			p.StartInfo.CreateNoWindow = true;
			p.StartInfo.RedirectStandardOutput = true;
			p.Start();
			string sOutput = p.StandardOutput.ReadToEnd(); // read output to temporary string
			p.WaitForExit();

			// Check output and calculate totals for report
			string[] aOutput = sOutput.Split('\n');
			string successfully = aOutput[aOutput.Length - 2];
			string BackupTime = "ERROR";
			double BackupSize = 0;
			if (successfully.StartsWith("Completed Successfully."))
			{
				try
				{
					BackupTime = successfully.Split('.')[1].Trim();
					FileInfo fi = new FileInfo(backupFile);
					BackupSize = Math.Round((double)fi.Length / 1024 / 1024);
					TotalBackupSize += fi.Length;
					TotalBackupSuccess++;
					log.Info("... Time: " + BackupTime + ", Size: " + BackupSize + " Mb.");
				}
				catch
				{
					log.Error("... Backup File Not Found: " + backupFile);
				}
			}
			else
			{
				log.Error(String.Format("... ERROR: {0}", sOutput));
			}
			// Store results
			SortedList BackupResult = new SortedList();
			BackupResult.Add("time", BackupTime);
			BackupResult.Add("size", BackupSize);
			ReportDatabases.Add(db, BackupResult);
		}

		static void sendSummaryReport(int dbCount)
		{
			// Create report
			StringBuilder reportString = new StringBuilder();
			reportString.AppendLine("<!DOCTYPE html>");
			reportString.AppendLine("");
			reportString.AppendLine("<html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\" />");
			reportString.AppendFormat("<title>{0} - SQL backup Report</title></head><body>", System.Net.Dns.GetHostName());
			reportString.AppendFormat("<h1>{0} - SQL backup Report</h1>", System.Net.Dns.GetHostName());
			reportString.AppendFormat("<p><b>{0}</b></p>", DateTime.Now);
			reportString.AppendLine("<table border=1><tr><th align=left>Database</th><th align=left>Time</th><th align=left>Size</th></tr>");
			for (int i = 0; i < ReportDatabases.Count; i++)
			{
				SortedList BackupResult = new SortedList();
				BackupResult = (SortedList)ReportDatabases.GetByIndex(i);
				reportString.AppendFormat("<tr><td>{0}</td><td>{1}</td><td align=right>{2}</td></tr>",
					ReportDatabases.GetKey(i).ToString(),
					BackupResult.GetByIndex(BackupResult.IndexOfKey("time")),
					BackupResult.GetByIndex(BackupResult.IndexOfKey("size"))
				);
			}
			reportString.AppendFormat("<tr><td colspan=3><b>Total: {0} files, {1} Mb</b></td></tr></table><br>", dbCount, Math.Round((double)TotalBackupSize / 1024 / 1024));
			reportString.AppendLine("</body></html>");

			//  Send report ////
			try
			{
				MailMessage mail = new MailMessage();
				SmtpClient SmtpServer = new SmtpClient(Program.currentSettings.SMTP_Server);
				mail.From = new MailAddress(Program.currentSettings.Mail_From);
				mail.To.Add(Program.currentSettings.Mail_To);
				mail.Subject = string.Format("{0} - sqlbackup success {1}/{2} databases, total size: {3} Mb, errors: {4}",
					System.Net.Dns.GetHostName(), TotalBackupSuccess, dbCount, Math.Round((double)TotalBackupSize / 1024 / 1024), dbCount - Program.TotalBackupSuccess);
				mail.IsBodyHtml = true;
				mail.Body = reportString.ToString();
				//				mail.Attachments.Add(new Attachment(LogFile, MediaTypeNames.Text.Plain));
				SmtpServer.Send(mail);
				log.Info("Successfully send summary report");
			}
			catch (Exception ex)
			{
				log.Warn(ex.ToString());
			}
		}
	}

	/// <summary>
	/// Struct for store settings
	/// General settings:
	/// - string ProgramSettings.MsbpExe - path to msbp.exe,
	/// - string ProgramSettings.BackupPath - path to store backups, create it if not exists.
	/// Storage time in days:
	/// - TODO.
	/// Mail settings:
	/// - TODO.
	/// </summary>
	public struct ProgramSettings
	{
		public string MsbpExe;		// Path to SQL Compressed binary (msbp.exe)
		public string BackupPath;	// Path to store backup copies
		// Mail settings
		public string SMTP_Server;
		public string Mail_From;
		public string Mail_To;
		public bool SendReport;

		public ProgramSettings(string iniFileName)
		{
			// is exists INI-file?
			Program.log.Debug(String.Format("Read INI File \"{0}\"", iniFileName));
			if (!File.Exists(iniFileName))
			{
				Program.log.Fatal("MSBPLaunch stop: Cannot find INI File");
				Environment.Exit(2);
			}
			// read INI-file
			IniParser.FileIniDataParser iniParser = new FileIniDataParser();
			IniData iniData = null;
			try
			{
				iniData = iniParser.LoadFile(iniFileName);
			}
			catch (Exception ex)
			{
				Program.log.Fatal("MSBPLaunch stop: Cannot read INI File. " + ex);
				Environment.Exit(2);
			}
			// check msbp.exe
			MsbpExe = iniData["General"]["MSBP_Exe"];
			if (String.IsNullOrWhiteSpace(MsbpExe))
			{
				Program.log.Fatal("MSBPLaunch stop: Not found path settings for msbp.exe");
				Environment.Exit(2);
			}
			if (!File.Exists(MsbpExe))
			{
				Program.log.Fatal(String.Format("MSBPLaunch stop: Not found {0}", MsbpExe));
				Environment.Exit(2);
			}
			// check backup path and create it if not exists
			BackupPath = iniData["General"]["Backup_Path"];
			if (String.IsNullOrWhiteSpace(BackupPath))
			{
				Program.log.Fatal("MSBPLaunch stop: Not found path settings for backups");
				Environment.Exit(2);
			}
			if (!Directory.Exists(BackupPath))
			{
				Program.log.Info(String.Format("Not exist directory \"{0}\". Create it.", BackupPath));
				try
				{
					Directory.CreateDirectory(BackupPath);
				}
				catch (Exception ex)
				{
					Program.log.Fatal(String.Format("MSBPLaunch stop: Cannot create directory {0}. Stack: {1}", BackupPath, ex));
					Environment.Exit(2);
				}
			}
			// check mail settings
			SMTP_Server = iniData["Mail"]["SMTP_Server"];
			Mail_From = iniData["Mail"]["Mail_From"];
			Mail_To = iniData["Mail"]["Mail_To"];
			if (String.IsNullOrWhiteSpace(SMTP_Server) || String.IsNullOrWhiteSpace(Mail_From) || String.IsNullOrWhiteSpace(Mail_To))
			{
				SendReport = false;
				Program.log.Warn("Check mail settings. Send summary report DISABLED");
			}
			else
			{
				SendReport = true;
				Program.log.Info("Send summary report ENABLED");

			}
		}
	}

	/// <summary>
	/// Settings for current backup
	/// - DateTime CurrentDateTime - store current datetime
	/// - string CurrentDateString - date in format yyyyMMdd
	/// - string Id - identificator of backup type (D - daily, W - weekly, Q - quarter)
	/// - string Method - backup method: differential or full
	/// </summary>
	public struct BackupSettings
	{
		public DateTime CurrentDateTime;
		public string CurrentDateString;
		public string Id;
		public string Method;

		public BackupSettings(DateTime dt)
		{
			CurrentDateTime = dt;
			CurrentDateString = dt.ToString("yyyyMMdd");
			switch ((int)dt.DayOfWeek)
			{
				case 6:
					int CurrentWeekYear = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear
					(
						CurrentDateTime,
						CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule,
						CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek
					);
					Method = "full";
					if ((CurrentWeekYear % 13) == 1)
					{
						Id = "Q";
					}
					else
					{
						Id = "W";
					}
					break;
				default:
					Id = "D";
					Method = "differential";
					break;
			}
		}
	}

}
