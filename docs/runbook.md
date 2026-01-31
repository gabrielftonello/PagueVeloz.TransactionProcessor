# Runbook operacional (local)

## Endpoints de saúde
- Liveness: `GET http://localhost:5000/health/live`
- Readiness: `GET http://localhost:5000/health/ready`

## Verificar Outbox “presa”
1. Gere uma transação.
2. Veja logs do Worker:
```bash
docker compose logs -f worker
```
3. Se necessário, consulte `OutboxEvents` no SQL Server:
- `ProcessedAt is null` e `NextAttemptAt <= now` indica “pronto para processar”.

## RabbitMQ
UI: `http://localhost:15672` (guest/guest)
- Exchange: `tx.events` (topic)
- RoutingKey: `transaction.processed`

## Kibana
`http://localhost:5601`
- Discover → `filebeat-*`

KQL útil:
- `service.name : "PagueVeloz.TransactionProcessor"`
- `event_type : "transaction.processed"`

## Elasticsearch não sobe
Linux:
```bash
sudo sysctl -w vm.max_map_count=262144
```
Depois:
```bash
docker compose up --build
```
