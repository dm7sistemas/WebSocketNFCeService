using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class MensagemWebSocket
{
    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }
}

public class MensagemSetup
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "setup";

    [JsonPropertyName("certificado")]
    public CertificadoSetup? Certificado { get; set; }

    [JsonPropertyName("csc")]
    public CscSetup? Csc { get; set; }

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 60000;
}

public class CertificadoSetup
{
    [JsonPropertyName("arquivo")]
    public string Arquivo { get; set; } = string.Empty;

    [JsonPropertyName("senha")]
    public string Senha { get; set; } = string.Empty;

    [JsonPropertyName("serial")]
    public string? Serial { get; set; }
}

public class CscSetup
{
    [JsonPropertyName("idToken")]
    public string IdToken { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

public class ResultadoSetup
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "setup";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("mensagem")]
    public string? Mensagem { get; set; }
}
