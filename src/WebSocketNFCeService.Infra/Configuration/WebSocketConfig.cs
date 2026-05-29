namespace WebSocketNFCeService.Infra.Configuration;

public class WebSocketConfig
{
    public const string SectionName = "WebSocketConfig";

    public int Porta { get; set; } = 8080;
    public string Path { get; set; } = "/ws/nfce";
}
