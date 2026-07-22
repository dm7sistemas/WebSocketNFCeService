using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class ProdutoNFe
{
    [JsonPropertyName("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [JsonPropertyName("codigoBarras")]
    public string? CodigoBarras { get; set; }

    [JsonPropertyName("descricao")]
    public string Descricao { get; set; } = string.Empty;

    [JsonPropertyName("ncm")]
    public string Ncm { get; set; } = string.Empty;

    [JsonPropertyName("cfop")]
    public string Cfop { get; set; } = string.Empty;

    [JsonPropertyName("unidade")]
    public string Unidade { get; set; } = "UN";

    [JsonPropertyName("quantidade")]
    public decimal Quantidade { get; set; }

    [JsonPropertyName("valorUnitario")]
    public decimal ValorUnitario { get; set; }

    [JsonPropertyName("valorTotal")]
    public decimal ValorTotal { get; set; }

    [JsonPropertyName("valorDesconto")]
    public decimal ValorDesconto { get; set; }

    [JsonPropertyName("valorFrete")]
    public decimal ValorFrete { get; set; }

    [JsonPropertyName("valorSeguro")]
    public decimal ValorSeguro { get; set; }

    [JsonPropertyName("valorOutrasDespesas")]
    public decimal ValorOutrasDespesas { get; set; }

    [JsonPropertyName("cstIcms")]
    public string CstIcms { get; set; } = "00";

    [JsonPropertyName("aliquotaIcms")]
    public decimal AliquotaIcms { get; set; }

    [JsonPropertyName("cstPis")]
    public string CstPis { get; set; } = "01";

    [JsonPropertyName("aliquotaPis")]
    public decimal AliquotaPis { get; set; }

    [JsonPropertyName("cstCofins")]
    public string CstCofins { get; set; } = "01";

    [JsonPropertyName("aliquotaCofins")]
    public decimal AliquotaCofins { get; set; }

    // ── IBS/CBS (Reforma Tributária — NT 2025.002) ──────────────────────────
    // Opt-in: só é mapeado para o grupo <IBSCBS> se CstIbsCbs vier preenchido.
    // Produto sem esses campos emite exatamente como antes (só ICMS/PIS/COFINS).
    // As alíquotas vêm no payload (configuração/contador), nunca hardcoded aqui.

    // CST do IBS/CBS (ex.: "000" = tributação integral). Ver enum CST da lib.
    [JsonPropertyName("cstIbsCbs")]
    public string? CstIbsCbs { get; set; }

    // Código de Classificação Tributária (cClassTrib) — ex.: "000001".
    [JsonPropertyName("cClassTrib")]
    public string? CClassTrib { get; set; }

    // Alíquota do IBS Estadual (%).
    [JsonPropertyName("aliquotaIbsUf")]
    public decimal AliquotaIbsUf { get; set; }

    // Alíquota do IBS Municipal (%).
    [JsonPropertyName("aliquotaIbsMun")]
    public decimal AliquotaIbsMun { get; set; }

    // Alíquota da CBS (%).
    [JsonPropertyName("aliquotaCbs")]
    public decimal AliquotaCbs { get; set; }
}
