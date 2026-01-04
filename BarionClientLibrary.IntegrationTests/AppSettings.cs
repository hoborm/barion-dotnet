using System;
using Microsoft.Extensions.Configuration;

namespace BarionClientLibrary.IntegrationTests;

public static class AppSettings
{
    private static IConfiguration _config => GetConfiguration();

    public static string BarionBaseAddress => GetSetting("BarionBaseAddress");
    public static string BarionPOSKey => GetSetting("BarionPOSKey");
    public static string BarionPayee => GetSetting("BarionPayee");
    public static string BarionPayer => GetSetting("BarionPayer");
    public static string BarionPayerPassword => GetSetting("BarionPayerPassword");

    private static IConfiguration GetConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json", optional: false)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string GetSetting(string key)
    {
        // Environment variables take precedence
        var envValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(envValue))
        {
            return envValue;
        }

        // Fall back to JSON config
        var configValue = _config[key];
        return string.IsNullOrEmpty(configValue)
            ? throw new InvalidOperationException($"Configuration key '{key}' not found in appsettings.test.json or environment variables")
            : configValue;
    }
}
