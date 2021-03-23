using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;

namespace SysBot.ACNHOrders
{
    internal static class Program
    {
        private const string ConfigPath = "config.json";
        private const string TwitchPath = "twitch.json";

        private static async Task Main(string[] args)
        {
            Console.WriteLine("Starting up...");
            if (args.Length > 1)
                Console.WriteLine("This program does not support command line arguments.");

            if (!File.Exists(ConfigPath))
            {
                CreateConfigQuit();
                return;
            }

            if (!File.Exists(TwitchPath))
                SaveConfig(new TwitchConfig(), TwitchPath);

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<CrossBotConfig>(json);
            if (config == null)
            {
                Console.WriteLine("Failed to deserialize configuration file.");
                WaitKeyExit();
                return;
            }

            json = File.ReadAllText(TwitchPath);
            var twitchConfig = JsonSerializer.Deserialize<TwitchConfig>(json);
            if (twitchConfig == null)
            {
                Console.WriteLine("Failed to deserialize twitch configuration file.");
                WaitKeyExit();
                return;
            }

            SaveConfig(config, ConfigPath);
            SaveConfig(twitchConfig, TwitchPath);
            await BotRunner.RunFrom(config, CancellationToken.None, twitchConfig).ConfigureAwait(false);
            WaitKeyExit();
        }

        private static void SaveConfig<T>(T config, string path)
        {
            var options = new JsonSerializerOptions {WriteIndented = true};
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(path, json);
        }

        private static void CreateConfigQuit()
        {
            SaveConfig(new CrossBotConfig {IP = "192.168.0.1", Port = 6000}, ConfigPath);
            SaveConfig(new TwitchConfig(), TwitchPath);
            Console.WriteLine("Created blank config file. Please configure it and restart the program.");
            WaitKeyExit();
        }

        private static void WaitKeyExit()
        {
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
