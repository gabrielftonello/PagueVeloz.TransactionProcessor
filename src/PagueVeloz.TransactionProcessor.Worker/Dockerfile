# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore ./PagueVeloz.TransactionProcessor.sln
RUN dotnet publish ./src/PagueVeloz.TransactionProcessor.Worker/PagueVeloz.TransactionProcessor.Worker.csproj -c Release -o /app/publish --no-restore

# Runtime
# The Worker references the shared framework Microsoft.AspNetCore.App (via FrameworkReference),
# so it must run on the ASP.NET runtime image (which contains Microsoft.AspNetCore.App).
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Ensure shared log directory is writable (the base image runs as a non-root user).
USER root
RUN mkdir -p /var/log/pv && chown -R app:app /var/log/pv
USER app

ENTRYPOINT ["dotnet", "PagueVeloz.TransactionProcessor.Worker.dll"]
