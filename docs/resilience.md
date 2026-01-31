# Resiliência e eventos

## Objetivo
Garantir que:
1) o processamento transacional **não dependa** do broker estar disponível
2) eventos sejam publicados com **retry** e **controle de carga**

---

## Outbox Pattern

### Por quê?
Sem outbox, existem dois modos ruins:
- publica primeiro → broker ok, banco falha → evento “fantasma”
- commita banco primeiro → banco ok, broker falha → evento perdido

Com outbox:
- grava banco + outbox **na mesma transação**
- worker publica depois (eventual consistency)

### Garantia obtida
- **At-least-once** no publish
- consumidores devem ser idempotentes (ex.: chave = `event_id` ou `reference_id`)

---

## Retry com backoff exponencial + jitter

Quando publish falha:
- incrementa `Attempts`
- calcula `NextAttemptAt`:
  - `2^attempts` segundos (cap em 60s)
  - + jitter aleatório (0–250ms)

Evita:
- “thundering herd”
- saturação do broker após outage

---

## Circuit breaker (Polly)

Config:
- abre após 5 exceções
- mantém aberto 30s

Benefícios:
- protege recursos (CPU/threads/banco)
- reduz cascata de falhas

---

## Observabilidade em falhas
- erro do publish vai para `OutboxEvents.LastError`
- logs estruturados registram:
  - `event_id`, `attempts`, `next_attempt_at`
