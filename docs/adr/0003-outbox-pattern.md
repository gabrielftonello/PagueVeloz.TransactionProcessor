# ADR 0003 – Outbox Pattern para eventos

## Contexto
Publicar eventos e commitar banco sem 2PC pode gerar “evento fantasma” ou evento perdido.

## Decisão
Gravar eventos em `OutboxEvents` na mesma transação do processamento e publicar via Worker com retry/backoff.

## Consequências
✅ consistência entre estado e eventos  
✅ tolera indisponibilidade do broker  
⚠️ entrega “at-least-once” (consumidores precisam ser idempotentes)
