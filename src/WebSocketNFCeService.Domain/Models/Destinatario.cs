using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class Destinatario
{
    [JsonPropertyName("cpf")]
    public string? Cpf { get; set; }

    [JsonPropertyName("cnpj")]
    public string? Cnpj { get; set; }

    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;

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
    public string? Cep { get; set; }

    [JsonPropertyName("telefone")]
    public string? Telefone { get; set; }

    [JsonPropertyName("indicadorContribuinte")]
    public int IndicadorContribuinte { get; set; } = 9;

    [JsonPropertyName("indicadorIE")]
    public int IndicadorIE { get; set; } = 9;
}
