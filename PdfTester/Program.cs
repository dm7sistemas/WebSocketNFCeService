using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using DFe.Classes.Flags;
using NFe.Classes;
using WebSocketNFCeService.NFe.Services;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
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
}
