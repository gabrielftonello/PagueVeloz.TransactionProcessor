FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore ./PagueVeloz.TransactionProcessor.sln
RUN dotnet publish ./src/PagueVeloz.TransactionProcessor.Api/PagueVeloz.TransactionProcessor.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER root
RUN mkdir -p /var/log/pv && chown -R app:app /var/log/pv
USER app

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "PagueVeloz.TransactionProcessor.Api.dll"]
