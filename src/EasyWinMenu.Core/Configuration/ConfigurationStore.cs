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
        if (TryLoad(out var configuration))
        {
            return configuration;
        }

        Save(configuration);
        return configuration;
    }

    public bool TryLoad(out LauncherConfiguration configuration)
    {
        if (File.Exists(configFilePath))
        {
            try
            {
                var json = File.ReadAllText(configFilePath);
                var loaded = JsonSerializer.Deserialize<LauncherConfiguration>(json, SerializerOptions);
                if (loaded is not null)
                {
                    configuration = loaded;
                    return true;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        configuration = LauncherConfiguration.CreateDefault();
        return false;
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
}
