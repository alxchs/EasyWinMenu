using System.Text.Json;
using System.Text.Json.Serialization;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.Core.Configuration;

public sealed class ConfigurationStore(string configFilePath)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public LauncherConfiguration Load()
    {
        if (!File.Exists(configFilePath))
        {
            return SaveDefault();
        }

        try
        {
            var json = File.ReadAllText(configFilePath);
            var configuration = JsonSerializer.Deserialize<LauncherConfiguration>(json, SerializerOptions);
            if (configuration is not null)
            {
                return configuration;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return SaveDefault();
    }

    public void Save(LauncherConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(configuration, SerializerOptions);
        File.WriteAllText(configFilePath, json);
    }

    private LauncherConfiguration SaveDefault()
    {
        var configuration = LauncherConfiguration.CreateDefault();
        Save(configuration);
        return configuration;
    }
}
