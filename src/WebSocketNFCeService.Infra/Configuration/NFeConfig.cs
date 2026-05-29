namespace WebSocketNFCeService.Infra.Configuration;

public class NFeConfig
{
    public const string SectionName = "NFeConfig";

    public string Ambiente { get; set; } = "Homologacao";
    public CertificadoConfig Certificado { get; set; } = new();
    public CSCConfig CSC { get; set; } = new();
    public int Timeout { get; set; } = 60000;
    public CloudApiConfig CloudApi { get; set; } = new();
}

public class CloudApiConfig
{
    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class CertificadoConfig
{
    public string Arquivo { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
    public string? Serial { get; set; }
}

public class CSCConfig
{
    public string IdToken { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
