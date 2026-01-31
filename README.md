# PagueVeloz – Transaction Processor (Net 9 + SQL Server + RabbitMQ + EFK/APM)

Implementação de um **processador de transações** com foco em:
- **Consistência** (operações atômicas com rollback)
- **Concorrência** (locks e ordenação para evitar deadlocks)
- **Idempotência** (reference_id único)
- **Auditabilidade** (ledger por conta + outbox)
- **Observabilidade** (logs estruturados + traces)

> Convenção: valores monetários são `long` em **centavos**.

---

## Arquitetura

- **Api**: Minimal API (REST) + Swagger, serialização `snake_case`.
- **Application**: casos de uso (MediatR), validação (FluentValidation), contratos.
- **Domain**: regras de negócio (saldo/reserva/captura/limite), invariantes.
- **Infrastructure**: SQL Server (EF Core), repositórios, outbox, fila de comandos, RabbitMQ.
- **Worker**: processa **outbox** (publica em RabbitMQ) e **fila interna** (`/transactions/enqueue`).

Padrões aplicados:
- **Clean Architecture / DDD-ish** (separação Domain/Application/Infra)
- **Outbox Pattern** (garante “at-least-once delivery” para eventos)
- **Pessimistic Locking** + **ordenação de locks** (evita race + deadlock)

---

## Modelo de Dados (SQL Server)

Tabelas principais:
- `Accounts` – saldo, reservado, limite, status, `LedgerSequence`.
- `Transactions` – registro idempotente por `ReferenceId` (**UNIQUE**).
- `AccountEvents` – ledger imutável por conta (sequência por conta).
- `OutboxEvents` – eventos pendentes de publicação (retry/backoff).
- `QueuedCommands` – fila interna para processamento assíncrono.

---

## Semântica das Operações

- `credit`: `balance += amount`
- `debit`: `balance -= amount` (permitido ir negativo até `credit_limit`)
- `reserve`: `reserved += amount` (não mexe no `balance`)
- `capture`: **confirma reserva** → `reserved -= amount` e `balance -= amount`
- `transfer`: débito na origem + crédito no destino (mesma transação SQL)
- `reversal`: desfaz uma transação anterior (`related_reference_id`)
  - `credit` → débito
  - `debit` → crédito
  - `reserve` → release
  - `capture` → refund (crédito)
  - `transfer` → transfere de volta (débito no destino + crédito na origem)

---

## Concorrência e Consistência

- Cada operação roda dentro de uma **transação SQL** (IsolationLevel `Serializable` no caso de processamento síncrono).
- Para evitar “double spend”:
  - leitura com lock: `SELECT ... WITH (UPDLOCK, ROWLOCK)`
  - para `transfer` e `reversal` com 2 contas: **lock ordering** (ordem lexicográfica do `account_id`).

---

## Idempotência

- `Transactions.ReferenceId` é **único**.
- Se uma requisição chega repetida com o mesmo `reference_id`, o sistema:
  - retorna o mesmo resultado previamente persistido
  - não re-executa a lógica de negócio.

---

## Observabilidade (EFK + APM)

- Logs estruturados (Serilog, compact JSON) → **Filebeat** → Elasticsearch → Kibana.
- Traces (OpenTelemetry) → **APM Server** → Kibana (APM).

Serviços:
- Kibana: `http://localhost:5601`
- RabbitMQ UI: `http://localhost:15672` (guest/guest)
- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`

### Visualizar logs no Kibana

- Kibana: `http://localhost:5601` (pode levar ~1–2 min para ficar “available”).
- O compose inclui um **auto-bootstrap** (service `pv-kibana-setup`) que:
  - importa *Data Views* (`filebeat-*` e `pv-events-*`)
  - cria 2 *Saved Searches* (API e Worker)
  - define `filebeat-*` como padrão

Onde olhar:

1) **Discover** → selecione o *data view* `filebeat-*`
2) No menu de **Saved** / **Open**, abra:
   - `PV API - Requests & Errors`
   - `PV Worker - Errors & Warnings`

Filtros úteis (KQL):
- `reference_id : "TXN-001"`
- `account_id : "ACC-001"`
- `operation : "debit"`
- `service.name : "pv-consumer"` (consumer RabbitMQ)
- `event_type : "transaction.processed"` (quando consultar `pv-events-*`)

> Persistência: Kibana salva configurações no Elasticsearch (índice `.kibana`). O compose cria o volume `esdata`, então **data views / saved searches ficam persistidos** entre `docker compose down/up`. Se você rodar `docker compose down -v`, aí sim reseta tudo.

### Visualizar traces (APM)


No Kibana, vá em **Observability → APM**. Se não aparecer nada, valide que as variáveis `OTEL_EXPORTER_OTLP_ENDPOINT` e `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` estão presentes nos containers `api` e `worker`.

### Métricas (OpenTelemetry → Elastic/Kibana)

Além de *traces*, a API também publica **métricas** via OpenTelemetry:

- HTTP server/client (`AspNetCore` + `HttpClient`)
- Runtime (.NET GC, threads etc.)
- Process (CPU/memória)

Para visualizar no Kibana, envie métricas para o stack Elastic via OTLP (APM Server ou Elastic Agent). No `docker compose`, basta garantir as variáveis (exemplo):

- `OTEL_EXPORTER_OTLP_ENDPOINT=http://apm-server:8200`
- `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`

Depois, no Kibana:

- **Observability → Metrics** (ou **Infrastructure**) para *runtime/process*
- **APM → Services** para ver métricas correlacionadas por serviço

#### Config simples para AWS (posterior)

Se você for enviar telemetria para AWS (por exemplo, usando o AWS Distro for OpenTelemetry Collector), você pode apontar a aplicação para um collector interno:

- `OTEL_EXPORTER_OTLP_ENDPOINT=http://<collector>:4318`
- `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`

Um exemplo de config de collector (OTLP receiver + export) pode ser adicionado em `infra/aws/otel-collector.yaml`.

---

---

## Consumer (RabbitMQ)

Além do **Worker** (que publica eventos via Outbox), o compose sobe um **Consumer de exemplo** (`pv-consumer`) que:

- Cria/binda a fila `tx.events.audit` no exchange `tx.events` (topic, binding `#`)
- Consome eventos publicados pelo sistema
- Loga o payload em JSON (Filebeat -> Elasticsearch -> Kibana)
- (Opcional) Indexa o evento cru em `pv-events-YYYY.MM.DD` (Elasticsearch), criando o data view `pv-events-*`

Ver logs do consumer:
```bash
docker compose logs -f consumer
```

## Como rodar (Docker)

```bash
docker compose up --build
```

### Troubleshooting (containers não sobem)

Veja status:
```bash
docker compose ps
```

Veja logs do serviço com problema:
```bash
docker compose logs -f filebeat
docker compose logs -f worker
docker compose logs -f sqlserver
docker compose logs -f elasticsearch
```

Reset completo (remove volumes/dados persistidos) — útil quando você alterou o compose:
```bash
docker compose down -v
docker compose up --build
```

**Elasticsearch não inicia (Linux):** ajuste `vm.max_map_count`:
```bash
sudo sysctl -w vm.max_map_count=262144
```

**Worker aparentemente “não faz nada”:** ele roda em loop e processa Outbox/Fila. Se não houver eventos pendentes, ele fica ocioso. Crie uma conta e rode uma transação para ver atividade.

Health:
- `GET http://localhost:5000/health/live`
- `GET http://localhost:5000/health/ready`

---

## Endpoints

### Criar conta
`POST /api/accounts`

Exemplo:
```bash
curl -X POST http://localhost:5000/api/accounts \
  -H "Content-Type: application/json" \
  -d @samples/create_account.json
```

### Consultar conta
`GET /api/accounts/{account_id}`

### Processar transação (síncrono)
`POST /api/transactions`

### Enfileirar transação (assíncrono)
`POST /api/transactions/enqueue`

- Retorna `202 Accepted` com status `pending`.
- O **Worker** consome `QueuedCommands` e executa o mesmo caso de uso.

### Consultar transação
`GET /api/transactions/{reference_id}`

---

## CLI (opcional)

Enviar um arquivo JSON de transações:

```bash
dotnet run --project src/PagueVeloz.TransactionProcessor.Cli -- --url http://localhost:5000 --file samples/transactions_sync.json
```

Para enfileirar:
```bash
dotnet run --project src/PagueVeloz.TransactionProcessor.Cli -- --url http://localhost:5000 --file samples/transactions_sync.json --async
```

---

## Testes

```bash
dotnet test
```

- **UnitTests**: regras de negócio do domínio.
- **IntegrationTests**: sobe SQL Server via Testcontainers e valida concorrência + idempotência.

---

## Pontos de extensão / produção

- Trocar `EnsureCreated()` por **migrations** (EF Core) em ambiente real.
- Publicar eventos com **schema registry** / versionamento.
- Consumidores RabbitMQ e DLQ.
- Rate limiting, authn/authz, multi-tenant.
