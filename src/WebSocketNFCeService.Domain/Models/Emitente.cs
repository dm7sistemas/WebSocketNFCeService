using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class Emitente
{
    [JsonPropertyName("cnpj")]
    public string Cnpj { get; set; } = string.Empty;

    [JsonPropertyName("inscricaoEstadual")]
    public string InscricaoEstadual { get; set; } = string.Empty;

    [JsonPropertyName("inscricaoMunicipal")]
    public string? InscricaoMunicipal { get; set; }

    [JsonPropertyName("razaoSocial")]
    public string RazaoSocial { get; set; } = string.Empty;

    [JsonPropertyName("nomeFantasia")]
    public string NomeFantasia { get; set; } = string.Empty;

    [JsonPropertyName("logradouro")]
    public string Logradouro { get; set; } = string.Empty;

    [JsonPropertyName("numero")]
    public string Numero { get; set; } = string.Empty;

    [JsonPropertyName("complemento")]
    public string? Complemento { get; set; }

    [JsonPropertyName("bairro")]
    public string Bairro { get; set; } = string.Empty;

    [JsonPropertyName("codigoMunicipio")]
    public int CodigoMunicipio { get; set; }

    [JsonPropertyName("municipio")]
    public string Municipio { get; set; } = string.Empty;

    [JsonPropertyName("uf")]
    public string Uf { get; set; } = string.Empty;

    [JsonPropertyName("cep")]
    public string Cep { get; set; } = string.Empty;

    [JsonPropertyName("telefone")]
    public string Telefone { get; set; } = string.Empty;

    [JsonPropertyName("crt")]
    public int Crt { get; set; }
}
