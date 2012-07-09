using System;
using System.Collections.Generic;
using System.Text;
using NLog;
using IniParser;

namespace msbplaunch
{
	class Program
	{
		// Create instance for Nlog
		static Logger log = LogManager.GetLogger("msbplaunch");

		static void Main(string[] args)
		{
			log.Info("MSBPLaunch start");

			log.Info("MSBPLaunch stop");
		}
	}
}
