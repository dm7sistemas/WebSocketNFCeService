using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class Destinatario
{
    [JsonPropertyName("cpf")]
    public string? Cpf { get; set; }

    [JsonPropertyName("cnpj")]
    public string? Cnpj { get; set; }

    // NULO quando nao veio, e nao string vazia.
    //
    // O AZFood OMITE `nome` quando o cliente so informou o CPF/CNPJ (commit
    // 18a43af, 04/09/2026) -- xNome e opcional na NFC-e, mas quando presente a
    // SEFAZ exige conteudo. Com o default `string.Empty` este modelo devolvia o
    // "" que o app tinha acabado de tirar, o mapper escrevia `<xNome></xNome>` e
    // a nota voltava recusada no schema do mesmo jeito. So em PRODUCAO: na
    // homologacao o NFeService sobrescreve o nome, e o erro nunca aparecia em
    // teste.
    [JsonPropertyName("nome")]
    public string? Nome { get; set; }

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

    // Inscricao Estadual do destinatario. FALTAVA -- e o AZFood mandava.
    //
    // Sem esta propriedade o System.Text.Json descartava o campo calado, e o
    // mapper escrevia `indIEDest = 1` (contribuinte) sem a tag <IE>. A SEFAZ
    // devolve "NF-e sem informacao da IE do destinatario". GRAVETOS, 12/09/2026:
    // a primeira NF-e da casa para uma EMPRESA contribuinte -- todas as
    // anteriores foram para CPF, que nao leva IE, e o buraco nunca apareceu.
    [JsonPropertyName("inscricaoEstadual")]
    public string? InscricaoEstadual { get; set; }
}
