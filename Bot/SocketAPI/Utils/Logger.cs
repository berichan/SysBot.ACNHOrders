using System;
using SysBot.Base;

namespace SocketAPI
{
	public class Logger
	{
		/// <summary>
		/// Whether logs are enabled or not.
		/// </summary>
		private static bool logsEnabled = true;
		
		public static void LogInfo(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogInfo(message, nameof(SocketAPI));
		}

		public static void LogWarning(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogInfo(message, nameof(SocketAPI) + "Warning");
		}

		public static void LogError(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogError(message, nameof(SocketAPI));
		}

		public static void disableLogs()
		{
			logsEnabled = false;
		}
	}
}