using System;
using System.Collections;
using System.Data.SqlClient;
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

		static void Main(string[] args)
		{
			log.Info("MSBPLaunch start");

			// Read and check program settings
			ProgramSettings currentSettings = new ProgramSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
				System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".ini"));
			// Get list active databases, exclude system databases
			ArrayList aDatabases = getDatabasesList();

			// Run backup command for every database
			foreach (Object db in aDatabases)
			{
				log.Info(String.Format("Backup start for: {0}", db.ToString()));
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
}
