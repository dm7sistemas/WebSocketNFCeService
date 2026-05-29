using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class ResultadoNFe
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sucesso")]
    public bool Sucesso { get; set; }

    [JsonPropertyName("codigoStatus")]
    public int CodigoStatus { get; set; }

    [JsonPropertyName("motivo")]
    public string Motivo { get; set; } = string.Empty;

    [JsonPropertyName("chaveAcesso")]
    public string? ChaveAcesso { get; set; }

    [JsonPropertyName("numeroProtocolo")]
    public string? NumeroProtocolo { get; set; }

    [JsonPropertyName("numeroNFe")]
    public int? NumeroNFe { get; set; }

    [JsonPropertyName("serie")]
    public int? Serie { get; set; }

    [JsonPropertyName("xmlAutorizado")]
    public string? XmlAutorizado { get; set; }

    [JsonPropertyName("erros")]
    public List<ErroNFe> Erros { get; set; } = new();
}

public class ErroNFe
{
    [JsonPropertyName("codigo")]
    public int Codigo { get; set; }

    [JsonPropertyName("mensagem")]
    public string Mensagem { get; set; } = string.Empty;
}
