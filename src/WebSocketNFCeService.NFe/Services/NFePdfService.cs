using Microsoft.Extensions.Logging;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using BarcodeStandard;
using SkiaSharp;
using NFeNFe = global::NFe.Classes.NFe;
using infNFe = global::NFe.Classes.Informacoes.infNFe;
using FormaPagamento = global::NFe.Classes.Informacoes.Pagamento.FormaPagamento;
using DFe.Classes.Flags;

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

    public void Gerar(string chave, NFeNFe nfe, string? protocolo, string? diretorioDestino)
    {
        try
        {
            if (diretorioDestino == null) return;
            if (!Directory.Exists(diretorioDestino))
            {
                Directory.CreateDirectory(diretorioDestino);
            }

            var arquivoPdf = Path.Combine(diretorioDestino, $"{chave}-nfe.pdf");
            
            if (nfe.infNFe.ide.mod == ModeloDocumento.NFe)
            {
                GerarDanfeA4(chave, nfe, arquivoPdf, protocolo);
            }
            else
            {
                GerarNFCeBobina(chave, nfe, arquivoPdf, protocolo);
            }
            
            _logger.LogInformation("PDF gerado em {ArquivoPdf}", arquivoPdf);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar PDF para chave {Chave}", chave);
            throw;
        }
    }

    private void GerarNFCeBobina(string chave, NFeNFe nfe, string caminho, string? protocolo)
    {
        var inf = nfe.infNFe;
        var endereco = inf.emit.enderEmit;

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(2, Unit.Millimetre);
                page.ContinuousSize(80, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Courier New"));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text(inf.emit.xFant ?? inf.emit.xNome).Bold().FontSize(12);
                    col.Item().AlignCenter().Text($"CNPJ: {FormatarCnpj(inf.emit.CNPJ)} IE: {inf.emit.IE}");
                    
                    if (endereco != null)
                    {
                        var strEnd = $"{endereco.xLgr}, {endereco.nro}";
                        if (!string.IsNullOrEmpty(endereco.xCpl)) strEnd += $" - {endereco.xCpl}";
                        col.Item().AlignCenter().Text(strEnd).FontSize(7);
                        col.Item().AlignCenter().Text($"{endereco.xMun} - {endereco.UF}").FontSize(7);
                    }

                    LinhaTracejada(col);
                    col.Item().AlignCenter().Text("DANFE NFC-e Documento Auxiliar da").Bold();
                    col.Item().AlignCenter().Text("Nota Fiscal de Consumidor Eletrônica").Bold();
                    col.Item().AlignCenter().Text("Não permite aproveitamento de crédito de ICMS").FontSize(7);
                    LinhaTracejada(col);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem(2).Text("CÓDIGO");
                        row.RelativeItem(4).Text("DESCRIÇÃO");
                        row.RelativeItem(1).AlignRight().Text("QTD");
                        row.RelativeItem(2).AlignRight().Text("UN");
                        row.RelativeItem(2).AlignRight().Text("VL UN");
                        row.RelativeItem(2).AlignRight().Text("VL TOT");
                    });

                    foreach (var det in inf.det)
                    {
                        var p = det.prod;
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(2).Text(p.cProd).FontSize(7);
                            row.RelativeItem(4).Text(p.xProd).FontSize(7);
                            row.RelativeItem(1).AlignRight().Text(p.qCom.ToString("0.##")).FontSize(7);
                            row.RelativeItem(2).AlignRight().Text(p.uCom).FontSize(7);
                            row.RelativeItem(2).AlignRight().Text(p.vUnCom.ToString("F2")).FontSize(7);
                            row.RelativeItem(2).AlignRight().Text(p.vProd.ToString("F2")).FontSize(7);
                        });
                    }

                    LinhaTracejada(col);
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("QTD. TOTAL DE ITENS").Bold();
                        row.ConstantItem(50).AlignRight().Text(inf.det.Count.ToString()).Bold();
                    });
                    
                    var vTotalDesc = inf.total.ICMSTot.vDesc;
                    if (vTotalDesc > 0)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("DESCONTO").Bold();
                            row.ConstantItem(50).AlignRight().Text(vTotalDesc.ToString("F2")).Bold();
                        });
                    }

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("VALOR TOTAL R$").Bold().FontSize(10);
                        row.ConstantItem(50).AlignRight().Text(inf.total.ICMSTot.vNF.ToString("F2")).Bold().FontSize(10);
                    });

                    LinhaTracejada(col);
                    col.Item().Text("FORMA DE PAGAMENTO").Bold();
                    
                    if (inf.pag != null)
                    {
                        foreach (var pag in inf.pag)
                        {
                            if (pag.detPag != null)
                            {
                                foreach (var dp in pag.detPag)
                                {
                                    col.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(ObterNomeFormaPagamento(dp.tPag)).FontSize(7.5f);
                                        row.ConstantItem(50).AlignRight().Text(dp.vPag.ToString("F2")).FontSize(7.5f);
                                    });
                                }
                            }
                            if (pag.vTroco > 0)
                            {
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Text("Troco").Bold().FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                                    row.ConstantItem(50).AlignRight().Text($"R$ {pag.vTroco:F2}").Bold().FontSize(7.5f);
                                });
                            }
                        }
                    }

                    LinhaTracejada(col);
                    col.Item().AlignCenter().Text($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm:ss}").FontSize(6.5f).FontColor(Colors.Grey.Lighten1);

                    if (!string.IsNullOrEmpty(chave))
                    {
                        col.Item().AlignCenter().Text($"Protocolo de Autorização: {FormatarChaveEspacada(chave)}")
                            .FontSize(6.5f).FontColor(Colors.Grey.Lighten1);
                    }
                });
            });
        }).GeneratePdf(caminho);
    }

    private void GerarDanfeA4(string chave, NFeNFe nfe, string caminho, string? protocolo)
    {
        var inf = nfe.infNFe;
        
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(5, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(7).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    // QUADRO: RECEBIMENTO
                    col.Item().Border(0.5f).Padding(2).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"RECEBEMOS DE {inf.emit.xNome} OS PRODUTOS/SERVIÇOS CONSTANTES DA NOTA FISCAL INDICADA AO LADO").FontSize(6);
                            c.Item().PaddingTop(5).Row(r =>
                            {
                                r.RelativeItem(2).Column(rc =>
                                {
                                    rc.Item().Text("DATA DE RECEBIMENTO").FontSize(5).Bold();
                                    rc.Item().Text(" ").FontSize(5);
                                });
                                r.RelativeItem(4).BorderLeft(0.5f).PaddingLeft(2).Column(rc =>
                                {
                                    rc.Item().Text("IDENTIFICAÇÃO E ASSINATURA DO RECEBEDOR").FontSize(5).Bold();
                                    rc.Item().Text(" ").FontSize(5);
                                });
                            });
                        });
                        row.ConstantItem(80).BorderLeft(0.5f).PaddingLeft(2).AlignCenter().Column(c =>
                        {
                            c.Item().Text("NF-e").FontSize(8).Bold();
                            c.Item().Text($"Nº {inf.ide.nNF:D9}").FontSize(8).Bold();
                            c.Item().Text($"SÉRIE {inf.ide.serie}").FontSize(8).Bold();
                        });
                    });

                    col.Item().PaddingVertical(3).AlignCenter().Text(new string('-', 100)).FontSize(6).FontColor(Colors.Grey.Medium);

                    // QUADRO: IDENTIFICAÇÃO DO EMITENTE
                    col.Item().Border(0.5f).Row(row =>
                    {
                        // Emitente (Logo + Dados)
                        row.RelativeItem(4).Padding(2).Column(c =>
                        {
                            c.Item().Text(inf.emit.xNome ?? "").Bold().FontSize(10);
                            c.Item().Text((inf.emit.enderEmit?.xLgr ?? "") + ", " + (inf.emit.enderEmit?.nro ?? ""));
                            c.Item().Text((inf.emit.enderEmit?.xBairro ?? "") + " - " + (inf.emit.enderEmit?.CEP ?? ""));
                            c.Item().Text((inf.emit.enderEmit?.xMun ?? "") + " - " + (inf.emit.enderEmit?.UF.ToString() ?? ""));
                            c.Item().Text("Fone: " + (inf.emit.enderEmit?.fone?.ToString() ?? ""));
                        });

                        // DANFE Info
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).AlignCenter().Column(c =>
                        {
                            c.Item().Text("DANFE").Bold().FontSize(12);
                            c.Item().Text("Documento Auxiliar da Nota").FontSize(6);
                            c.Item().Text("Fiscal Eletrônica").FontSize(6);
                            
                            c.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().AlignRight().Text("0 - Entrada").FontSize(6);
                                r.RelativeItem().PaddingLeft(2).Border(0.5f).AlignCenter().Text(inf.ide.tpNF == global::NFe.Classes.Informacoes.Identificacao.Tipos.TipoNFe.tnEntrada ? "0" : "1").Bold();
                            });
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().AlignRight().Text("1 - Saída").FontSize(6);
                                r.RelativeItem().Text(" ").FontSize(6); // align block
                            });
                            
                            c.Item().PaddingTop(2).Text($"Nº {inf.ide.nNF:D9}").Bold().FontSize(8);
                            c.Item().Text($"SÉRIE {inf.ide.serie}").Bold().FontSize(8);
                            c.Item().Text("Página 1 de 1").FontSize(6);
                        });

                        // Codigo de Barras e Chave
                        row.RelativeItem(4).BorderLeft(0.5f).Padding(2).Column(c =>
                        {
                            c.Item().Text("CONTROLE DO FISCO").FontSize(5).Bold();
                            var chr = chave ?? string.Empty;
                            if (chr.Length == 44)
                            {
                                try
                                {
                                    var bcBytes = GenerateBarcode128C(chr);
                                    c.Item().PaddingTop(2).Height(30).Image(bcBytes);
                                }
                                catch
                                {
                                    c.Item().Text("[Erro Cod. Barras]").FontSize(6);
                                }
                            }
                            c.Item().PaddingTop(2).Text("CHAVE DE ACESSO").FontSize(5).Bold();
                            c.Item().AlignCenter().Text(FormatarChaveEspacada(chr)).FontSize(8).Bold();
                            c.Item().Text("Consulta de autenticidade no portal nacional da NF-e").FontSize(5).AlignCenter();
                            c.Item().Text("www.nfe.fazenda.gov.br/portal ou no site da Sefaz Autorizadora").FontSize(5).AlignCenter();
                        });
                    });

                    // NATUREZA DA OPERACAO
                    col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                    {
                        row.RelativeItem(6).Padding(2).Column(c =>
                        {
                            c.Item().Text("NATUREZA DA OPERAÇÃO").FontSize(5).Bold();
                            c.Item().Text(inf.ide.natOp ?? "").Bold().FontSize(8);
                        });
                        row.RelativeItem(4).BorderLeft(0.5f).Padding(2).Column(c =>
                        {
                            c.Item().Text("PROTOCOLO DE AUTORIZAÇÃO DE USO").FontSize(5).Bold();
                            c.Item().Text(protocolo ?? "-").Bold().FontSize(8);
                        });
                    });

                    // INSCRICAO ESTADUAL
                    col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                    {
                        row.RelativeItem(3).Padding(2).Column(c =>
                        {
                            c.Item().Text("INSCRIÇÃO ESTADUAL").FontSize(5).Bold();
                            c.Item().Text(inf.emit.IE ?? "-").Bold().FontSize(8);
                        });
                        row.RelativeItem(3).BorderLeft(0.5f).Padding(2).Column(c =>
                        {
                            c.Item().Text("INSCRIÇÃO ESTADUAL DO SUBST. TRIBUTÁRIO").FontSize(5).Bold();
                            c.Item().Text(inf.emit.IEST ?? "-").Bold().FontSize(8);
                        });
                        row.RelativeItem(4).BorderLeft(0.5f).Padding(2).Column(c =>
                        {
                            c.Item().Text("CNPJ").FontSize(5).Bold();
                            c.Item().Text(FormatarCnpj(inf.emit.CNPJ ?? "")).Bold().FontSize(8);
                        });
                    });

                    // DESTINATÁRIO
                    if (inf.dest != null)
                    {
                        col.Item().PaddingTop(2).Text("DESTINATÁRIO / REMETENTE").FontSize(6).Bold();
                        col.Item().Border(0.5f).Row(row =>
                        {
                            row.RelativeItem(6).Padding(2).Column(c =>
                            {
                                c.Item().Text("NOME / RAZÃO SOCIAL").FontSize(5).Bold();
                                c.Item().Text(inf.dest.xNome ?? "").Bold().FontSize(8);
                            });
                            row.RelativeItem(3).BorderLeft(0.5f).Padding(2).Column(c =>
                            {
                                c.Item().Text("CNPJ / CPF").FontSize(5).Bold();
                                string docDest = "";
                                if (!string.IsNullOrEmpty(inf.dest.CNPJ)) docDest = inf.dest.CNPJ;
                                else if (!string.IsNullOrEmpty(inf.dest.CPF)) docDest = inf.dest.CPF;
                                c.Item().Text(FormatarCnpj(docDest)).Bold().FontSize(8);
                            });
                            row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c =>
                            {
                                c.Item().Text("DATA EMISSÃO").FontSize(5).Bold();
                                c.Item().Text($"{inf.ide.dhEmi:dd/MM/yyyy}").Bold().FontSize(8);
                            });
                        });
                        if (inf.dest.enderDest != null)
                        {
                            col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                            {
                                row.RelativeItem(5).Padding(2).Column(c =>
                                {
                                    c.Item().Text("ENDEREÇO").FontSize(5).Bold();
                                    c.Item().Text($"{inf.dest.enderDest.xLgr ?? ""}, {inf.dest.enderDest.nro ?? ""}").Bold().FontSize(8);
                                });
                                row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("BAIRRO / DISTRITO").FontSize(5).Bold();
                                    c.Item().Text(inf.dest.enderDest.xBairro ?? "").Bold().FontSize(8);
                                });
                                row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("CEP").FontSize(5).Bold();
                                    c.Item().Text(inf.dest.enderDest.CEP ?? "").Bold().FontSize(8);
                                });
                                row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("DATA SAÍDA").FontSize(5).Bold();
                                    c.Item().Text($"{inf.ide.dhSaiEnt:dd/MM/yyyy}").Bold().FontSize(8);
                                });
                            });
                            col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                            {
                                row.RelativeItem(4).Padding(2).Column(c =>
                                {
                                    c.Item().Text("MUNICÍPIO").FontSize(5).Bold();
                                    c.Item().Text(inf.dest.enderDest.xMun ?? "").Bold().FontSize(8);
                                });
                                row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("FONE / FAX").FontSize(5).Bold();
                                    c.Item().Text(inf.dest.enderDest.fone?.ToString() ?? "").Bold().FontSize(8);
                                });
                                row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("UF").FontSize(5).Bold();
                                    c.Item().Text(inf.dest.enderDest.UF.ToString() ?? "").Bold().FontSize(8);
                                });
                                row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("INSCRIÇÃO ESTADUAL").FontSize(5).Bold();
                                    c.Item().Text(inf.dest.IE ?? "").Bold().FontSize(8);
                                });
                                row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c =>
                                {
                                    c.Item().Text("HORA SAÍDA").FontSize(5).Bold();
                                    c.Item().Text($"{inf.ide.dhSaiEnt:HH:mm}").Bold().FontSize(8);
                                });
                            });
                        }
                    }

                    // FATURA / DUPLICATAS
                    col.Item().PaddingTop(2).Text("FATURA / DUPLICATAS").FontSize(6).Bold();
                    col.Item().Border(0.5f).Padding(2).Row(row =>
                    {
                        if (inf.cobr != null && inf.cobr.dup != null && inf.cobr.dup.Any())
                        {
                            foreach (var dup in inf.cobr.dup)
                            {
                                row.AutoItem().PaddingRight(5).Column(c =>
                                {
                                    c.Item().Text($"Nº: {dup.nDup}").FontSize(6);
                                    c.Item().Text($"Venc: {(dup.dVenc.HasValue ? dup.dVenc.Value.ToString("dd/MM/yyyy") : "")}").FontSize(6);
                                    c.Item().Text($"Valor: R$ {dup.vDup:F2}").FontSize(6);
                                });
                            }
                        }
                        else
                        {
                            row.RelativeItem().Text("-").FontSize(6);
                        }
                    });

                    // CÁLCULO DO IMPOSTO
                    col.Item().PaddingTop(2).Text("CÁLCULO DO IMPOSTO").FontSize(6).Bold();
                    col.Item().Border(0.5f).Row(row =>
                    {
                        row.RelativeItem().Padding(2).Column(c => { c.Item().Text("BASE DE CÁLCULO DO ICMS").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vBC:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("VALOR DO ICMS").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vICMS:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("BASE CÁLCULO ICMS ST").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vBCST:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("VALOR DO ICMS ST").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vST:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("VALOR TOTAL PRODUTOS").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vProd:F2}").Bold().FontSize(8); });
                    });
                    col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                    {
                        row.RelativeItem().Padding(2).Column(c => { c.Item().Text("VALOR DO FRETE").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vFrete:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("VALOR DO SEGURO").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vSeg:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("DESCONTO").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vDesc:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("OUTRAS DESPESAS").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vOutro:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("VALOR DO IPI").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vIPI:F2}").Bold().FontSize(8); });
                        row.RelativeItem().BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("VALOR TOTAL DA NOTA").FontSize(5).Bold(); c.Item().AlignRight().Text($"{inf.total.ICMSTot.vNF:F2}").Bold().FontSize(8); });
                    });

                    // TRANSPORTADOR
                    col.Item().PaddingTop(2).Text("TRANSPORTADOR / VOLUMES TRANSPORTADOS").FontSize(6).Bold();
                    col.Item().Border(0.5f).Row(row =>
                    {
                        var t = inf.transp?.transporta;
                        row.RelativeItem(4).Padding(2).Column(c => { c.Item().Text("NOME / RAZÃO SOCIAL").FontSize(5).Bold(); c.Item().Text(t?.xNome ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("FRETE POR CONTA").FontSize(5).Bold(); c.Item().Text(inf.transp?.modFrete.ToString() ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("CÓD. ANTT").FontSize(5).Bold(); c.Item().Text("-").FontSize(7).Bold(); });
                        row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("PLACA").FontSize(5).Bold(); c.Item().Text("-").FontSize(7).Bold(); });
                        row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("UF").FontSize(5).Bold(); c.Item().Text("-").FontSize(7).Bold(); });
                        
                        string tDoc = "";
                        if (t != null) {
                            if (!string.IsNullOrEmpty(t.CNPJ)) tDoc = t.CNPJ;
                            else if (!string.IsNullOrEmpty(t.CPF)) tDoc = t.CPF;
                        }
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("CNPJ / CPF").FontSize(5).Bold(); c.Item().Text(FormatarCnpj(tDoc)).FontSize(7).Bold(); });
                    });
                    col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                    {
                        var t = inf.transp?.transporta;
                        row.RelativeItem(5).Padding(2).Column(c => { c.Item().Text("ENDEREÇO").FontSize(5).Bold(); c.Item().Text(t?.xEnder ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(3).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("MUNICÍPIO").FontSize(5).Bold(); c.Item().Text(t?.xMun ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(1).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("UF").FontSize(5).Bold(); c.Item().Text(t?.UF.ToString() ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("INSCRIÇÃO ESTADUAL").FontSize(5).Bold(); c.Item().Text(t?.IE ?? "-").FontSize(7).Bold(); });
                    });
                    col.Item().BorderLeft(0.5f).BorderRight(0.5f).BorderBottom(0.5f).Row(row =>
                    {
                        var v = inf.transp?.vol?.FirstOrDefault();
                        row.RelativeItem(2).Padding(2).Column(c => { c.Item().Text("QUANTIDADE").FontSize(5).Bold(); c.Item().Text(v?.qVol?.ToString() ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("ESPÉCIE").FontSize(5).Bold(); c.Item().Text(v?.esp ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("MARCA").FontSize(5).Bold(); c.Item().Text(v?.marca ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("NUMERAÇÃO").FontSize(5).Bold(); c.Item().Text(v?.nVol ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("PESO BRUTO").FontSize(5).Bold(); c.Item().Text(v?.pesoB?.ToString("F3") ?? "-").FontSize(7).Bold(); });
                        row.RelativeItem(2).BorderLeft(0.5f).Padding(2).Column(c => { c.Item().Text("PESO LÍQUIDO").FontSize(5).Bold(); c.Item().Text(v?.pesoL?.ToString("F3") ?? "-").FontSize(7).Bold(); });
                    });

                    // PRODUTOS
                    col.Item().PaddingTop(2).Text("DADOS DO PRODUTO / SERVIÇO").FontSize(6).Bold();
                    col.Item().Border(0.5f).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40); // Cod
                            columns.RelativeColumn();   // Desc
                            columns.ConstantColumn(35); // NCM
                            columns.ConstantColumn(20); // CST
                            columns.ConstantColumn(20); // CFOP
                            columns.ConstantColumn(15); // UN
                            columns.ConstantColumn(30); // Qtd
                            columns.ConstantColumn(35); // V.Un
                            columns.ConstantColumn(40); // V.Tot
                            columns.ConstantColumn(35); // BC ICMS
                            columns.ConstantColumn(30); // V.ICMS
                            columns.ConstantColumn(30); // V.IPI
                            columns.ConstantColumn(20); // %ICMS
                            columns.ConstantColumn(20); // %IPI
                        });

                        table.Header(header =>
                        {
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).Text("CÓDIGO").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).Text("DESCRIÇÃO").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).Text("NCM/SH").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).Text("CST").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).Text("CFOP").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).Text("UN").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("QTD.").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("V.UNIT").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("V.TOTAL").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("BC ICMS").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("V.ICMS").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("V.IPI").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).BorderRight(0.5f).Padding(1).AlignRight().Text("%ICMS").FontSize(5).Bold();
                            header.Cell().BorderBottom(0.5f).Padding(1).AlignRight().Text("%IPI").FontSize(5).Bold();
                        });

                        foreach (var det in inf.det)
                        {
                            var p = det.prod;
                            var i = det.imposto;
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).Text(p.cProd ?? "").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).Text(p.xProd ?? "").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).Text(p.NCM ?? "").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).Text("").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).Text(p.CFOP.ToString()).FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).Text(p.uCom ?? "").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text(p.qCom.ToString("F2")).FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text(p.vUnCom.ToString("F4")).FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text(p.vProd.ToString("F2")).FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text("0.00").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text("0.00").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text("0.00").FontSize(6);
                            table.Cell().BorderRight(0.5f).BorderBottom(0.1f).Padding(1).AlignRight().Text("0.00").FontSize(6);
                            table.Cell().BorderBottom(0.1f).Padding(1).AlignRight().Text("0.00").FontSize(6);
                        }
                    });

                });

                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(2).Text("DADOS ADICIONAIS").FontSize(6).Bold();
                    col.Item().Border(0.5f).Row(row =>
                    {
                        row.RelativeItem(7).Padding(2).MinHeight(60).Column(c =>
                        {
                            c.Item().Text("INFORMAÇÕES COMPLEMENTARES").FontSize(5).Bold();
                            c.Item().Text(inf.infAdic?.infCpl ?? "").FontSize(7);
                        });
                        row.RelativeItem(3).BorderLeft(0.5f).Padding(2).Column(c =>
                        {
                            c.Item().Text("RESERVADO AO FISCO").FontSize(5).Bold();
                            c.Item().Text(inf.infAdic?.infAdFisco ?? "").FontSize(7);
                        });
                    });
                    col.Item().PaddingTop(2).AlignCenter()
                        .Text($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm:ss} - Emitido por AZFood - Restaurantes de A a Z")
                        .FontSize(5).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(caminho);
    }

    private byte[] GenerateBarcode128C(string data)
    {
        var b = new Barcode();
        using var img = b.Encode(BarcodeStandard.Type.Code128C, data, SKColors.Black, SKColors.White, 350, 50);
        using var imgData = img.Encode(SKEncodedImageFormat.Png, 100);
        return imgData.ToArray();
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
