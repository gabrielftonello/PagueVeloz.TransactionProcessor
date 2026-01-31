# ADR 0002 – Locking pessimista por conta (SQL Server)

## Contexto
Concorrência alta em operações financeiras pode gerar double spend e inconsistências.

## Decisão
Usar locking pessimista por conta com:
- `UPDLOCK` + `ROWLOCK` para serializar atualizações
- ordenação determinística para operações multi-contas

## Consequências
✅ consistência forte e simples de raciocinar  
✅ testes de concorrência passam sem “lost update”  
⚠️ contenção em hot accounts (mitigável com particionamento e design adicional)
