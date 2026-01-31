# Operações e regras de negócio

Este documento define as operações suportadas e seus pré-requisitos, efeitos e códigos de erro esperados.

## Glossário

- **Balance**: saldo total da conta (pode ficar negativo se houver limite de crédito)
- **ReservedBalance**: saldo reservado (ex.: pré-autorização)
- **AvailableBalance**: saldo disponível = `Balance - ReservedBalance`
- **CreditLimit**: limite que permite `Balance` negativo até `-CreditLimit`

> Todos os valores são em centavos (inteiro).

---

## Contrato de entrada (TransactionRequest)

Campos:
- `operation`: `credit | debit | reserve | capture | reversal | transfer`
- `account_id`: conta principal
- `amount`: valor em centavos (positivo)
- `currency`: ex. `BRL`
- `reference_id`: id único (idempotência)
- `metadata`: dicionário opcional
- `target_account_id`: obrigatório para `transfer`
- `related_reference_id`: obrigatório para `reversal` (e opcional para rastrear `capture`)

---

## Contrato de saída (TransactionResponse)

- `transaction_id`: `{reference_id}-PROCESSED`
- `status`: `success | failed | pending`
- `balance`, `reserved_balance`, `available_balance`: estado após operação
- `timestamp`: ISO-8601 (UTC)
- `error_message`: mensagem (quando `failed`)

---

## Operações

### 1) credit
**Pré-requisitos**
- conta ativa
- `amount > 0`
- moeda compatível

**Efeito**
- `Balance += amount`

**Erros comuns**
- conta inexistente
- conta não ativa
- currency mismatch
- amount inválido

---

### 2) debit
**Pré-requisitos**
- conta ativa
- `amount > 0`
- `AvailableBalance + CreditLimit >= amount`

**Efeito**
- `Balance -= amount`

**Observação**
- permite `Balance` negativo até o limite

---

### 3) reserve
**Pré-requisitos**
- conta ativa
- `amount > 0`
- `AvailableBalance >= amount`

**Efeito**
- `ReservedBalance += amount`
- `Balance` não muda, mas `AvailableBalance` diminui

---

### 4) capture
**Pré-requisitos**
- conta ativa
- `amount > 0`
- `ReservedBalance >= amount`

**Efeito**
- `ReservedBalance -= amount`
- `Balance -= amount`

**Observação**
- `related_reference_id` pode ser usado para rastrear a reserva original (não é obrigatório para consistência).

---

### 5) transfer
**Pré-requisitos**
- `target_account_id` obrigatório
- ambas contas ativas
- mesmas moedas
- origem deve suportar `Debit(amount)` considerando limite

**Efeito**
- origem: `Balance -= amount`
- destino: `Balance += amount`
- ocorre na **mesma transação SQL** (atômico)

---

### 6) reversal
**Pré-requisitos**
- `related_reference_id` obrigatório
- transação original deve existir
- original não pode ter sido revertida anteriormente

**Efeito**
Depende da operação original:

| Original | Reversal aplica |
|---|---|
| `credit` | `Debit(amount)` |
| `debit` | `Credit(amount)` |
| `reserve` | `ReleaseReservation(amount)` |
| `capture` | `RefundCapture(amount)` |
| `transfer` | `Debit` no destino + `Credit` na origem |

**Observação**
- Marca a original como `IsReversed=true` para evitar reversões duplicadas.

---

## Exemplos rápidos (cURL)

### Transfer
```bash
curl -X POST http://localhost:5000/api/transactions \
  -H "Content-Type: application/json" \
  -d '{
    "operation": "transfer",
    "account_id": "ACC-A",
    "target_account_id": "ACC-B",
    "amount": 2500,
    "currency": "BRL",
    "reference_id": "TXN-A-2",
    "metadata": { "reason": "payout" }
  }'
```

### Reversal de transfer
```bash
curl -X POST http://localhost:5000/api/transactions \
  -H "Content-Type: application/json" \
  -d '{
    "operation": "reversal",
    "account_id": "ACC-A",
    "amount": 2500,
    "currency": "BRL",
    "reference_id": "TXN-A-3",
    "related_reference_id": "TXN-A-2",
    "metadata": { "reason": "chargeback" }
  }'
```
