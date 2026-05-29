# WebSocketNFCeService

## Arquitetura
- `WebSocketNFCeService` — Console app que roda como Windows Service (`AZFoodWebSocketNFCe`) ou via `dotnet run`
- `WebSocketNFCeService.Domain` — Modelos (`PedidoNFe`, `DadosNFe`, `Parcela`, etc.)
- `WebSocketNFCeService.NFe` — Mapeamento, emissão NFe/Zeus, PDF, envio nuvem
- `WebSocketNFCeService.Infra` — Persistência de config

## Fluxo de Emissão NFCe
1. Frontend envia JSON via WebSocket com `tipo: "nfce"`
2. `NFeWebSocketHandler.ProcessarNfceAsync` deserializa, injeta gorjeta como produto, valida
3. `NFeService.EmitirAsync` mapeia `PedidoNFe` → NFe (Zeus), assina, envia SEFAZ
4. XML autorizado salvo como procNFe (NFe + protocolo) em `Xmls/YYYYMM/{chave}-nfe.xml`
5. PDF DANFE salvo em `Pdfs/YYYYMM/{chave}-nfe.pdf`
6. XML enviado para Cloud API (`https://xml.dm7sistemas.com.br/api/functions/receiveXml`)

## FormaPagamento (Zeus 2026.5.20.1650)
| Código | Enum | Descrição | Requer card |
|--------|------|-----------|:-----------:|
| 1 | `fpDinheiro` | Dinheiro | não |
| 2 | `fpCheque` | Cheque | não |
| 3 | `fpCartaoCredito` | Cartão Crédito | **sim** |
| 4 | `fpCartaoDebito` | Cartão Débito | **sim** |
| 5 | `fpCartaoDaLoja` | Cartão da Loja | **sim** |
| 10 | `fpValeAlimentacao` | Vale Alimentação | não |
| 11 | `fpValeRefeicao` | Vale Refeição | não |
| 12 | `fpValePresente` | Vale Presente | não |
| 13 | `fpValeCombustivel` | Vale Combustível | não |
| 14 | `fpDuplicataMercantil` | Duplicata Mercantil | não |
| 15 | `fpBoletoBancario` | Boleto Bancário | não |
| 16 | `fpDepositoBancario` | Depósito Bancário | não |
| 17 | `fpPagamentoInstantaneoPIXDinamico` | PIX Dinâmico | **sim** |
| 18 | `fpTed` | TED | **sim** |
| 19 | `fpProgramadefidelidade` | Programa de Fidelidade | **sim** |
| 20 | `fpPagamentoInstantaneoPIXEstatico` | PIX Estático | **sim** |
| 21 | `fpCreditoEmLoja` | Crédito em Loja | **sim** |
| 22 | `fpPagamentoEletronicoNaoInformado` | Pag. Eletrônico | **sim** |
| 23 | `fpPagamentoInstantaneoPixAutomatico` | PIX Automático | **sim** |
| 24 | `fpPagamentoTefBookTransfer` | TEF Book Transfer | **sim** |
| 90 | `fpSemPagamento` | Sem Pagamento | não |
| 91 | `fpPagamentoPosterior` | Pagamento Posterior | não |
| 99 | `fpOutro` | Outros | não |

**Card data**: Quando `FormaPagamentoRequerCard` retorna true, o sistema gera automaticamente `<card>` com:
- `tpIntegra=2` (não integrado)
- `CNPJ` = `parcela.cnpjCredenciadora` → `nfe.cnpjCredenciadora` → CNPJ do emitente
- `tBand` = `parcela.bandeiraCartao` → "Outros" (99)
- `cAut` = `parcela.codigoAutorizacao`

## Payload NFCe
```json
{
  "tipo": "nfce",
  "id": "order-xxx",
  "ambiente": "producao",
  "emitente": {
    "cnpj": "53912651000114",
    "razaoSocial": "JML DE SIQUEIRA",
    "nomeFantasia": "ICE HOUSE",
    "inscricaoEstadual": "182347678112",
    "logradouro": "DAS GARDENIAS",
    "numero": "275",
    "bairro": "JARDIM CANDIDA",
    "codigoMunicipio": 3503307,
    "municipio": "ARARAS",
    "uf": "SP",
    "cep": "13603004",
    "crt": 1
  },
  "destinatario": {
    "cpf": "",
    "nome": "Cliente #54425",
    "uf": "SP"
  },
  "produtos": [
    {
      "codigo": "6a00d1b778c7d45e856a72b0",
      "descricao": "Coca Garrafinha",
      "ncm": "21069090",
      "cfop": "5102",
      "unidade": "UN",
      "quantidade": 1,
      "valorUnitario": 0.08,
      "valorTotal": 0.08,
      "cstIcms": "102",
      "aliquotaIcms": 0,
      "cstPis": "99",
      "cstCofins": "99"
    }
  ],
  "nfe": {
    "numero": 11,
    "serie": 1,
    "modelo": 65,
    "naturezaOperacao": "VENDA",
    "finalidade": 1,
    "consumidorFinal": 1,
    "presencaComprador": 1,
    "valorGorjeta": 0.008,
    "parcelas": [
      { "tipoPagamento": 3, "valor": 0.15 },
      { "tipoPagamento": 17, "valor": 0.06 }
    ]
  }
}
```

## Gorjeta
- Campo separado `nfe.valorGorjeta` — não deve ser item em `produtos`
- Backend injeta automaticamente produto "GORJETA CONCEDIDA" (código 99999)
- CSOSN 400, PIS 99, COFINS 49
- O valor da gorjeta entra no `vNF`, e o `vTroco` é calculado como `soma(pagamentos) - vNF`

## Troco
- Calculado automaticamente em `MapearPagamento`: `vTroco = totalPago > vNF ? totalPago - vNF : 0`
- Exibido no PDF apenas se `vTroco > 0`

## PDF DANFE (QuestPDF)
- Layout 80mm contínuo
- Ordem: emitente → CNPJ → tipo doc → nº/série/data → destinatário → chave → QR Code → produtos → total → pagamentos → troco → protocolo
- QR Code gerado como imagem (QRCoder BitmapByteQRCode)

## IE
- Limpa caracteres não numéricos antes de enviar à SEFAZ

## Destinatário
- CPF/CNPJ não obrigatório. Quando vazio, NFe emitida para "CONSUMIDOR NÃO IDENTIFICADO"

## Config
- `appsettings.json` contém `NFeConfig` (CloudApi, Ambiente, Certificado)
- Config pode ser atualizada via WebSocket com `tipo: "setup"`

## Rebuild
- Sempre matar processo antigo antes de rebuild:
  ```powershell
  Get-Process -Name AZFoodWebSocketNFCe | Stop-Process -Force
  dotnet build
  ```
