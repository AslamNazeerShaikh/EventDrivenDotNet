# Build context is src/ (see compose.yaml). One build stage publishes every host; each host has its own target.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY backend/ backend/
RUN dotnet publish backend/EventDrivenDesign.Gateway/EventDrivenDesign.Gateway.csproj -c Release -o /out/gateway
RUN dotnet publish backend/EventDrivenDesign.Consumer/EventDrivenDesign.Consumer.csproj -c Release -o /out/consumer
RUN dotnet publish backend/EventDrivenDesign.Api/EventDrivenDesign.Api.csproj -c Release -o /out/api

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

# The oven/bun image only builds the static site; it does not ship.
FROM oven/bun:1.4.4-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY frontend/ ./
RUN bun run build

FROM nginx:1.30-alpine-slim AS dashboard
COPY deployment/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=web /web/dist /usr/share/nginx/html
