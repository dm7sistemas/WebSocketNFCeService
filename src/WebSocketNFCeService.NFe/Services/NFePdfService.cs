using Microsoft.Extensions.Logging;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using NFeNFe = global::NFe.Classes.NFe;
using infNFe = global::NFe.Classes.Informacoes.infNFe;

using FormaPagamento = global::NFe.Classes.Informacoes.Pagamento.FormaPagamento;

namespace WebSocketNFCeService.NFe.Services;

public class NFePdfService : INFePdfService
{
    private readonly ILogger<NFePdfService> _logger;

    static NFePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public NFePdfService(ILogger<NFePdfService> logger)
    {
        _logger = logger;
    }

    public void Gerar(string chave, NFeNFe nfe, string? protocolo, string? diretorio)
    {
        if (string.IsNullOrWhiteSpace(diretorio))
        {
            _logger.LogWarning("Diretório de PDF não configurado. PDF não salvo.");
            return;
        }

        try
        {
            if (!Directory.Exists(diretorio))
                Directory.CreateDirectory(diretorio);

            var caminho = Path.Combine(diretorio, $"{chave}-nfe.pdf");
            var inf = nfe.infNFe;
            var tipoDoc = "DANFE NFC-e";

            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.ContinuousSize(80, Unit.Millimetre);
                    page.Margin(4, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Verdana"));

                    page.Content().Column(col =>
                    {
                        // ── Emitente (topo) ──────────────────────────────────────────
                        col.Item().AlignCenter().Text(inf.emit.xNome).Bold().FontSize(11);
                        if (!string.IsNullOrEmpty(inf.emit.CNPJ))
                            col.Item().AlignCenter().Text($"CNPJ: {FormatarCnpj(inf.emit.CNPJ)}").FontSize(7.5f).FontColor(Colors.Grey.Darken1);

                        // Tipo do documento
                        col.Item().PaddingTop(2).AlignCenter().Text(tipoDoc).Bold().FontSize(9);

                        // Número / série / data
                        col.Item().AlignCenter().Text(
                            $"NFC-e nº {inf.ide.nNF:D9}   Série: {inf.ide.serie}   {inf.ide.dhEmi:dd/MM/yyyy}")
                            .FontSize(7.5f).FontColor(Colors.Grey.Darken2);

                        LinhaTracejada(col);

                        // ── Destinatário ────────────────────────────────────────────
                        if (inf.dest != null)
                        {
                            col.Item().Text("Destinatário:").Bold().FontSize(7.5f).FontColor(Colors.Grey.Darken2);
                            col.Item().Text(inf.dest.xNome).FontSize(7.5f);
                            var docDest = inf.dest.CNPJ ?? inf.dest.CPF;
                            if (!string.IsNullOrWhiteSpace(docDest))
                                col.Item().Text($"CNPJ/CPF: {FormatarCnpj(docDest)}").FontSize(7.5f);
                            LinhaTracejada(col);
                        }

                        // ── Chave de Acesso ─────────────────────────────────────────
                        col.Item().Text("Chave de Acesso").Bold().FontSize(7).FontColor(Colors.Grey.Darken2);
                        col.Item().AlignCenter().Text(FormatarChaveEspacada(chave)).FontSize(6.5f).FontFamily("Courier New");

                        col.Item().PaddingTop(4).AlignCenter().Text("Consulte na SEFAZ pelo QR Code").FontSize(7).FontColor(Colors.Grey.Darken2);

                        // QR Code
                        var qrData = nfe.infNFeSupl?.qrCode ?? "";
                        if (!string.IsNullOrWhiteSpace(qrData))
                        {
                            using var qrGen = new QRCodeGenerator();
                            using var qrDataObj = qrGen.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.Q);
                            using var qrCode = new BitmapByteQRCode(qrDataObj);
                            var qrBytes = qrCode.GetGraphic(4);

                            col.Item().PaddingTop(2).AlignCenter().Width(100).Height(100).Image(qrBytes);
                        }

                        LinhaTracejada(col);

                        // ── Produtos ─────────────────────────────────────────────────
                        col.Item().Row(row =>
                        {
                            row.ConstantItem(8).Text("#").Bold().FontSize(7).FontColor(Colors.Grey.Darken2);
                            row.RelativeItem().Text("CÓDIGO/NOME DO PRODUTO").Bold().FontSize(7).FontColor(Colors.Grey.Darken2);
                            row.ConstantItem(16).AlignRight().Text("QT.").Bold().FontSize(7).FontColor(Colors.Grey.Darken2);
                            row.ConstantItem(20).AlignRight().Text("VL.UN.").Bold().FontSize(7).FontColor(Colors.Grey.Darken2);
                            row.ConstantItem(20).AlignRight().Text("VL.TOTAL").Bold().FontSize(7).FontColor(Colors.Grey.Darken2);
                        });

                        col.Item().LineHorizontal(0.5f);

                        var idx = 0;
                        foreach (var det in inf.det)
                        {
                            idx++;
                            var p = det.prod;

                            col.Item().PaddingVertical(0.5f).Row(row =>
                            {
                                row.ConstantItem(8).Text(idx.ToString()).FontSize(7).FontColor(Colors.Grey.Darken1);
                                row.RelativeItem().Text(p.cProd).FontSize(7).FontColor(Colors.Grey.Darken1);
                                row.ConstantItem(16).AlignRight().Text(p.qCom.ToString("F2")).FontSize(7);
                                row.ConstantItem(20).AlignRight().Text(p.vUnCom.ToString("F2")).FontSize(7);
                                row.ConstantItem(20).AlignRight().Text(p.vProd.ToString("F2")).FontSize(7);
                            });

                            // Descrição do produto (segunda linha)
                            col.Item().PaddingLeft(8).Text(p.xProd).FontSize(6.5f).FontColor(Colors.Grey.Darken1);

                            // NCM / CFOP / UN (info adicional)
                            var ncmVal = p.NCM.ToString();
                            var cfopVal = p.CFOP.ToString();
                            var ucomVal = p.uCom ?? "";
                            var ncmLine = string.Join("  ", new[]
                            {
                                !string.IsNullOrEmpty(ncmVal) && ncmVal != "00000000" ? $"NCM: {ncmVal}" : "",
                                !string.IsNullOrEmpty(cfopVal) ? $"CFOP: {cfopVal}" : "",
                                !string.IsNullOrEmpty(ucomVal) ? $"UN: {ucomVal}" : ""
                            }.Where(x => !string.IsNullOrEmpty(x)));

                            if (!string.IsNullOrEmpty(ncmLine))
                            {
                                col.Item().PaddingLeft(8).Text(ncmLine).FontSize(6).FontColor(Colors.Grey.Lighten1);
                            }
                        }

                        col.Item().LineHorizontal(0.5f);

                        // Total
                        col.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem().Text("VALOR TOTAL").Bold().FontSize(8);
                            row.ConstantItem(50).AlignRight().Text($"R$ {inf.total.ICMSTot.vNF:F2}").Bold().FontSize(8);
                        });

                        LinhaTracejada(col);

                        // ── Pagamentos ───────────────────────────────────────────────
                        col.Item().Text("PAGAMENTOS").Bold().FontSize(7.5f);

                        decimal? troco = null;
                        foreach (var pag in inf.pag)
                        {
                            foreach (var dp in pag.detPag)
                            {
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(ObterNomeFormaPagamento(dp.tPag)).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                                    row.ConstantItem(50).AlignRight().Text($"R$ {dp.vPag:F2}").Bold().FontSize(7.5f);
                                });
                            }
                            if (pag.vTroco > 0)
                                troco = pag.vTroco;
                        }

                        if (troco.HasValue)
                        {
                            col.Item().PaddingTop(2).Row(row =>
                            {
                                row.RelativeItem().Text("Troco").Bold().FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                                row.ConstantItem(50).AlignRight().Text($"R$ {troco.Value:F2}").Bold().FontSize(7.5f);
                            });
                        }

                        LinhaTracejada(col);

                        // ── Protocolo / rodapé ───────────────────────────────────────
                        col.Item().AlignCenter().Text($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm:ss}").FontSize(6.5f).FontColor(Colors.Grey.Lighten1);

                        if (!string.IsNullOrEmpty(chave))
                        {
                            col.Item().AlignCenter().Text($"Protocolo de Autorização: {FormatarChaveEspacada(chave)}")
                                .FontSize(6.5f).FontColor(Colors.Grey.Lighten1);
                        }
                    });
                });
            }).GeneratePdf(caminho);

            _logger.LogInformation("PDF salvo em: {Caminho}", caminho);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar PDF da NFCe {Chave}", chave);
        }
    }

    private static void LinhaTracejada(ColumnDescriptor col)
    {
        col.Item().PaddingVertical(2).AlignCenter()
            .Text(new string('-', 42)).FontSize(6).FontColor(Colors.Grey.Lighten1);
    }

    private static string FormatarChaveEspacada(string chave)
    {
        if (string.IsNullOrEmpty(chave)) return "";
        var grupos = new List<string>();
        for (int i = 0; i < chave.Length; i += 4)
        {
            if (i + 4 <= chave.Length)
                grupos.Add(chave.Substring(i, 4));
            else
                grupos.Add(chave[i..]);
        }
        return string.Join(" ", grupos);
    }

    private static string FormatarCnpj(string doc)
    {
        if (string.IsNullOrEmpty(doc)) return "";
        if (doc.Length == 14)
            return $"{doc[..2]}.{doc[2..5]}.{doc[5..8]}/{doc[8..12]}-{doc[12..]}";
        if (doc.Length == 11)
            return $"{doc[..3]}.{doc[3..6]}.{doc[6..9]}-{doc[9..]}";
        return doc;
    }

    private static string ObterNomeFormaPagamento(FormaPagamento tPag)
    {
        return tPag switch
        {
            FormaPagamento.fpDinheiro => "Dinheiro",
            FormaPagamento.fpCheque => "Cheque",
            FormaPagamento.fpCartaoCredito => "Cartão Crédito",
            FormaPagamento.fpCartaoDebito => "Cartão Débito",
            FormaPagamento.fpCartaoDaLoja => "Cartão da Loja",
            FormaPagamento.fpValeAlimentacao => "Vale Alimentação",
            FormaPagamento.fpValeRefeicao => "Vale Refeição",
            FormaPagamento.fpValePresente => "Vale Presente",
            FormaPagamento.fpValeCombustivel => "Vale Combustível",
            FormaPagamento.fpDuplicataMercantil => "Duplicata",
            FormaPagamento.fpBoletoBancario => "Boleto",
            FormaPagamento.fpDepositoBancario => "Depósito Bancário",
            FormaPagamento.fpPagamentoInstantaneoPIXDinamico => "PIX",
            FormaPagamento.fpTed => "TED",
            FormaPagamento.fpProgramadefidelidade => "Fidelidade",
            FormaPagamento.fpPagamentoInstantaneoPIXEstatico => "PIX",
            FormaPagamento.fpCreditoEmLoja => "Crédito em Loja",
            FormaPagamento.fpPagamentoEletronicoNaoInformado => "Pagamento Eletrônico",
            FormaPagamento.fpPagamentoInstantaneoPixAutomatico => "PIX",
            FormaPagamento.fpPagamentoTefBookTransfer => "TEF",
            FormaPagamento.fpSemPagamento => "Sem Pagamento",
            FormaPagamento.fpPagamentoPosterior => "Pagamento Posterior",
            FormaPagamento.fpOutro => "Outro",
            _ => $"Código {(int)tPag}"
        };
    }
}
