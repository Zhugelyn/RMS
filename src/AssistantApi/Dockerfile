FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AssistantApi/AssistantApi.csproj AssistantApi/
COPY AgentPacks/ AgentPacks/
RUN dotnet restore AssistantApi/AssistantApi.csproj
COPY AssistantApi/ AssistantApi/
WORKDIR /src/AssistantApi
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 appuser
COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app
USER appuser
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "AssistantApi.dll"]
