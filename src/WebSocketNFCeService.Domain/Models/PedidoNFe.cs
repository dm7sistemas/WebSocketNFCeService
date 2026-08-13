using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class PedidoNFe
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ambiente")]
    public string Ambiente { get; set; } = "homologacao";

    [JsonPropertyName("emitente")]
    public Emitente? Emitente { get; set; }

    [JsonPropertyName("destinatario")]
    public Destinatario? Destinatario { get; set; }

    [JsonPropertyName("produtos")]
    public List<ProdutoNFe> Produtos { get; set; } = new();

    [JsonPropertyName("nfe")]
    public DadosNFe? NFe { get; set; }

    /// Opcional. Ausente = modalidade 9 (sem frete), como sempre foi.
    [JsonPropertyName("transporte")]
    public Transporte? Transporte { get; set; }

    /// Opcional. Ausente = nota sem fatura/duplicatas.
    [JsonPropertyName("cobranca")]
    public Cobranca? Cobranca { get; set; }

    /// Opcional.
    [JsonPropertyName("informacoesAdicionais")]
    public InformacoesAdicionais? InformacoesAdicionais { get; set; }
}

public class DadosNFe
{
    [JsonPropertyName("numero")]
    public int Numero { get; set; }

    [JsonPropertyName("modelo")]
    public int Modelo { get; set; } = 65;

    [JsonPropertyName("serie")]
    public int Serie { get; set; }

    [JsonPropertyName("naturezaOperacao")]
    public string NaturezaOperacao { get; set; } = "VENDA";

    [JsonPropertyName("finalidade")]
    public int Finalidade { get; set; } = 1;

    [JsonPropertyName("consumidorFinal")]
    public int ConsumidorFinal { get; set; } = 1;

    [JsonPropertyName("presencaComprador")]
    public int PresencaComprador { get; set; } = 1;

    [JsonPropertyName("formaPagamento")]
    public string FormaPagamento { get; set; } = "dinheiro";

    [JsonPropertyName("valorPagamento")]
    public decimal ValorPagamento { get; set; }

    [JsonPropertyName("tipoPagamento")]
    public int TipoPagamento { get; set; } = 1;

    [JsonPropertyName("valorTroco")]
    public decimal ValorTroco { get; set; }

    [JsonPropertyName("valorGorjeta")]
    public decimal ValorGorjeta { get; set; }

    [JsonPropertyName("valorDesconto")]
    public decimal ValorDesconto { get; set; }

    [JsonPropertyName("parcelas")]
    public List<Parcela>? Parcelas { get; set; }

    [JsonPropertyName("cnpjCredenciadora")]
    public string? CnpjCredenciadora { get; set; }

    [JsonPropertyName("codigoAutorizacao")]
    public string? CodigoAutorizacao { get; set; }
}

public class Parcela
{
    [JsonPropertyName("formaPagamento")]
    public string FormaPagamento { get; set; } = "dinheiro";

    [JsonPropertyName("valor")]
    public decimal Valor { get; set; }

    [JsonPropertyName("tipoPagamento")]
    public int TipoPagamento { get; set; } = 1;

    [JsonPropertyName("cnpjCredenciadora")]
    public string? CnpjCredenciadora { get; set; }

    [JsonPropertyName("bandeiraCartao")]
    public string? BandeiraCartao { get; set; }

    [JsonPropertyName("codigoAutorizacao")]
    public string? CodigoAutorizacao { get; set; }

    [JsonPropertyName("cnpjRecebedor")]
    public string? CnpjRecebedor { get; set; }

    [JsonPropertyName("terminalPagamento")]
    public string? TerminalPagamento { get; set; }
}

// ── Grupos que SÓ a NF-e (modelo 55) usa ────────────────────────────────────
//
// Todos OPCIONAIS, e isso é a regra: quem não mandar nada continua emitindo
// exatamente como antes. Nenhum destes grupos vira obrigatório para a NFC-e, e
// nenhum deles impede a emissão da NF-e quando ausente -- o layout 4.00 aceita
// nota sem transporte, sem cobrança e sem informação adicional.
//
// O DANFE A4 já sabe desenhar os três (quadro de transporte, quadro de
// fatura/duplicatas e o campo de dados adicionais); até agora o mapper é que
// nunca os preenchia.

/// <summary>
/// Grupo `transp`. Sem ele, a nota sai com modalidade 9 (sem frete), que é o
/// que este serviço sempre fez.
/// </summary>
public class Transporte
{
    /// 0=emitente/CIF, 1=destinatário/FOB, 2=terceiros,
    /// 3=próprio por conta do remetente, 4=próprio por conta do destinatário,
    /// 9=sem frete. Nulo cai em 9.
    [JsonPropertyName("modalidadeFrete")]
    public int? ModalidadeFrete { get; set; }

    [JsonPropertyName("transportadora")]
    public Transportadora? Transportadora { get; set; }

    [JsonPropertyName("placa")]
    public string? Placa { get; set; }

    [JsonPropertyName("ufPlaca")]
    public string? UfPlaca { get; set; }

    [JsonPropertyName("rntc")]
    public string? Rntc { get; set; }

    [JsonPropertyName("volumes")]
    public List<Volume>? Volumes { get; set; }
}

public class Transportadora
{
    [JsonPropertyName("cnpj")]
    public string? Cnpj { get; set; }

    [JsonPropertyName("cpf")]
    public string? Cpf { get; set; }

    [JsonPropertyName("nome")]
    public string? Nome { get; set; }

    [JsonPropertyName("inscricaoEstadual")]
    public string? InscricaoEstadual { get; set; }

    [JsonPropertyName("endereco")]
    public string? Endereco { get; set; }

    [JsonPropertyName("municipio")]
    public string? Municipio { get; set; }

    [JsonPropertyName("uf")]
    public string? Uf { get; set; }
}

public class Volume
{
    /// Contagem de volumes -- inteiro, e nao decimal: o schema define qVol
    /// como quantidade de caixas/fardos, nao como peso.
    [JsonPropertyName("quantidade")]
    public int? Quantidade { get; set; }

    [JsonPropertyName("especie")]
    public string? Especie { get; set; }

    [JsonPropertyName("marca")]
    public string? Marca { get; set; }

    [JsonPropertyName("numeracao")]
    public string? Numeracao { get; set; }

    [JsonPropertyName("pesoLiquido")]
    public decimal? PesoLiquido { get; set; }

    [JsonPropertyName("pesoBruto")]
    public decimal? PesoBruto { get; set; }
}

/// <summary>
/// Grupo `cobr` — a venda A PRAZO. Não confundir com `Parcelas`, que é o
/// grupo `pag`: aquele diz COMO foi pago no caixa (dinheiro, cartão, PIX);
/// este diz QUANDO vai ser pago (duplicata, vencimento).
///
/// A SEFAZ exige que a soma das duplicatas bata com o valor líquido da fatura.
/// Quando `fatura` não vem, ela é montada a partir das duplicatas (ver mapper),
/// justamente para essa conta nunca depender de quem chamou.
/// </summary>
public class Cobranca
{
    [JsonPropertyName("fatura")]
    public Fatura? Fatura { get; set; }

    [JsonPropertyName("duplicatas")]
    public List<Duplicata>? Duplicatas { get; set; }
}

public class Fatura
{
    [JsonPropertyName("numero")]
    public string? Numero { get; set; }

    [JsonPropertyName("valorOriginal")]
    public decimal? ValorOriginal { get; set; }

    [JsonPropertyName("valorDesconto")]
    public decimal? ValorDesconto { get; set; }

    [JsonPropertyName("valorLiquido")]
    public decimal? ValorLiquido { get; set; }
}

public class Duplicata
{
    [JsonPropertyName("numero")]
    public string? Numero { get; set; }

    /// "2026-09-15" (ISO). Só a data importa; hora é ignorada.
    [JsonPropertyName("vencimento")]
    public string? Vencimento { get; set; }

    [JsonPropertyName("valor")]
    public decimal Valor { get; set; }
}

/// <summary>Grupo `infAdic` — observações da nota.</summary>
public class InformacoesAdicionais
{
    /// Vai em `infCpl`. É onde normalmente entra o número do pedido de compra
    /// do cliente, em venda para empresa.
    [JsonPropertyName("complementar")]
    public string? Complementar { get; set; }

    /// Vai em `infAdFisco`. Reservado ao interesse do fisco.
    [JsonPropertyName("fisco")]
    public string? Fisco { get; set; }
}

/// <summary>
/// Grupo `rastro` do item — rastreabilidade por lote, para recall.
///
/// OS QUATRO PRIMEIROS ANDAM JUNTOS. O schema da NF-e exige nLote, qLote, dFab
/// e dVal quando o grupo existe; mandar só o número do lote faz a SEFAZ
/// rejeitar a nota inteira. Por isso a validação recusa o grupo incompleto
/// ANTES de emitir -- rejeição da SEFAZ custa número da série.
/// </summary>
public class LoteProduto
{
    [JsonPropertyName("numero")]
    public string Numero { get; set; } = string.Empty;

    [JsonPropertyName("quantidade")]
    public decimal Quantidade { get; set; }

    /// "2026-08-01" (ISO).
    [JsonPropertyName("fabricacao")]
    public string? Fabricacao { get; set; }

    /// "2027-02-01" (ISO).
    [JsonPropertyName("validade")]
    public string? Validade { get; set; }

    /// Código de agregação, opcional mesmo no schema.
    [JsonPropertyName("codigoAgregacao")]
    public string? CodigoAgregacao { get; set; }
}
