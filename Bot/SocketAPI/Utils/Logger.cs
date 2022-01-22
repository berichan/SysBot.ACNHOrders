using System;

namespace SysBot.ACNHOrders
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

			ConsoleColor def = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.Write("[SocketAPIServer] ");
			Console.ForegroundColor = def;
			Console.Write(message + "\n");
		}

		public static void LogWarning(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			ConsoleColor def = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine($"[SocketAPIServer] {message}");
			Console.ForegroundColor = def;
		}

		public static void LogError(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			ConsoleColor def = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine($"[SocketAPIServer] {message}");
			Console.ForegroundColor = def;
		}

		public static void disableLogs()
		{
			logsEnabled = false;
		}
	}
}