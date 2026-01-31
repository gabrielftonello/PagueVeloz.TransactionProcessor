# ADR 0001 – Clean Architecture / DDD-ish

## Contexto
O desafio pede um sistema com possibilidade de evolução para microsserviços, separação clara de responsabilidades e facilidade de testes.

## Decisão
Adotar uma estrutura em camadas:
- Domain (regras)
- Application (casos de uso)
- Infrastructure (persistência/mensageria)
- Entrypoints (API/Worker/CLI)

## Consequências
✅ regras isoladas e testáveis  
✅ troca de infra (ex.: Postgres, Kafka) com mínimo impacto  
⚠️ mais arquivos/projetos do que uma abordagem “monolítica simples”
