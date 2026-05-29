using System.Text.Json;
using WebSocketNFCeService.Domain.Models;

namespace WebSocketNFCeService.Infra.Services;

public class ConfigPersistenceService
{
    private static readonly string ConfigFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private MensagemSetup? _config;

    public MensagemSetup? Config => _config;

    public ConfigPersistenceService()
    {
        Carregar();
    }

    public void Salvar(MensagemSetup config)
    {
        _config = config;
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    private void Carregar()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                _config = JsonSerializer.Deserialize<MensagemSetup>(json);
            }
        }
        catch
        {
            // Fallback para appsettings.json
        }
    }
}
