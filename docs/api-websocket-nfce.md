# API WebSocket — Emissão de NFCe

## Conexão

```
ws://<host>:5000/ws/nfce
```

| Parâmetro | Padrão | Config |
|-----------|--------|--------|
| Porta | 5000 | `appsettings.json:WebSocketConfig:Porta` |
| Path | `/ws/nfce` | `appsettings.json:WebSocketConfig:Path` |

Health check: `GET http://localhost:5000/health`

---

## Mensagens

Toda mensagem enviada ao WebSocket deve ser um JSON com campo `tipo`.

### `tipo: "setup"` — Configurar certificado e CSC

Enviar uma vez (ou quando alterar cliente/certificado). Persistido em `config.json`.

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

| Campo | Tipo | Obrigatório | Descrição |
|-------|------|:-----------:|-----------|
| `tipo` | string | sim | `"setup"` |
| `certificado.arquivo` | string | não* | Caminho do .pfx/.p12 |
| `certificado.senha` | string | não* | Senha do certificado |
| `certificado.serial` | string | não* | Serial alternativo (busca na store) |
| `csc.idToken` | string | não | Token ID do CSC (SEFAZ) |
| `csc.token` | string | não | Token do CSC (SEFAZ) |
| `timeout` | int | não | Timeout SEFAZ em ms (padrão 60000) |

*\* Pelo menos um de `arquivo` ou `serial` deve ser informado.*

Resposta:
```json
{ "tipo": "setup", "status": "ok", "mensagem": "Config salva com sucesso" }
```

---

### `tipo: "nfce"` — Emitir NFCe

#### Payload completo

```json
{
  "tipo": "nfce",
  "id": "order-456",
  "ambiente": "producao",
  "emitente": {
    "cnpj": "11222333000181",
    "razaoSocial": "EMPRESA FICTICIA LTDA",
    "nomeFantasia": "LOJA MODELO",
    "inscricaoEstadual": "123456789000",
    "inscricaoMunicipal": "",
    "logradouro": "RUA DAS FLORES",
    "numero": "100",
    "complemento": "",
    "bairro": "CENTRO",
    "codigoMunicipio": 3550308,
    "municipio": "SAO PAULO",
    "uf": "SP",
    "cep": "01001000",
    "telefone": "",
    "crt": 1
  },
  "destinatario": {
    "cpf": "",
    "cnpj": "",
    "nome": "Cliente #54425",
    "logradouro": "",
    "numero": "",
    "bairro": "",
    "codigoMunicipio": 0,
    "municipio": "",
    "uf": "SP",
    "cep": "",
    "telefone": "",
    "indicadorContribuinte": 9,
    "indicadorIE": 9
  },
  "produtos": [
    {
      "codigo": "001",
      "codigoBarras": "",
      "descricao": "Coca Garrafinha",
      "ncm": "21069090",
      "cfop": "5102",
      "unidade": "UN",
      "quantidade": 2,
      "valorUnitario": 4.00,
      "valorTotal": 8.00,
      "valorDesconto": 1.00,
      "valorFrete": 0,
      "valorSeguro": 0,
      "valorOutrasDespesas": 0,
      "cstIcms": "102",
      "aliquotaIcms": 0,
      "cstPis": "99",
      "aliquotaPis": 0,
      "cstCofins": "99",
      "aliquotaCofins": 0
    }
  ],
  "nfe": {
    "numero": 12,
    "serie": 1,
    "modelo": 65,
    "naturezaOperacao": "VENDA",
    "finalidade": 1,
    "consumidorFinal": 1,
    "presencaComprador": 1,
    "valorGorjeta": 0,
    "parcelas": [
      {
        "tipoPagamento": 3,
        "formaPagamento": "dinheiro",
        "valor": 13.50,
        "cnpjCredenciadora": "",
        "bandeiraCartao": "",
        "codigoAutorizacao": "",
        "cnpjRecebedor": "",
        "terminalPagamento": ""
      }
    ],
    "cnpjCredenciadora": "",
    "codigoAutorizacao": ""
  }
}
```

#### Campos do topo

| Campo | Tipo | Obrigatório | Padrão | Descrição |
|-------|------|:-----------:|--------|-----------|
| `tipo` | string | sim | — | `"nfce"` |
| `id` | string | não | `""` | ID livre do cliente (ecoado na resposta) |
| `ambiente` | string | não | `"homologacao"` | `"producao"` ou `"homologacao"` |
| `emitente` | object | **sim** | — | Dados do emitente |
| `destinatario` | object | não | — | Quando vazio: consumidor não identificado |
| `produtos` | array | **sim** | `[]` | Mínimo 1 produto |
| `nfe` | object | **sim** | — | Dados da NFCe |

#### `emitente`

| Campo | Tipo | Obrigatório | Descrição |
|-------|------|:-----------:|-----------|
| `cnpj` | string | **sim** | CNPJ (14 dígitos) |
| `razaoSocial` | string | **sim** | Razão social |
| `nomeFantasia` | string | não | Nome fantasia |
| `inscricaoEstadual` | string | não | IE (caracteres não numéricos são removidos) |
| `inscricaoMunicipal` | string | não | IM |
| `logradouro` | string | não | Logradouro |
| `numero` | string | não | Número |
| `complemento` | string | não | Complemento |
| `bairro` | string | não | Bairro |
| `codigoMunicipio` | int | não | Código IBGE do município |
| `municipio` | string | não | Nome do município |
| `uf` | string | **sim** | UF (ex: `"SP"`) |
| `cep` | string | não | CEP |
| `telefone` | string | não | Telefone |
| `crt` | int | não | 1=SN, 2=SN excesso, 3=Normal, 4=MEI |

#### `destinatario`

Quando CPF e CNPJ estiverem vazios, a NFCe é emitida para "CONSUMIDOR NÃO IDENTIFICADO".

| Campo | Tipo | Obrigatório | Padrão | Descrição |
|-------|------|:-----------:|--------|-----------|
| `cpf` | string | não | `null` | CPF |
| `cnpj` | string | não | `null` | CNPJ |
| `nome` | string | não | `""` | Nome |
| `logradouro` | string | não | `""` | Logradouro |
| `numero` | string | não | `""` | Número |
| `complemento` | string | não | `null` | Complemento |
| `bairro` | string | não | `""` | Bairro |
| `codigoMunicipio` | int | não | 0 | Código IBGE |
| `municipio` | string | não | `""` | Município |
| `uf` | string | não | `""` | UF |
| `cep` | string | não | `null` | CEP |
| `telefone` | string | não | `null` | Telefone |
| `indicadorContribuinte` | int | não | 9 | 9=Não contribuinte |
| `indicadorIE` | int | não | 9 | 9=Não contribuinte |

#### `produtos[]`

| Campo | Tipo | Obrigatório | Padrão | Descrição |
|-------|------|:-----------:|--------|-----------|
| `codigo` | string | **sim** | — | Código do produto |
| `codigoBarras` | string | não | `null` | GTIN (se vazio → `"SEM GTIN"`) |
| `descricao` | string | **sim** | — | Descrição |
| `ncm` | string | não | `""` | NCM |
| `cfop` | string | não | `""` | CFOP (convertido para int) |
| `unidade` | string | não | `"UN"` | Unidade |
| `quantidade` | decimal | **sim** | — | Quantidade (> 0) |
| `valorUnitario` | decimal | **sim** | — | Valor unitário (> 0) |
| `valorTotal` | decimal | não | 0 | Total (qtd × unit) |
| `valorDesconto` | decimal | não | 0 | **Desconto item a item** |
| `valorFrete` | decimal | não | 0 | Frete |
| `valorSeguro` | decimal | não | 0 | Seguro |
| `valorOutrasDespesas` | decimal | não | 0 | Outras despesas |
| `cstIcms` | string | não | `"00"` | CST/CSOSN (ver tabela abaixo) |
| `aliquotaIcms` | decimal | não | 0 | Alíquota ICMS (%) |
| `cstPis` | string | não | `"01"` | CST PIS |
| `aliquotaPis` | decimal | não | 0 | Alíquota PIS (%) |
| `cstCofins` | string | não | `"01"` | CST COFINS |
| `aliquotaCofins` | decimal | não | 0 | Alíquota COFINS (%) |

##### CST/CSOSN conforme CRT

| CRT | Regime | Campo | Valores válidos |
|-----|--------|-------|----------------|
| 1, 2, 4 | Simples Nacional | CSOSN | 101, 102, 103, 201, 202, 203, 300, 400, 500, 900 |
| 3 | Regime Normal | CST | 00, 02, 40, 41, 50 |

#### `nfe`

| Campo | Tipo | Obrigatório | Padrão | Descrição |
|-------|------|:-----------:|--------|-----------|
| `numero` | int | **sim** | — | Número da NFCe (> 0) |
| `serie` | int | **sim** | — | Série (> 0) |
| `modelo` | int | não | 65 | Modelo (sempre 65) |
| `naturezaOperacao` | string | não | `"VENDA"` | Natureza da operação |
| `finalidade` | int | não | 1 | 1=Normal, 2=Complementar, 3=Ajuste, 4=Devolução |
| `consumidorFinal` | int | não | 1 | 1=Sim, 0=Não |
| `presencaComprador` | int | não | 1 | 1=Presencial, 2=Internet, 3=Telefone, 4=Delivery, 5=Presencial fora, 9=Outros |
| `valorGorjeta` | decimal | não | 0 | Gorjeta (injetada automaticamente como produto 99999) |
| `valorDesconto` | decimal | não | 0 | **Desconto global/cupom** (distribuído proporcionalmente aos itens) |
| `parcelas` | array | não | `null` | **Array de pagamentos** (recomendado). Se presente, `tipoPagamento`/`valorPagamento`/`formaPagamento` são ignorados |
| `cnpjCredenciadora` | string | não | `null` | CNPJ da credenciadora (fallback para parcelas) |
| `codigoAutorizacao` | string | não | `null` | Código de autorização (fallback) |

> **Legacy:** Os campos `formaPagamento`, `tipoPagamento` e `valorPagamento` no nível do `nfe` só são usados se `parcelas` não for informado.

#### `parcelas[]`

| Campo | Tipo | Obrigatório | Padrão | Descrição |
|-------|------|:-----------:|--------|-----------|
| `tipoPagamento` | int | não | 1 | Código da forma de pagamento (ver tabela) |
| `formaPagamento` | string | não | `"dinheiro"` | Ignorado (usar `tipoPagamento`) |
| `valor` | decimal | não | 0 | Valor pago |
| `cnpjCredenciadora` | string | não | `null` | CNPJ da credenciadora (usado no `<card>`) |
| `bandeiraCartao` | string | não | `null` | Bandeira (visa, mastercard, elo, etc.) |
| `codigoAutorizacao` | string | não | `null` | Código de autorização (usado no `<card>`) |
| `cnpjRecebedor` | string | não | `null` | CNPJ do recebedor |
| `terminalPagamento` | string | não | `null` | Terminal de pagamento |

**Bandeiras suportadas** (case-insensitive): `visa`, `mastercard`/`master`, `amex`/`americanexpress`, `sorocred`, `diners`/`dinersclub`, `elo`, `hipercard`, `aura`, `cabal`, `alelo`, `banescard`, `calcard`, `credz`, `discover`, `goodcard`, `greencard`, `hiper`, `jcb`, `mais`, `maxvan`, `policard`, `redecompras`, `sodexo`, `valecard`, `verocheque`, `vr`, `ticket`. Qualquer outra → `"Outros"` (código 99).

#### Tabela de `tipoPagamento`

| Código | Descrição | Gera `<card>`? |
|:------:|-----------|:--------------:|
| 1 | Dinheiro | não |
| 2 | Cheque | não |
| 3 | Cartão Crédito | **sim** |
| 4 | Cartão Débito | **sim** |
| 5 | Cartão da Loja | **sim** |
| 10 | Vale Alimentação | não |
| 11 | Vale Refeição | não |
| 12 | Vale Presente | não |
| 13 | Vale Combustível | não |
| 14 | Duplicata Mercantil | não |
| 15 | Boleto Bancário | não |
| 16 | Depósito Bancário | não |
| 17 | PIX Dinâmico | **sim** |
| 18 | TED | **sim** |
| 19 | Programa de Fidelidade | **sim** |
| 20 | PIX Estático | **sim** |
| 21 | Crédito em Loja | **sim** |
| 22 | Pagamento Eletrônico | **sim** |
| 23 | PIX Automático | **sim** |
| 24 | TEF Book Transfer | **sim** |
| 90 | Sem Pagamento | não |
| 91 | Pagamento Posterior | não |
| 99 | Outros | não |

Quando o tipo gera `<card>`, o backend preenche automaticamente:
- `tpIntegra` = 2 (não integrado)
- `CNPJ` = `parcela.cnpjCredenciadora` → `nfe.cnpjCredenciadora` → CNPJ do emitente
- `tBand` = mapeado de `parcela.bandeiraCartao` (99 = Outros se vazio)
- `cAut` = `parcela.codigoAutorizacao`

---

## Troco

Calculado automaticamente: `vTroco = totalPago - vNF` (se `totalPago > vNF`, senão 0). Exibido no PDF apenas se > 0.

---

## Gorjeta

- Campo separado `nfe.valorGorjeta` — não deve ser enviado como produto
- Backend injeta automaticamente um produto:
  - Código `99999`, descrição `"GORJETA CONCEDIDA"`
  - NCM `00000000`, CFOP `5102`
  - CSOSN `400`, PIS `99`, COFINS `49`
- O valor da gorjeta entra no `vNF` e o troco é recalculado considerando ela

---

## Desconto e Cupom de Desconto

O sistema suporta duas formas de desconto que podem ser utilizadas em conjunto:

### 1. Desconto item a item
- Campo `valorDesconto` em cada produto.
- Aplicado diretamente sobre o respectivo item.

### 2. Desconto global (Cupom de Desconto)
- Campo `valorDesconto` no objeto `nfe`.
- Para atender às regras da SEFAZ, este desconto global é **distribuído proporcionalmente** entre todos os produtos da nota (com base no `valorTotal` de cada item). O resto da divisão por arredondamento é somado ao último item para garantir a exatidão.

### Cálculo final por item e na Nota
- O desconto total de cada item no XML final será a soma do seu desconto individual (`valorDesconto` do produto) + sua parcela proporcional do desconto global.
- Base de ICMS, PIS e COFINS de cada item: `valorTotal - (descontoItem + descontoGlobalProporcional)`.
- Totais da NF-e:
  - `vProd` (Total bruto): `sum(valorTotal)`
  - `vDesc` (Total de descontos): `sum(descontoItem) + descontoGlobal`
  - `vNF` (Total líquido): `vProd - vDesc + sum(frete + seguro + outras)`

Exemplo com ambos os descontos:
```json
{
  "produtos": [
    { "codigo": "001", "quantidade": 2, "valorUnitario": 5.00, "valorTotal": 10.00, "valorDesconto": 1.00 },
    { "codigo": "002", "quantidade": 1, "valorUnitario": 10.00, "valorTotal": 10.00, "valorDesconto": 2.00 }
  ],
  "nfe": {
    "numero": 12,
    "serie": 1,
    "valorDesconto": 3.00
  }
}
```
* **Distribuição do desconto global (3.00)**:
  * Como ambos os produtos têm o mesmo `valorTotal` (10.00), cada um recebe 1.50 de desconto global.
* **Resultado final nos itens**:
  * Item 1: `vDesc` final = `1.00` (individual) + `1.50` (global) = `2.50`. Base de cálculo = `10.00 - 2.50 = 7.50`.
  * Item 2: `vDesc` final = `2.00` (individual) + `1.50` (global) = `3.50`. Base de cálculo = `10.00 - 3.50 = 6.50`.
* **Resultado nos totais da nota**:
  * `vProd = 20.00`
  * `vDesc = 6.00`
  * `vNF = 14.00`

---

## Respostas

### Sucesso (cStat 100)

```json
{
  "id": "order-456",
  "sucesso": true,
  "codigoStatus": 100,
  "motivo": "Autorizado o uso da NF-e",
  "chaveAcesso": "35230653912651000114650010000000111111111111",
  "numeroProtocolo": "135230000123456",
  "numeroNFe": 12,
  "serie": 1,
  "xmlAutorizado": "<?xml version=\"1.0\" encoding=\"utf-8\"?>...<procNFe>...</procNFe>",
  "erros": []
}
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `id` | string | Mesmo ID enviado na requisição |
| `sucesso` | bool | `true` se autorizada |
| `codigoStatus` | int | Código de retorno SEFAZ |
| `motivo` | string | Motivo (autorização ou rejeição) |
| `chaveAcesso` | string | Chave de 44 dígitos |
| `numeroProtocolo` | string | Protocolo SEFAZ |
| `numeroNFe` | int | Número da NF-e |
| `serie` | int | Série |
| `xmlAutorizado` | string | XML do procNFe (NFe + protocolo) |
| `erros` | array | Lista de erros `{codigo, mensagem}` |

### Erro de validação

```json
{
  "id": "order-456",
  "sucesso": false,
  "codigoStatus": 0,
  "motivo": "Emitente é obrigatório",
  "chaveAcesso": null,
  "numeroProtocolo": null,
  "numeroNFe": null,
  "serie": null,
  "xmlAutorizado": null,
  "erros": [
    { "codigo": 1, "mensagem": "Emitente é obrigatório" }
  ]
}
```

### Rejeição SEFAZ

```json
{
  "id": "order-456",
  "sucesso": false,
  "codigoStatus": 531,
  "motivo": "Diferença entre BC do ICMS e a soma dos itens",
  "chaveAcesso": "35230653912651000114650010000000111111111111",
  "numeroProtocolo": null,
  "numeroNFe": 12,
  "serie": 1,
  "xmlAutorizado": null,
  "erros": [
    { "codigo": 531, "mensagem": "Diferença entre BC do ICMS e a soma dos itens" }
  ]
}
```

### Erro interno (exceção)

```json
{
  "id": "order-456",
  "sucesso": false,
  "codigoStatus": 0,
  "motivo": "Nenhum certificado configurado.",
  "chaveAcesso": null,
  "numeroProtocolo": null,
  "numeroNFe": null,
  "serie": null,
  "xmlAutorizado": null,
  "erros": [
    { "codigo": -1, "mensagem": "Nenhum certificado configurado." }
  ]
}
```

---

## Validações (pré-emissão)

| Código | Mensagem |
|:------:|----------|
| -1 | Pedido nulo / JSON inválido / exceção |
| 1 | Emitente é obrigatório |
| 2 | CNPJ do emitente é obrigatório |
| 3 | Razão social do emitente é obrigatória |
| 4 | UF do emitente é obrigatória |
| 5 | Dados da NFe (nfe) são obrigatórios |
| 6 | Número da NFe é obrigatório |
| 7 | Série da NFe é obrigatória |
| 8 | Pelo menos um produto é obrigatório |
| 9 | Produto [i]: código é obrigatório |
| 10 | Produto [i]: descrição é obrigatória |
| 11 | Produto [i]: quantidade deve ser maior que zero |
| 12 | Produto [i]: valor unitário deve ser maior que zero |
| 14 | Produto [i]: CSOSN inválido para Simples Nacional |

---

## Fluxo completo

```
Frontend                          WebSocketNFCeService               SEFAZ
   |                                      |                            |
   |-- JSON (tipo: "setup") ------------>|                            |
   |<-- {"tipo":"setup","status":"ok"} --|                            |
   |                                      |                            |
   |-- JSON (tipo: "nfce") ------------->|                            |
   |                                      |-- Envia NFe ------------->|
   |                                      |<-- Autorização ------------|
   |                                      |                            |
   |                                      |-- Salva XML (Xmls/...)     |
   |                                      |-- Gera PDF (Pdfs/...)      |
   |                                      |-- Envia XML para Cloud API |
   |<-- ResultadoNFe --------------------|                            |
```

O XML autorizado é salvo em `Xmls/YYYYMM/{chave}-nfe.xml` e o PDF em `Pdfs/YYYYMM/{chave}-nfe.pdf`.
