using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using DFe.Classes.Flags;
using NFe.Classes;
using WebSocketNFCeService.NFe.Services;
using System.Linq;
using System.Collections.Generic;
using WebSocketNFCeService.Domain.Models;
using WebSocketNFCeService.NFe.Mappers;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "mapper") { ChecarGruposDaNFe(); return; }

        var dir = @"C:\DM7\WebSocketNFCeService\PdfTester";
        var xmlFiles = Directory.GetFiles(dir, "*.xml");
        
        var pdfService = new NFePdfService(NullLogger<NFePdfService>.Instance);
        
        foreach (var xmlPath in xmlFiles)
        {
            try 
            {
                var xmlString = File.ReadAllText(xmlPath).Replace("<procNFe", "<nfeProc").Replace("</procNFe>", "</nfeProc>");
                var proc = DFe.Utils.FuncoesXml.XmlStringParaClasse<nfeProc>(xmlString);
                var nfe = proc.NFe;
                
                var chave = proc.protNFe?.infProt?.chNFe ?? nfe.infNFe.Id?.Replace("NFe", "");
                if (string.IsNullOrEmpty(chave)) chave = Path.GetFileNameWithoutExtension(xmlPath).Replace(" (1)", "");
                
                pdfService.Gerar(chave, nfe, proc.protNFe?.infProt?.nProt, dir);
                
                Console.WriteLine("PDF gerado em: " + Path.Combine(dir, $"{chave}-nfe.pdf"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao processar {xmlPath}: {ex.Message}");
            }
        }
    }

    // CHECAGEM DOS GRUPOS SO-NF-e: transp, cobr, infAdic e rastro.
    //
    // Os dois casos que importam:
    //   1. pedido SEM os grupos -> tem que sair igual a antes (transp = sem
    //      frete, cobr e infAdic nulos, rastro nulo). E a garantia de que a
    //      NFC-e de quem ja usa nao muda.
    //   2. pedido COM os grupos -> tem que chegar no XML.
    //
    // Rodar: dotnet run --project PdfTester -- mapper
    static void ChecarGruposDaNFe()
    {
        var falhas = new List<string>();
        void Exigir(bool ok, string oque) { if (!ok) falhas.Add(oque); }

        PedidoNFe Base() => new PedidoNFe
        {
            Ambiente = "homologacao",
            Emitente = new Emitente
            {
                Cnpj = "00000000000191", RazaoSocial = "EMITENTE TESTE", Uf = "SP",
                CodigoMunicipio = 3550308, Municipio = "SAO PAULO", Crt = 3,
                Logradouro = "RUA X", Numero = "1", Bairro = "CENTRO", Cep = "01001000"
            },
            NFe = new DadosNFe { Numero = 1, Serie = 1, Modelo = 55, ValorPagamento = 100m },
            Destinatario = new Destinatario
            {
                Cnpj = "00000000000272", Nome = "DESTINATARIO TESTE", Uf = "SP",
                Logradouro = "RUA Y", Numero = "2", Bairro = "CENTRO",
                Municipio = "SAO PAULO", CodigoMunicipio = 3550308, Cep = "01001000"
            },
            Produtos = new List<ProdutoNFe>
            {
                new ProdutoNFe { Codigo = "P1", Descricao = "PRODUTO", Ncm = "21069090",
                    Cfop = "5102", Unidade = "UN", Quantidade = 2, ValorUnitario = 50m, ValorTotal = 100m }
            }
        };

        // 1) SEM os grupos -- o comportamento antigo, intacto
        var semGrupos = PedidoToNFeMapper.Mapear(Base());
        Exigir(semGrupos.infNFe.transp != null &&
               semGrupos.infNFe.transp.modFrete == NFe.Classes.Informacoes.Transporte.ModalidadeFrete.mfSemFrete,
               "sem grupos: transp deveria continuar 'sem frete'");
        Exigir(semGrupos.infNFe.cobr == null, "sem grupos: cobr deveria ser nulo");
        Exigir(semGrupos.infNFe.infAdic == null, "sem grupos: infAdic deveria ser nulo");
        Exigir(semGrupos.infNFe.det[0].prod.rastro == null, "sem grupos: rastro deveria ser nulo");

        // 2) COM os grupos
        var p = Base();
        p.Transporte = new Transporte
        {
            ModalidadeFrete = 1,
            Transportadora = new Transportadora { Cnpj = "00000000000353", Nome = "TRANSP TESTE", Uf = "SP" },
            Placa = "abc-1d23", UfPlaca = "SP",
            Volumes = new List<Volume> { new Volume { Quantidade = 3, Especie = "CAIXA", PesoBruto = 12.5m } }
        };
        p.Cobranca = new Cobranca
        {
            Duplicatas = new List<Duplicata>
            {
                new Duplicata { Vencimento = "2026-09-15", Valor = 60m },
                new Duplicata { Vencimento = "2026-10-15", Valor = 40m }
            }
        };
        p.InformacoesAdicionais = new InformacoesAdicionais { Complementar = "PEDIDO 12345" };
        p.Produtos[0].Lotes = new List<LoteProduto>
        {
            new LoteProduto { Numero = "L342", Quantidade = 2, Fabricacao = "2026-08-01", Validade = "2027-02-01" }
        };

        var nfe = PedidoToNFeMapper.Mapear(p);
        var t = nfe.infNFe.transp;
        Exigir(t.modFrete == NFe.Classes.Informacoes.Transporte.ModalidadeFrete.mfContaDestinatario, "transp: modalidade nao chegou");
        Exigir(t.transporta?.xNome == "TRANSP TESTE", "transp: transportadora nao chegou");
        Exigir(t.transporta?.CPF == null, "transp: CPF deveria ficar nulo quando ha CNPJ");
        Exigir(t.veicTransp?.placa == "ABC1D23", "transp: placa deveria sair sem hifen e em maiuscula");
        Exigir(t.vol?.Count == 1 && t.vol[0].qVol == 3, "transp: volume nao chegou");

        var c = nfe.infNFe.cobr;
        Exigir(c?.dup?.Count == 2, "cobr: duplicatas nao chegaram");
        Exigir(c?.dup?[0].nDup == "001" && c?.dup?[1].nDup == "002", "cobr: numeracao automatica das duplicatas");
        // A fatura tem que ser DERIVADA da soma -- a SEFAZ confere isso.
        Exigir(c?.fat?.vLiq == 100m, "cobr: valor liquido deveria vir da soma das duplicatas");
        Exigir(c?.dup?[0].dVenc == new DateTime(2026, 9, 15), "cobr: vencimento nao foi convertido");

        Exigir(nfe.infNFe.infAdic?.infCpl == "PEDIDO 12345", "infAdic: complementar nao chegou");

        var r = nfe.infNFe.det[0].prod.rastro;
        Exigir(r?.Count == 1, "rastro: lote nao chegou");
        Exigir(r?[0].nLote == "L342" && r?[0].qLote == 2, "rastro: numero/quantidade do lote");
        Exigir(r?[0].dFab == new DateTime(2026, 8, 1) && r?[0].dVal == new DateTime(2027, 2, 1),
               "rastro: fabricacao/validade nao foram convertidas");

        if (falhas.Count == 0) Console.WriteLine("OK - transp, cobr, infAdic e rastro conferidos (com e sem os grupos).");
        else { foreach (var f in falhas) Console.WriteLine("FALHOU: " + f); Environment.Exit(1); }
    }
}