using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

// O que acontece com a nota DEPOIS da emissao -- e as consultas que nao sao nota.
//
// Um modelo so para os cinco tipos ("cancelar", "cartaCorrecao", "consultar",
// "inutilizar", "statusServico"): cada um usa um pedaco, e o handler confere o
// pedaco de cada tipo antes de chegar na SEFAZ.
//
// UF, CNPJ e modelo saem da CHAVE quando ha chave: e ela que a SEFAZ confere, e
// um campo solto que discorde dela so produz rejeicao.
public class PedidoEventoFiscal
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    /// Id do pedido no AZFood. So volta na resposta.
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ambiente")]
    public string Ambiente { get; set; } = "homologacao";

    [JsonPropertyName("uf")]
    public string? Uf { get; set; }

    [JsonPropertyName("cnpj")]
    public string? Cnpj { get; set; }

    [JsonPropertyName("modelo")]
    public int Modelo { get; set; } = 65;

    [JsonPropertyName("chave")]
    public string? Chave { get; set; }

    [JsonPropertyName("protocolo")]
    public string? Protocolo { get; set; }

    [JsonPropertyName("justificativa")]
    public string? Justificativa { get; set; }

    [JsonPropertyName("correcao")]
    public string? Correcao { get; set; }

    /// nSeqEvento. Cancelamento e sempre 1; cada carta de correcao nova soma um.
    [JsonPropertyName("sequencia")]
    public int Sequencia { get; set; } = 1;

    [JsonPropertyName("serie")]
    public int Serie { get; set; }

    [JsonPropertyName("numeroInicial")]
    public int NumeroInicial { get; set; }

    [JsonPropertyName("numeroFinal")]
    public int NumeroFinal { get; set; }
}

public class ResultadoEventoFiscal
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sucesso")]
    public bool Sucesso { get; set; }

    [JsonPropertyName("codigoStatus")]
    public int CodigoStatus { get; set; }

    [JsonPropertyName("motivo")]
    public string Motivo { get; set; } = string.Empty;

    [JsonPropertyName("chave")]
    public string? Chave { get; set; }

    /// Protocolo do EVENTO (ou da inutilizacao). Na consulta, o da autorizacao.
    [JsonPropertyName("protocolo")]
    public string? Protocolo { get; set; }

    /// Consulta: "autorizada", "cancelada", "denegada", "inexistente" ou "outra".
    [JsonPropertyName("situacao")]
    public string? Situacao { get; set; }

    /// procEventoNFe (cancelamento, CC-e) ou procInutNFe (inutilizacao). E o
    /// arquivo que o contador precisa junto com o da nota.
    [JsonPropertyName("xml")]
    public string? Xml { get; set; }

    /// Consulta: o procNFe, quando este servico ainda tem a nota assinada.
    [JsonPropertyName("xmlAutorizado")]
    public string? XmlAutorizado { get; set; }

    /// Consulta: os eventos que a SEFAZ devolveu junto (procEventoNFe).
    [JsonPropertyName("eventos")]
    public List<string> Eventos { get; set; } = new();

    /// Status do servico: tempo medio de resposta da SEFAZ, em segundos.
    [JsonPropertyName("tempoMedio")]
    public int? TempoMedio { get; set; }

    [JsonPropertyName("erros")]
    public List<ErroNFe> Erros { get; set; } = new();
}
