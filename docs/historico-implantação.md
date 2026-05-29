# Histórico de Implementação — WebSocket NFCe Service

## 1. Correções no Mapeamento (PedidoToNFeMapper)

### NullReferenceException em MapearPagamento
- `detPag` não era inicializado → adicionado `new List<detPag>()` no objeto `pag`

### Decimal.Parse(null) em versao
- `versao` do `infNFe` não era setada → adicionado `versao = "4.00"`

### Endereço do destinatário (NFCe)
- `enderDest` só é criado quando Logradouro, Municipio e CodigoMunicipio > 0 estão preenchidos

### xNome do destinatário em homologação
- Automático: se `tpAmb == Homologacao`, substitui `xNome` por "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"

### xProd do primeiro item em homologação
- Se `tpAmb == Homologacao` e `nItem == 1`, substitui `xProd` por "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"

### GTIN / cEAN
- `cEAN` e `cEANTrib` = `"SEM GTIN"` quando `CodigoBarras` é nulo/vazio

### ICMS por Regime Tributário (CRT)
- `MapearIcms`: escolhe a classe ICMS conforme CRT
  - **CRT 1, 2, 4** (Simples Nacional): usa classes ICMSSN101, ICMSSN102, ICMSSN201, ICMSSN202, ICMSSN500, ICMSSN900 conforme CSOSN
  - **CRT 3** (Regime Normal): usa ICMS00 (CST 00/02) ou ICMS40 (CST 40/41/50)

### Total ICMS (ICMSTot)
- Para Simples Nacional (CRT 1, 2, 4): `vBC = 0` e `vICMS = 0` no total

---

## 2. Serviço de Emissão (NFeService)

### Tratamento do retorno da SEFAZ
- Lê `cStat` do `protNFe.infProt` individual, não do `cStat` do lote (104)
- Logging detalhado de rejeições

### Esquemas XSD
- Baixados para `{BaseDirectory}/Schemas/`
- Configurado `DiretorioSchemas` no `ConfiguracaoServico`

### Pasta Xmls
- Criada automaticamente se não existir

### QR Code
- `urlChave` e `qrCode` atribuídos corretamente via `infNFeSupl`

---

## 3. Configuração via WebSocket (Setup)

### Arquitetura
| Fonte | Prioridade | Descrição |
|-------|-----------|-----------|
| `config.json` | 1ª (maior) | Criado/atualizado via WebSocket (`tipo: "setup"`) |
| `appsettings.json:NFeConfig` | 2ª (fallback) | Backup manual |

### Mensagem Setup
```json
{
  "tipo": "setup",
  "certificado": { "arquivo": "...", "senha": "..." },
  "csc": { "idToken": "000001", "token": "..." },
  "timeout": 60000
}
```
- Salva em `config.json` no diretório base do executável
- Sobrevive a restart
- Resposta: `{ "tipo": "setup", "status": "ok" }`

### Mensagem NFCe
```json
{
  "tipo": "nfce",
  "ambiente": "homologacao",
  "emitente": { "cnpj": "...", "crt": 1, ... },
  "destinatario": { ... },
  "produtos": [ ... ],
  "nfe": { ... }
}
```

---

## 5. Destinatário Opcional

- `destinatario` não é mais obrigatório no JSON. Se omitido, a NFCe é emitida sem destinatário (consumidor não identificado)
- Adicionada validação: se informado, exige CPF ou CNPJ

## 6. Separação de XMLs por Mês

- `Xmls/` agora organiza em subpastas `YYYYMM` (ex: `Xmls/202605/`) para facilitar localização

## 7. Envio do XML para Nuvem

- Quando a NFCe é autorizada, o XML é enviado automaticamente via POST para a URL configurada em `appsettings.json:NFeConfig:CloudApi`
- Header `X-API-Key` é adicionado automaticamente
- Configuração em `appsettings.Development.json`:

```json
"CloudApi": {
  "Url": "https://xml.dm7sistemas.com.br/api/functions/receiveXml",
  "ApiKey": "59d511d536715be2fdd5693cf57b7a29"
}
```

## 8. Relação de Arquivos Alterados/Criados

| Arquivo | Ação |
|---------|------|
| `src/WebSocketNFCeService.NFe/Mappers/PedidoToNFeMapper.cs` | Alterado |
| `src/WebSocketNFCeService.NFe/Services/NFeService.cs` | Alterado |
| `src/WebSocketNFCeService/NFeWebSocketHandler.cs` | Alterado |
| `src/WebSocketNFCeService/Program.cs` | Alterado |
| `src/WebSocketNFCeService/appsettings.json` | Alterado |
| `src/WebSocketNFCeService/appsettings.Development.json` | Alterado |
| `src/WebSocketNFCeService.Domain/Models/MensagemWebSocket.cs` | **Criado** |
| `src/WebSocketNFCeService.Infra/Configuration/NFeConfig.cs` | Alterado |
| `src/WebSocketNFCeService.Infra/Services/ConfigPersistenceService.cs` | **Criado** |
| `src/WebSocketNFCeService.NFe/Schemas/` | **Criado** (XSDs) |
