# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
# Restore only this project to keep the Docker layer small/independent from solution membership.
RUN dotnet restore ./src/PagueVeloz.TransactionProcessor.EventConsumer/PagueVeloz.TransactionProcessor.EventConsumer.csproj
RUN dotnet publish ./src/PagueVeloz.TransactionProcessor.EventConsumer/PagueVeloz.TransactionProcessor.EventConsumer.csproj -c Release -o /app/publish --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER root
RUN mkdir -p /var/log/pv && chown -R app:app /var/log/pv
USER app

ENTRYPOINT ["dotnet", "PagueVeloz.TransactionProcessor.EventConsumer.dll"]
