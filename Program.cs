using System;
using System.Collections;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

			log.Info("MSBPLaunch successfully");
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
			string backupFile = String.Format("{0}_{1}{2}.zip", db, currentBackups.Id, currentBackups.CurrentDateString);
			string arg = String.Format("backup db(database={0};backuptype={1}) zip64(level=5) file:///{2}",
				db, currentBackups.Method, Path.Combine(backupPath,backupFile));
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
