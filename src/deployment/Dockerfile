# Build context is src/ (see compose.yaml). One build stage publishes every host; each host has its own target.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY backend/ backend/
RUN dotnet publish backend/EventDrivenDesign.Gateway/EventDrivenDesign.Gateway.csproj -c Release -o /out/gateway
RUN dotnet publish backend/EventDrivenDesign.Consumer/EventDrivenDesign.Consumer.csproj -c Release -o /out/consumer
RUN dotnet publish backend/EventDrivenDesign.Api/EventDrivenDesign.Api.csproj -c Release -o /out/api
# Task 14 adds the dashboard publish step and target below.

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS gateway
ENV DOTNET_gcServer=0
WORKDIR /app
COPY --from=build /out/gateway .
ENTRYPOINT ["dotnet", "EventDrivenDesign.Gateway.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS consumer
ENV DOTNET_gcServer=0
WORKDIR /app
COPY --from=build /out/consumer .
ENTRYPOINT ["dotnet", "EventDrivenDesign.Consumer.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS api
ENV DOTNET_gcServer=0
WORKDIR /app
COPY --from=build /out/api .
ENTRYPOINT ["dotnet", "EventDrivenDesign.Api.dll"]
