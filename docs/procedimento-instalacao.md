# Procedimento de Instalação — WebSocket NFCe Service

## Pré-requisitos

- Windows 10/11 ou Windows Server 2019+
- .NET 8.0 Runtime (ou SDK)
- Certificado digital A1 (arquivo .pfx) válido para emissão de NFCe
- Cadastro do CNPJ habilitado na SEFAZ para NFCe (homologação e produção)

## 1. Publicar o Serviço

```powershell
cd C:\DM7\WebSocketNFCeService
dotnet publish src\WebSocketNFCeService -c Release -o C:\AZFoodNFCeService
```

## 2. Estrutura após publicar

```
C:\AZFoodNFCeService\
├── AZFoodWebSocketNFCe.exe
├── appsettings.json
├── Schemas\          (XSDs de validação — copiar manualmente)
└── Xmls\             (criado automaticamente)
```

### Schema XSD

Copiar a pasta `Schemas\` do projeto para o diretório publicado:
```powershell
Copy-Item -Recurse src\WebSocketNFCeService.NFe\Schemas C:\AZFoodNFCeService\Schemas
```

## 3. Configurar appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "WebSocketConfig": {
    "Porta": 5000,
    "Path": "/ws/nfce"
  }
}
```

| Parâmetro | Descrição | Padrão |
|-----------|-----------|--------|
| `Porta` | Porta HTTP do serviço | 5000 |
| `Path` | Caminho do WebSocket | `/ws/nfce` |

> Dados de certificado e CSC **não** ficam no `appsettings.json`.
> São enviados via WebSocket (`tipo: "setup"`) pela aplicação cloud.

## 4. Instalar como Serviço Windows (opcional)

```powershell
sc create "AZFoodWebSocketNFCe" binPath="C:\AZFoodNFCeService\AZFoodWebSocketNFCe.exe" start=auto
sc start "AZFoodWebSocketNFCe"
```

## 5. Verificar se está rodando

```powershell
curl http://localhost:5000/health
```

Resposta esperada:
```json
{"status":"running","service":"AZFoodWebSocketNFCe","wsEndpoint":"ws://localhost:5000/ws/nfce"}
```

## 6. Fluxo de Uso (Cloud → Serviço)

> **Importante:** `destinatario` é **opcional**. Para consumidor não identificado, basta omitir o campo.

### 6.1 Setup (uma vez ou quando trocar cliente/certificado)

Enviar via WebSocket:
```json
{
  "tipo": "setup",
  "certificado": {
    "arquivo": "C:\\caminho\\certificado.pfx",
    "senha": "senha123"
  },
  "csc": {
    "idToken": "000001",
    "token": "8e506b78-..."
  },
  "timeout": 60000
}
```

Resposta:
```json
{"tipo":"setup","status":"ok","mensagem":"Config salva com sucesso"}
```

> A configuração fica salva em `config.json` ao lado do executável.
> Sobrevive a restart. Só precisa reenviar se alterar os dados.

### 6.2 Emitir NFCe

```json
{
  "tipo": "nfce",
  "ambiente": "homologacao",
  "emitente": {
    "cnpj": "17336311000129",
    "inscricaoEstadual": "708079206118",
    "razaoSocial": "PASTEIS CANTINHO NOBRE LTDA",
    "nomeFantasia": "PASTEIS CANTINHO NOBRE LTDA",
    "logradouro": "Rua Teste",
    "numero": "100",
    "bairro": "Centro",
    "codigoMunicipio": 3550308,
    "municipio": "São Paulo",
    "uf": "SP",
    "cep": "01001000",
    "crt": 1
  },
  "destinatario": {
    "cpf": "12345678909",
    "nome": "Consumidor Teste",
    "uf": "SP"
  },
  "produtos": [
    {
      "codigo": "001",
      "descricao": "Produto Teste",
      "ncm": "84713019",
      "cfop": "5102",
      "unidade": "UN",
      "quantidade": 1,
      "valorUnitario": 100.00,
      "valorTotal": 100.00,
      "cstIcms": "102",
      "aliquotaIcms": 18.0,
      "cstPis": "99",
      "cstCofins": "99"
    }
  ],
  "nfe": {
    "numero": 1,
    "serie": 1,
    "naturezaOperacao": "VENDA",
    "finalidade": 1,
    "consumidorFinal": 1,
    "presencaComprador": 1,
    "tipoPagamento": 1,
    "valorPagamento": 100.00
  }
}
```

### 6.3 Resposta da Emissão

Quando a NFCe é autorizada, o WebSocket retorna:

```json
{
  "id": "teste001",
  "sucesso": true,
  "codigoStatus": 100,
  "motivo": "Autorizado o uso da NF-e",
  "chaveAcesso": "35260512345678000199550010000000011000000001",
  "numeroProtocolo": "123456789012345",
  "numeroNFe": 1,
  "serie": 1,
  "xmlAutorizado": "<?xml version=\"1.0\" encoding=\"UTF-8\"?>... <NFe>... </NFe>"
}
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `id` | string | Mesmo ID enviado na requisição |
| `sucesso` | bool | `true` se autorizada |
| `codigoStatus` | int | Código de retorno SEFAZ |
| `motivo` | string | Motivo (autorização ou rejeição) |
| `chaveAcesso` | string | Chave de 44 dígitos |
| `numeroProtocolo` | string | Número do protocolo SEFAZ |
| `numeroNFe` | int | Número da NF-e |
| `serie` | int | Série |
| `xmlAutorizado` | string | XML completo da NF-e autorizada (com protocolo) |
| `erros` | array | Lista de erros (vazio se sucesso) |

Em caso de rejeição:

```json
{
  "id": "teste001",
  "sucesso": false,
  "codigoStatus": 531,
  "motivo": "Diferença entre BC do ICMS e a soma dos itens",
  "chaveAcesso": null,
  "erros": [
    { "codigo": 531, "mensagem": "Diferença entre BC do ICMS e a soma dos itens" }
  ]
}
```

> O XML autorizado também é **enviado automaticamente** para a nuvem via API configurada em `appsettings.json:NFeConfig:CloudApi`.

## 7. Observações Importantes

### Ambiente (`homologacao` vs `producao`)
- Definido por campo no payload (`ambiente`)
- Se alterar, **não precisa reenviar setup**

### Regime Tributário (`crt` no emitente)

| CRT | Regime | Campo `cstIcms` |
|-----|--------|----------------|
| 1, 2, 4 | Simples Nacional | CSOSN (101, 102, 103, 201, 202, 203, 300, 400, 500, 900) |
| 3 | Regime Normal | CST (00, 02, 40, 41, 50) |

### CST/CSOSN comuns para NFCe

| Regime | Código | Descrição |
|--------|--------|-----------|
| SN | 102 | Tributada pelo SN sem permissão de crédito (mais comum) |
| SN | 400 | Não tributada pelo SN |
| SN | 900 | Outros (com BC e alíquota) |
| Normal | 00 | Tributada integralmente |
| Normal | 40 | Isenta |

### Portas
- Serviço escuta em `http://127.0.0.1:{porta}` — **somente loopback**
- **Não** libere a porta 5000 no firewall, e não a exponha na rede

Até 2026-07 o serviço subia em `0.0.0.0`, ou seja, atendia em todas as
interfaces da máquina. Qualquer aparelho na rede do salão abria
`http://<ip-da-maquina>:5000/ws/nfce` e emitia NFC-e no CNPJ do restaurante,
sem credencial nenhuma — sem passar pelo `electron-print` nem pela Cloudflare.

Quem consome este serviço é o `electron-print`, que roda na **mesma máquina** e
conecta em `ws://127.0.0.1:5000/ws/nfce`. Nada legítimo vem de fora. Todo acesso
externo entra pelo túnel, no `electron-print`, que exige token.

### Logs
- Console + Windows Event Log (fonte: `AZFoodWebSocketNFCe`)
- Nível: Information (pode alterar em `appsettings.json:Logging`)

### XMLs
- Salvos em `Xmls/YYYYMM/` (ex: `Xmls/202605/`) no diretório base
- `{lote}-env-lot.xml` — enviado à SEFAZ
- `{lote}-rec.xml` — resposta da SEFAZ
- `{chave}-nfe.xml` — NFCe autorizada (se aprovada)
- A pasta é criada automaticamente se não existir

## 8. Solução de Problemas

| Erro | Causa | Solução |
|------|-------|---------|
| 245 — CNPJ não cadastrado | CNPJ não habilitado na UF | Cadastrar na SEFAZ |
| 383 — CSOSN indevido | CSOSN inválido para NFCe | Usar CSOSN 102 (SN) |
| 531 — Diferença BC ICMS | vBC no total sem vBC nos itens | Verificar CRT x CST/CSOSN |
| 590 — CST para SN | CRT=3 com CSOSN ou CRT=1 com CST | Alinhar cstIcms com crt |
| 883 — GTIN sem informação | cEAN vazio | Enviar "SEM GTIN" |
| 598 — erro no nome do destinatário | Homologação sem nome padrão | O serviço corrige automaticamente |


Publicar : 
dotnet publish src\WebSocketNFCeService -c Release -o C:\AZFoodNFCeService