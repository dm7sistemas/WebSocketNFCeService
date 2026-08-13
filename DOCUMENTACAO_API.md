# Documentação da API WebSocket (NFC-e e NF-e)

O **AZFoodWebSocketNFCe** é um serviço (Windows Service / Console App) que recebe requisições via WebSocket para emitir Notas Fiscais Eletrônicas (NFC-e Modelo 65 e NF-e Modelo 55) de forma síncrona.

## Conexão

- **Protocolo:** `ws://` ou `wss://`
- **Porta Padrão:** Verificar a configuração no `appsettings.json` da aplicação host.
- **Formato das mensagens:** JSON

---

## 1. Configuração do Serviço (`tipo: "setup"`)

Antes de emitir notas, o serviço precisa estar configurado com os dados do Certificado Digital e do CSC (apenas para NFC-e). Isso pode ser feito via arquivo `appsettings.json` ou dinamicamente enviando um payload de setup pela conexão WebSocket.

### Payload de Envio:
```json
{
  "tipo": "setup",
  "certificado": {
    "arquivo": "C:\\Certificados\\empresa.pfx",
    "senha": "senha_do_certificado",
    "serial": "" // Pode usar o serial caso o certificado já esteja instalado no Windows
  },
  "csc": {
    "idToken": "000001",
    "token": "01234567-89ab-cdef-0123-456789abcdef"
  },
  "timeout": 30000,
  "cloudApi": {
    "url": "https://xml.dm7sistemas.com.br/api/functions/receiveXml",
    "apiKey": "SUA_API_KEY"
  }
}
```

### Resposta:
```json
{
  "status": "ok",
  "mensagem": "Config salva com sucesso"
}
```

---

## 2. Emissão de Documento Fiscal (`tipo: "nfe"` ou `"nfce"`)

Envia os dados do pedido para geração do XML, assinatura, transmissão e geração do PDF em layout bobina (NFC-e) ou A4 (NF-e).

**Obrigatório:** Preencher `"tipo"` com `"nfe"` (Modelo 55) ou `"nfce"` (Modelo 65). O serviço gerencia o envio na modalidade síncrona, portanto a resposta aguardará o processamento da SEFAZ.

### Payload de Envio (Exemplo Completo):

```json
{
  "tipo": "nfe", 
  "id": "order-12345",
  "ambiente": "producao",
  "emitente": {
    "cnpj": "53912651000114",
    "razaoSocial": "EMPRESA DE TESTE LTDA",
    "nomeFantasia": "FANTASIA TESTE",
    "inscricaoEstadual": "182347678112",
    "logradouro": "AVENIDA PRINCIPAL",
    "numero": "1000",
    "bairro": "CENTRO",
    "codigoMunicipio": 3503307,
    "municipio": "ARARAS",
    "uf": "SP",
    "cep": "13600000",
    "crt": 1
  },
  "destinatario": {
    "cpf": "11122233344",
    "cnpj": "",
    "nome": "NOME DO CLIENTE",
    "uf": "SP",
    "inscricaoEstadual": "",
    "logradouro": "RUA SECUNDARIA",
    "numero": "200",
    "bairro": "JARDIM",
    "codigoMunicipio": 3503307,
    "municipio": "ARARAS",
    "cep": "13600000"
  },
  "produtos": [
    {
      "codigo": "7891234567890",
      "descricao": "PRODUTO TESTE",
      "ncm": "21069090",
      "cfop": "5102",
      "unidade": "UN",
      "quantidade": 1.0,
      "valorUnitario": 10.50,
      "valorTotal": 10.50,
      "cstIcms": "102",
      "aliquotaIcms": 0.0,
      "cstPis": "99",
      "cstCofins": "99"
    }
  ],
  "nfe": {
    "numero": 12,
    "serie": 1,
    "naturezaOperacao": "VENDA DE MERCADORIA",
    "finalidade": 1,
    "consumidorFinal": 1,
    "presencaComprador": 1,
    "valorGorjeta": 1.05,
    "parcelas": [
      {
        "tipoPagamento": 3,
        "valor": 11.55,
        "bandeiraCartao": 99,
        "codigoAutorizacao": "123456",
        "cnpjCredenciadora": "12345678000199"
      }
    ]
  }
}
```

### Notas sobre os Campos:
- **Gorjetas:** Para adicionar gorjetas, envie no campo `nfe.valorGorjeta`. O sistema automaticamente injetará um produto descritivo "GORJETA CONCEDIDA" com código interno `99999` e as tributações exigidas.
- **Pagamentos Eletrônicos (Cartão/PIX):** Se `tipoPagamento` for referente a Cartão/TEF (ex: `3` ou `4`), os campos de `bandeiraCartao`, `codigoAutorizacao` e `cnpjCredenciadora` passam a ser necessários para compor a tag `<card>`.
- **Destinatário em branco:** Se `cpf` e `cnpj` forem enviados em branco na `"nfce"`, a nota é autorizada como "CONSUMIDOR NÃO IDENTIFICADO". Na `nfe` (Modelo 55), o destinatário costuma ser obrigatório de acordo com as UFs.

### Resposta de Sucesso:
Retorna `sucesso = true` contendo a chave, protocolo, e o XML aprovado. O Serviço também salva automaticamente uma cópia PDF do DANFE localmente na mesma máquina (em uma pasta `Pdfs/YYYYMM/`) e manda o XML para sua API Cloud.

```json
{
  "id": "order-12345",
  "sucesso": true,
  "motivo": "Autorizado o uso da NF-e",
  "codigoStatus": 100,
  "chaveAcesso": "35260853912651000114550010000000121123456789",
  "numeroNFe": 12,
  "serie": 1,
  "numeroProtocolo": "135260001234567",
  "xmlAutorizado": "<?xml version=\"1.0\" encoding=\"utf-8\"?><nfeProc>...</nfeProc>",
  "erros": []
}
```

### Resposta de Rejeição/Erro:
```json
{
  "id": "order-12345",
  "sucesso": false,
  "motivo": "Rejeição: Total da NF difere do somatorio dos Valores compoe o vNF",
  "codigoStatus": 532,
  "chaveAcesso": null,
  "numeroNFe": null,
  "serie": null,
  "numeroProtocolo": null,
  "xmlAutorizado": null,
  "erros": [
    {
      "codigo": 532,
      "mensagem": "Rejeição: Total da NF difere do somatorio dos Valores compoe o vNF"
    }
  ]
}
```
