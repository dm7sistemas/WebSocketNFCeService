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

    /// "assinadas": o mes das notas, como esta na chave (AAMM, ex.: "2610").
    [JsonPropertyName("mes")]
    public string? Mes { get; set; }

    /// "guardar": o XML autorizado (procNFe) que o AZFood tem e este computador nao.
    [JsonPropertyName("xml")]
    public string? Xml { get; set; }

    /// "guardar": o PDF (base64) desenhado pelo AZFood, com o MESMO codigo da
    /// opcao DANFE das telas. Vindo, e ele que fica na pasta -- o PDF daqui e o
    /// da tela tem de ser identicos, e so um desenhista garante isso.
    [JsonPropertyName("pdf")]
    public string? Pdf { get; set; }

    /// "distribuicao": o ultimo NSU que o AZFood ja recebeu (a SEFAZ devolve os
    /// documentos seguintes). Vazio ou "0" = desde o comeco (ate 3 meses).
    [JsonPropertyName("ultNSU")]
    public string? UltNSU { get; set; }
}

/// Um documento que a SEFAZ distribuiu para o CNPJ da loja: o resumo de uma nota
/// (resNFe), a nota completa (procNFe) ou um evento (resEvento, procEventoNFe).
public class DocumentoDistribuido
{
    [JsonPropertyName("nsu")]
    public string Nsu { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("xml")]
    public string Xml { get; set; } = string.Empty;
}

/// Uma nota que este servico assinou e mandou (ou tentou mandar) para a SEFAZ.
public class NotaAssinada
{
    [JsonPropertyName("chave")]
    public string Chave { get; set; } = string.Empty;

    [JsonPropertyName("pedidoId")]
    public string PedidoId { get; set; } = string.Empty;
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

    /// "assinadas": toda nota do mes que saiu daqui. O AZFood compara com as que
    /// ele conhece e consulta as que sobram -- a resposta delas se perdeu.
    [JsonPropertyName("assinadas")]
    public List<NotaAssinada>? Assinadas { get; set; }

    /// "assinadas": chaves do mes que ja tem o XML autorizado / o PDF neste
    /// computador. O AZFood compara com as notas dele e manda repor o que falta.
    [JsonPropertyName("prontas")]
    public List<string>? Prontas { get; set; }

    [JsonPropertyName("pdfs")]
    public List<string>? Pdfs { get; set; }

    /// "assinadas": chaves com o arquivo assinado NA PASTA DO MES (a lista
    /// `assinadas` olha todas as pastas, para a busca de notas perdidas).
    [JsonPropertyName("assinadasNoMes")]
    public List<string>? AssinadasNoMes { get; set; }

    /// "guardar" e "consultar": o que foi gravado agora neste computador.
    [JsonPropertyName("gravouXml")]
    public bool GravouXml { get; set; }

    [JsonPropertyName("gravouPdf")]
    public bool GravouPdf { get; set; }

    /// "distribuicao": ate onde a SEFAZ entregou e ate onde existe documento.
    /// Iguais = nao ha mais nada, e a proxima consulta so depois de 1 hora.
    [JsonPropertyName("ultNSU")]
    public string? UltNSU { get; set; }

    [JsonPropertyName("maxNSU")]
    public string? MaxNSU { get; set; }

    [JsonPropertyName("documentos")]
    public List<DocumentoDistribuido>? Documentos { get; set; }

    [JsonPropertyName("erros")]
    public List<ErroNFe> Erros { get; set; } = new();
}
