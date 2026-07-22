using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using NFe.Classes;
using NFe.Classes.Informacoes;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Detalhe.Tributacao;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual.Tipos;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal.Tipos;
using NFe.Classes.Informacoes.Emitente;
using NFe.Classes.Informacoes.Identificacao;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Total;
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Transporte;
using DFe.Classes;
using WebSocketNFCeService.Domain.Models;
using NFeNFe = global::NFe.Classes.NFe;
// IBS/CBS (Reforma Tributária — NT 2025.002). Aliases pra evitar ambiguidade
// entre os grupos de item e os de total, e com o CST de outros tributos.
using IbsCbsItem = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.IBSCBS;
using GIbsCbs = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.gIBSCBS;
using GIbsUf = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesIbs.gIBSUF;
using GIbsMun = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesIbs.gIBSMun;
using GCbsItem = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesCbs.gCBS;
using CstIbsCbsEnum = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.Tipos.CST;
using IbsCbsTot = NFe.Classes.Informacoes.Total.IbsCbs.IBSCBSTot;
using GIbsTot = NFe.Classes.Informacoes.Total.IbsCbs.Ibs.gIBS;
using GIbsUfTot = NFe.Classes.Informacoes.Total.IbsCbs.Ibs.gIBSUFTotal;
using GIbsMunTot = NFe.Classes.Informacoes.Total.IbsCbs.Ibs.gIBSMunTotal;
using GCbsTot = NFe.Classes.Informacoes.Total.IbsCbs.Cbs.gCBSTotal;

namespace WebSocketNFCeService.NFe.Mappers;

public static class PedidoToNFeMapper
{
    public static NFeNFe Mapear(PedidoNFe pedido)
    {
        var nfe = new NFeNFe();
        var emitente = pedido.Emitente!;
        var destinatario = pedido.Destinatario;

        var globalDiscount = pedido.NFe?.ValorDesconto ?? 0m;
        var distributedDiscounts = CalcularDescontosDistribuidos(pedido.Produtos, globalDiscount);

        nfe.infNFe = new infNFe
        {
            versao = "4.00",
            ide = MapearIdentificacao(pedido),
            emit = MapearEmitente(emitente),
            dest = destinatario is not null &&
                    (!string.IsNullOrWhiteSpace(destinatario.Cpf) ||
                     !string.IsNullOrWhiteSpace(destinatario.Cnpj))
                 ? MapearDestinatario(destinatario) : null,
            transp = new transp
            {
                modFrete = ModalidadeFrete.mfSemFrete
            },
            pag = new List<pag> { MapearPagamento(pedido, distributedDiscounts) },
            total = new total
            {
                ICMSTot = new ICMSTot()
            }
        };

        var tpAmb = pedido.Ambiente.Equals("producao", StringComparison.OrdinalIgnoreCase)
            ? TipoAmbiente.Producao : TipoAmbiente.Homologacao;
        var detalhes = MapearProdutos(pedido.Produtos, distributedDiscounts, tpAmb, emitente.Crt);
        nfe.infNFe.det = detalhes;
        nfe.infNFe.total.ICMSTot = CalcularICMSTot(pedido, distributedDiscounts, emitente.Crt);
        // IBS/CBS: só adiciona o total se algum item trouxe o grupo (opt-in).
        nfe.infNFe.total.IBSCBSTot = CalcularIbsCbsTot(detalhes);

        return nfe;
    }

    private static decimal[] CalcularDescontosDistribuidos(List<ProdutoNFe> produtos, decimal globalDiscount)
    {
        var distributedDiscounts = new decimal[produtos.Count];
        if (globalDiscount <= 0 || produtos.Count == 0) return distributedDiscounts;

        var totalProdutos = produtos.Sum(p => p.ValorTotal);
        if (totalProdutos > 0)
        {
            decimal allocatedDiscount = 0m;
            for (int i = 0; i < produtos.Count - 1; i++)
            {
                var itemDiscount = Math.Round(globalDiscount * (produtos[i].ValorTotal / totalProdutos), 2);
                distributedDiscounts[i] = itemDiscount;
                allocatedDiscount += itemDiscount;
            }
            distributedDiscounts[produtos.Count - 1] = globalDiscount - allocatedDiscount;
        }
        return distributedDiscounts;
    }

    private static ide MapearIdentificacao(PedidoNFe pedido)
    {
        var emitente = pedido.Emitente!;
        var dadosNfe = pedido.NFe!;
        var estado = ObterEstado(emitente.Uf);

        return new ide
        {
            cUF = estado,
            cNF = new Random().Next(10000000, 99999999).ToString(),
            natOp = dadosNfe.NaturezaOperacao,
            mod = ModeloDocumento.NFCe,
            serie = dadosNfe.Serie,
            nNF = dadosNfe.Numero,
            dhEmi = DateTime.Now,
            tpNF = TipoNFe.tnSaida,
            idDest = DestinoOperacao.doInterna,
            cMunFG = emitente.CodigoMunicipio,
            tpImp = TipoImpressao.tiNFCe,
            tpEmis = TipoEmissao.teNormal,
            tpAmb = pedido.Ambiente.Equals("producao", StringComparison.OrdinalIgnoreCase)
                ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            finNFe = (FinalidadeNFe)dadosNfe.Finalidade,
            indFinal = dadosNfe.ConsumidorFinal == 1
                ? ConsumidorFinal.cfConsumidorFinal : ConsumidorFinal.cfNao,
            indPres = (PresencaComprador)dadosNfe.PresencaComprador,
            procEmi = ProcessoEmissao.peAplicativoContribuinte,
            verProc = "WebSocketNFCe 1.0"
        };
    }

    private static emit MapearEmitente(Emitente emitente)
    {
        var estado = ObterEstado(emitente.Uf);

        return new emit
        {
            CNPJ = emitente.Cnpj,
            IE = string.IsNullOrWhiteSpace(emitente.InscricaoEstadual) ? string.Empty
                : new string(emitente.InscricaoEstadual.Where(char.IsDigit).ToArray()),
            xNome = emitente.RazaoSocial,
            xFant = emitente.NomeFantasia,
            CRT = (CRT)emitente.Crt,
            enderEmit = new enderEmit
            {
                xLgr = emitente.Logradouro,
                nro = emitente.Numero,
                xCpl = emitente.Complemento,
                xBairro = emitente.Bairro,
                cMun = emitente.CodigoMunicipio,
                xMun = emitente.Municipio,
                UF = estado,
                CEP = emitente.Cep?.Replace("-", "").Replace(".", ""),
                fone = ConverterTelefone(emitente.Telefone),
                cPais = 1058,
                xPais = "BRASIL"
            }
        };
    }

    private static dest MapearDestinatario(Destinatario destinatario)
    {
        var dest = new dest(VersaoServico.Versao400)
        {
            xNome = destinatario.Nome,
            indIEDest = (indIEDest)destinatario.IndicadorIE
        };

        if (!string.IsNullOrWhiteSpace(destinatario.Logradouro) &&
            !string.IsNullOrWhiteSpace(destinatario.Municipio) &&
            destinatario.CodigoMunicipio > 0)
        {
            dest.enderDest = new enderDest
            {
                xLgr = destinatario.Logradouro,
                nro = destinatario.Numero,
                xCpl = destinatario.Complemento,
                xBairro = destinatario.Bairro,
                cMun = destinatario.CodigoMunicipio,
                xMun = destinatario.Municipio,
                UF = destinatario.Uf,
                CEP = destinatario.Cep?.Replace("-", "").Replace(".", ""),
                fone = ConverterTelefone(destinatario.Telefone)
            };
        }

        if (!string.IsNullOrEmpty(destinatario.Cpf))
            dest.CPF = destinatario.Cpf;
        else if (!string.IsNullOrEmpty(destinatario.Cnpj))
            dest.CNPJ = destinatario.Cnpj;

        return dest;
    }

    private static List<det> MapearProdutos(List<ProdutoNFe> produtos, decimal[] distributedDiscounts, TipoAmbiente tpAmb, int crt)
    {
        var detalhes = new List<det>();

        for (int i = 0; i < produtos.Count; i++)
        {
            var prod = produtos[i];
            var item = i + 1;
            var distDesc = distributedDiscounts.Length > i ? distributedDiscounts[i] : 0m;
            var totalDesconto = prod.ValorDesconto + distDesc;

            var produtoNfe = new prod
            {
                cProd = prod.Codigo,
                xProd = item == 1 && tpAmb == TipoAmbiente.Homologacao
                    ? "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"
                    : prod.Descricao,
                NCM = prod.Ncm,
                CFOP = int.Parse(prod.Cfop),
                uCom = prod.Unidade,
                qCom = prod.Quantidade,
                vUnCom = prod.ValorUnitario,
                vProd = prod.ValorTotal,
                uTrib = prod.Unidade,
                qTrib = prod.Quantidade,
                vUnTrib = prod.ValorUnitario,
                indTot = IndicadorTotal.ValorDoItemCompoeTotalNF,
                vDesc = totalDesconto,
                vFrete = prod.ValorFrete,
                vSeg = prod.ValorSeguro,
                vOutro = prod.ValorOutrasDespesas
            };

            produtoNfe.cEAN = !string.IsNullOrWhiteSpace(prod.CodigoBarras) ? prod.CodigoBarras : "SEM GTIN";
            produtoNfe.cEANTrib = !string.IsNullOrWhiteSpace(prod.CodigoBarras) ? prod.CodigoBarras : "SEM GTIN";

            var det = new det
            {
                nItem = item,
                prod = produtoNfe,
                imposto = new imposto
                {
                    ICMS = new ICMS
                    {
                        TipoICMS = MapearIcms(prod, totalDesconto, crt)
                    },
                    PIS = new PIS
                    {
                        TipoPIS = new PISOutr
                        {
                            CST = (CSTPIS)Enum.Parse(typeof(CSTPIS), $"pis{prod.CstPis}"),
                            vBC = prod.ValorTotal - totalDesconto,
                            pPIS = prod.AliquotaPis,
                            vPIS = ((prod.ValorTotal - totalDesconto) * prod.AliquotaPis / 100)
                        }
                    },
                    COFINS = new COFINS
                    {
                        TipoCOFINS = new COFINSOutr
                        {
                            CST = (CSTCOFINS)Enum.Parse(typeof(CSTCOFINS), $"cofins{prod.CstCofins}"),
                            vBC = prod.ValorTotal - totalDesconto,
                            pCOFINS = prod.AliquotaCofins,
                            vCOFINS = ((prod.ValorTotal - totalDesconto) * prod.AliquotaCofins / 100)
                        }
                    },
                    // IBS/CBS: só preenchido se o produto trouxe CstIbsCbs (opt-in).
                    IBSCBS = MapearIbsCbs(prod, prod.ValorTotal - totalDesconto)
                }
            };

            detalhes.Add(det);
        }

        return detalhes;
    }

    // Monta o grupo <IBSCBS> do item (Reforma Tributária). Retorna null quando o
    // produto não trouxe CstIbsCbs — nesse caso a nota sai como antes (sem IBS/CBS).
    // As alíquotas (IBS UF, IBS Municipal, CBS) vêm do payload/configuração.
    private static IbsCbsItem? MapearIbsCbs(ProdutoNFe prod, decimal vBc)
    {
        if (string.IsNullOrWhiteSpace(prod.CstIbsCbs)) return null;

        var vIbsUf = Math.Round(vBc * prod.AliquotaIbsUf / 100m, 2);
        var vIbsMun = Math.Round(vBc * prod.AliquotaIbsMun / 100m, 2);
        var vCbs = Math.Round(vBc * prod.AliquotaCbs / 100m, 2);

        return new IbsCbsItem
        {
            CST = (CstIbsCbsEnum)Enum.Parse(typeof(CstIbsCbsEnum), $"Cst{prod.CstIbsCbs}"),
            cClassTrib = prod.CClassTrib,
            gIBSCBS = new GIbsCbs
            {
                vBC = vBc,
                gIBSUF = new GIbsUf { pIBSUF = prod.AliquotaIbsUf, vIBSUF = vIbsUf },
                gIBSMun = new GIbsMun { pIBSMun = prod.AliquotaIbsMun, vIBSMun = vIbsMun },
                vIBS = vIbsUf + vIbsMun,
                gCBS = new GCbsItem { pCBS = prod.AliquotaCbs, vCBS = vCbs }
            }
        };
    }

    // Totais <IBSCBSTot> — soma os valores dos itens que têm o grupo IBS/CBS.
    // Retorna null se nenhum item tiver (mantém a nota antiga sem o total).
    private static IbsCbsTot? CalcularIbsCbsTot(List<det> detalhes)
    {
        var comIbsCbs = detalhes
            .Where(d => d.imposto?.IBSCBS?.gIBSCBS is not null)
            .Select(d => d.imposto.IBSCBS.gIBSCBS)
            .ToList();
        if (comIbsCbs.Count == 0) return null;

        var vBc = comIbsCbs.Sum(g => g.vBC);
        var vIbsUf = comIbsCbs.Sum(g => g.gIBSUF?.vIBSUF ?? 0m);
        var vIbsMun = comIbsCbs.Sum(g => g.gIBSMun?.vIBSMun ?? 0m);
        var vCbs = comIbsCbs.Sum(g => g.gCBS?.vCBS ?? 0m);

        return new IbsCbsTot
        {
            vBCIBSCBS = vBc,
            gIBS = new GIbsTot
            {
                gIBSUF = new GIbsUfTot { vIBSUF = vIbsUf },
                gIBSMun = new GIbsMunTot { vIBSMun = vIbsMun },
                vIBS = vIbsUf + vIbsMun
            },
            gCBS = new GCbsTot { vCBS = vCbs }
        };
    }

    private static ICMSBasico MapearIcms(ProdutoNFe prod, decimal totalDesconto, int crt)
    {
        var ehSimplesNacional = crt is 1 or 2 or 4;
        var vBC = prod.ValorTotal - totalDesconto;
        var vICMS = vBC * prod.AliquotaIcms / 100;

        if (ehSimplesNacional)
        {
            var csosn = (Csosnicms)Enum.Parse(typeof(Csosnicms), $"Csosn{prod.CstIcms}");
            var vCred = vBC * prod.AliquotaIcms / 100;

            return prod.CstIcms switch
            {
                "101" => new ICMSSN101
                {
                    orig = OrigemMercadoria.OmNacional,
                    CSOSN = csosn,
                    pCredSN = prod.AliquotaIcms,
                    vCredICMSSN = vCred
                },
                "102" or "103" or "300" or "400" => new ICMSSN102
                {
                    orig = OrigemMercadoria.OmNacional,
                    CSOSN = csosn
                },
                "201" => new ICMSSN201
                {
                    orig = OrigemMercadoria.OmNacional,
                    CSOSN = csosn,
                    pCredSN = prod.AliquotaIcms,
                    vCredICMSSN = vCred
                },
                "202" or "203" => new ICMSSN202
                {
                    orig = OrigemMercadoria.OmNacional,
                    CSOSN = csosn
                },
                "500" => new ICMSSN500
                {
                    orig = OrigemMercadoria.OmNacional,
                    CSOSN = csosn
                },
                "900" => new ICMSSN900
                {
                    orig = OrigemMercadoria.OmNacional,
                    CSOSN = csosn,
                    modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                    vBC = vBC,
                    pICMS = prod.AliquotaIcms,
                    vICMS = vICMS
                },
                _ => throw new ArgumentException($"CSOSN inválido para Simples Nacional: {prod.CstIcms}")
            };
        }

        var cst = (Csticms)Enum.Parse(typeof(Csticms), $"Cst{prod.CstIcms}");

        return prod.CstIcms switch
        {
            "00" or "02" => new ICMS00
            {
                orig = OrigemMercadoria.OmNacional,
                CST = cst,
                modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                vBC = vBC,
                pICMS = prod.AliquotaIcms,
                vICMS = vICMS
            },
            "40" or "41" or "50" => new ICMS40
            {
                orig = OrigemMercadoria.OmNacional,
                CST = cst
            },
            _ => throw new ArgumentException($"CST não implementado para Regime Normal: {prod.CstIcms}")
        };
    }

    private static pag MapearPagamento(PedidoNFe pedido, decimal[] distributedDiscounts)
    {
        var dadosNfe = pedido.NFe!;
        var cnpjEmitente = pedido.Emitente?.Cnpj ?? string.Empty;
        var pag = new pag
        {
            detPag = new List<detPag>()
        };

        if (dadosNfe.Parcelas is { Count: > 0 })
        {
            foreach (var parcela in dadosNfe.Parcelas)
            {
                var dp = new detPag
                {
                    tPag = (FormaPagamento)parcela.TipoPagamento,
                    vPag = parcela.Valor
                };
                if (FormaPagamentoRequerCard(parcela.TipoPagamento))
                {
                    dp.card = new card
                    {
                        tpIntegra = TipoIntegracaoPagamento.TipNaoIntegrado,
                        CNPJ = parcela.CnpjCredenciadora ?? dadosNfe.CnpjCredenciadora ?? cnpjEmitente,
                        tBand = ParseBandeira(parcela.BandeiraCartao),
                        cAut = parcela.CodigoAutorizacao
                    };
                }
                pag.detPag.Add(dp);
            }
        }
        else
        {
            var dp = new detPag
            {
                tPag = (FormaPagamento)dadosNfe.TipoPagamento,
                vPag = dadosNfe.ValorPagamento
            };
            if (FormaPagamentoRequerCard(dadosNfe.TipoPagamento))
            {
                dp.card = new card
                {
                    tpIntegra = TipoIntegracaoPagamento.TipNaoIntegrado,
                    CNPJ = dadosNfe.CnpjCredenciadora ?? cnpjEmitente,
                    cAut = dadosNfe.CodigoAutorizacao
                };
            }
            pag.detPag.Add(dp);
        }

        var totalPago = pag.detPag.Sum(p => p.vPag);
        var produtos = pedido.Produtos;
        var vNF = 0m;
        for (int i = 0; i < produtos.Count; i++)
        {
            var prod = produtos[i];
            var distDesc = distributedDiscounts.Length > i ? distributedDiscounts[i] : 0m;
            vNF += (prod.ValorTotal - (prod.ValorDesconto + distDesc) + prod.ValorFrete + prod.ValorSeguro + prod.ValorOutrasDespesas);
        }
        pag.vTroco = totalPago > vNF ? totalPago - vNF : 0m;

        return pag;
    }

    private static ICMSTot CalcularICMSTot(PedidoNFe pedido, decimal[] distributedDiscounts, int crt)
    {
        var produtos = pedido.Produtos;
        var vProd = produtos.Sum(p => p.ValorTotal);
        var vFrete = produtos.Sum(p => p.ValorFrete);
        var vSeg = produtos.Sum(p => p.ValorSeguro);
        
        var vDesc = 0m;
        var vBC = 0m;
        var vICMS = 0m;
        var ehSimplesNacional = crt is 1 or 2 or 4;

        for (int i = 0; i < produtos.Count; i++)
        {
            var prod = produtos[i];
            var distDesc = distributedDiscounts.Length > i ? distributedDiscounts[i] : 0m;
            var totalDescontoItem = prod.ValorDesconto + distDesc;

            vDesc += totalDescontoItem;
            if (!ehSimplesNacional)
            {
                vBC += (prod.ValorTotal - totalDescontoItem);
                vICMS += ((prod.ValorTotal - totalDescontoItem) * prod.AliquotaIcms / 100);
            }
        }

        var vOutro = produtos.Sum(p => p.ValorOutrasDespesas);
        var vNF = vProd - vDesc + vFrete + vSeg + vOutro;

        return new ICMSTot
        {
            vBC = vBC,
            vICMS = vICMS,
            vICMSDeson = 0,
            vFCP = 0,
            vBCST = 0,
            vST = 0,
            vFCPST = 0,
            vFCPSTRet = 0,
            vProd = vProd,
            vFrete = vFrete,
            vSeg = vSeg,
            vDesc = vDesc,
            vII = 0,
            vIPI = 0,
            vIPIDevol = 0,
            vPIS = 0,
            vCOFINS = 0,
            vOutro = vOutro,
            vNF = vNF
        };
    }

    private static long? ConverterTelefone(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            return null;

        var digitos = new string(telefone.Where(char.IsDigit).ToArray());
        if (digitos.Length == 0)
            return null;

        return long.Parse(digitos);
    }

    internal static Estado ObterEstado(string uf)
    {
        var sigla = uf.ToUpperInvariant();
        return sigla switch
        {
            "AC" => Estado.AC,
            "AL" => Estado.AL,
            "AP" => Estado.AP,
            "AM" => Estado.AM,
            "BA" => Estado.BA,
            "CE" => Estado.CE,
            "DF" => Estado.DF,
            "ES" => Estado.ES,
            "GO" => Estado.GO,
            "MA" => Estado.MA,
            "MT" => Estado.MT,
            "MS" => Estado.MS,
            "MG" => Estado.MG,
            "PA" => Estado.PA,
            "PB" => Estado.PB,
            "PR" => Estado.PR,
            "PE" => Estado.PE,
            "PI" => Estado.PI,
            "RJ" => Estado.RJ,
            "RN" => Estado.RN,
            "RS" => Estado.RS,
            "RO" => Estado.RO,
            "RR" => Estado.RR,
            "SC" => Estado.SC,
            "SP" => Estado.SP,
            "SE" => Estado.SE,
            "TO" => Estado.TO,
            _ => throw new ArgumentException($"UF inválida: {uf}")
        };
    }

    private static BandeiraCartao ParseBandeira(string? bandeira)
    {
        if (string.IsNullOrWhiteSpace(bandeira)) return BandeiraCartao.bcOutros;
        return bandeira.ToLowerInvariant() switch
        {
            "visa" => BandeiraCartao.bcVisa,
            "mastercard" or "master" => BandeiraCartao.bcMasterCard,
            "amex" or "americanexpress" => BandeiraCartao.bcAmericanExpress,
            "sorocred" => BandeiraCartao.bcSorocred,
            "diners" or "dinersclub" => BandeiraCartao.bcDinersClub,
            "elo" => BandeiraCartao.Elo,
            "hipercard" => BandeiraCartao.Hipercard,
            "aura" => BandeiraCartao.Aura,
            "cabal" => BandeiraCartao.Cabal,
            "alelo" => BandeiraCartao.Alelo,
            "banescard" => BandeiraCartao.BanesCard,
            "calcard" => BandeiraCartao.CalCard,
            "credz" => BandeiraCartao.Credz,
            "discover" => BandeiraCartao.Discover,
            "goodcard" => BandeiraCartao.GoodCard,
            "greencard" => BandeiraCartao.GreenCard,
            "hiper" => BandeiraCartao.Hiper,
            "jcb" => BandeiraCartao.JcB,
            "mais" => BandeiraCartao.Mais,
            "maxvan" => BandeiraCartao.MaxVan,
            "policard" => BandeiraCartao.Policard,
            "redecompras" => BandeiraCartao.RedeCompras,
            "sodexo" => BandeiraCartao.Sodexo,
            "valecard" => BandeiraCartao.ValeCard,
            "verocheque" => BandeiraCartao.Verocheque,
            "vr" => BandeiraCartao.VR,
            "ticket" => BandeiraCartao.Ticket,
            _ => BandeiraCartao.bcOutros
        };
    }

    private static bool FormaPagamentoRequerCard(int tPag)
    {
        // tPag que exigem card pela SEFAZ:
        // 3=Cartão Crédito, 4=Cartão Débito, 5=Cartão da Loja
        // 17=PIX Dinâmico, 18=TED, 19=Fidelidade, 20=PIX Estático
        // 21=Crédito em Loja, 22=PagEletrônico, 23=PIX Automático, 24=TEF
        return tPag is 3 or 4 or 5 or >= 17 and <= 24;
    }
}
