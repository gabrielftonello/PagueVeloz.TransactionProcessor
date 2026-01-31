# Estratégia de testes

## Camadas

### Unit tests (Domain)
Objetivo: validar invariantes e regras sem infra.
Exemplos:
- `Debit` respeita `CreditLimit`
- `Reserve` não deixa `AvailableBalance` negativo
- `Capture` exige saldo reservado suficiente

### Integration tests
Usa Testcontainers para subir SQL Server real e validar:
- concorrência (parallel credits/debits/transfers)
- idempotência (mesmo `reference_id`)
- atomicidade (transfer com destino inexistente não debita origem)
- worker (fila assíncrona / outbox)

---

## Como rodar

```bash
dotnet test
```

Dicas:
- Rode com `-c Release` para benchmarks leves
- Em CI, use caches de imagem Docker para acelerar Testcontainers
