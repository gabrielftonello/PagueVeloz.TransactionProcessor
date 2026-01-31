# Concorrência e consistência

## Problema-alvo
Operações simultâneas na mesma conta podem causar:
- “lost update” (última escrita vence)
- double spend (duas operações aprovam com o mesmo saldo)
- inconsistência entre `balance`, `reserved_balance` e `available_balance`

## Estratégia adotada
**Pessimistic locking** no SQL Server:

- Para operações de 1 conta:
  - `SELECT * FROM Accounts WITH (UPDLOCK, ROWLOCK) WHERE AccountId = @id`
- Para operações de 2 contas:
  - ordena `account_id` e aplica o lock nessa ordem

### Por que `UPDLOCK`?
- garante exclusividade de atualização ao longo da transação
- evita que duas transações leiam o mesmo saldo como “disponível” e aprovem ambas

### Por que ordenação de locks?
Deadlock clássico:
- T1: lock(A) → lock(B)
- T2: lock(B) → lock(A)

Ordenando por id, todos tentam lock na mesma ordem.

---

## Isolation Level
No processamento síncrono (`POST /api/transactions`) é usado:
- `IsolationLevel.Serializable`

Vantagens:
- evita anomalias de leitura/escrita no raciocínio do desafio
- reduz “surpresas” em cenários de alta concorrência

Trade-off:
- maior contenção em casos extremos; em produção pode ser ajustado para `RepeatableRead`/`ReadCommitted` com locks corretos e invariantes adicionais.

---

## Retry e tolerância a deadlock

O caso de uso faz retry em:
- `DbUpdateConcurrencyException`
- deadlock do SQL Server (erro 1205)

A cada retry:
- rollback
- limpa ChangeTracker
- espera curta (evita loop “busy”)
