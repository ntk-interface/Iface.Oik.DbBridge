using System;
using System.IO;
using System.Linq;

namespace Iface.Oik.DbBridge;

public static class ConfigLoader
{
    public static string SqlTextPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "sql", "config.sql");

    public static Config Load()
    {
        if (!File.Exists(SqlTextPath))
        {
            throw new Exception("Не найден файл конфигурации");
        }

        var config = new Config { SqlText = File.ReadAllText(SqlTextPath) };

        var commandLineArguments = Environment.GetCommandLineArgs();
        if (commandLineArguments.Length < 2)
        {
            return config;
        }

        foreach (var arg in commandLineArguments.Skip(2))
        {
            var parts = arg.Split('=');
            if (parts.Length < 2)
            {
                continue;
            }

            switch (parts[0].TrimStart('/', '-').ToLowerInvariant())
            {
                case "period":
                    if (int.TryParse(parts[1], out var period))
                    {
                        config.WorkPeriod = period;
                    }

                    break;

                case "offset":
                    if (int.TryParse(parts[1], out var offset))
                    {
                        config.WorkOffset = offset;
                    }

                    break;

                case "db":
                    var dbParts = parts[1].Split(',');
                    config.DbType = dbParts.ElementAtOrDefault(0) ?? string.Empty;
                    config.DbHost = dbParts.ElementAtOrDefault(1) ?? string.Empty;
                    config.DbPort = int.Parse(dbParts.ElementAtOrDefault(2) ?? string.Empty);
                    config.DbDatabase = dbParts.ElementAtOrDefault(3) ?? string.Empty;
                    config.DbUser = dbParts.ElementAtOrDefault(4) ?? string.Empty;
                    config.DbPassword = dbParts.ElementAtOrDefault(5) ?? string.Empty;
                    break;
            }
        }

        return config;
    }
}
