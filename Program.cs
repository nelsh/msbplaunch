using System;
using System.Collections.Generic;
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
			Console.WriteLine(String.Format("Path to msbp.exe: {0}", currentSettings.MsbpExe));
			Console.WriteLine(String.Format("Path to backup: {0}", currentSettings.BackupPath));

			log.Info("MSBPLaunch successfully");
		}

	}

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
