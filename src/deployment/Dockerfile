# Build context is src/ (see compose.yaml). One build stage publishes every host; each host has its own target.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY backend/ backend/
RUN dotnet publish backend/EventDrivenDesign.Gateway/EventDrivenDesign.Gateway.csproj -c Release -o /out/gateway
# Tasks 10, 13 and 14 add the consumer, api and dashboard publish steps and targets below.

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS gateway
ENV DOTNET_gcServer=0
WORKDIR /app
COPY --from=build /out/gateway .
ENTRYPOINT ["dotnet", "EventDrivenDesign.Gateway.dll"]
