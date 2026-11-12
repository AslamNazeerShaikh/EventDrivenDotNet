# Build from repo root with src as context: docker build -f src/Dockerfile src
# 1) .NET build. SDK 11 image; hosts (Api/Producer/Consumer) land in Tasks 2-4.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS dotnet
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props EventDrivenDesign.slnx global.json ./
COPY backend/EventDrivenDesign.Shared/*.csproj backend/EventDrivenDesign.Shared/
COPY backend/EventDrivenDesign.Contracts/*.csproj backend/EventDrivenDesign.Contracts/
COPY backend/EventDrivenDesign.Messaging/*.csproj backend/EventDrivenDesign.Messaging/
COPY backend/EventDrivenDesign.Domain/*.csproj backend/EventDrivenDesign.Domain/
COPY backend/EventDrivenDesign.Application/*.csproj backend/EventDrivenDesign.Application/
COPY backend/EventDrivenDesign.Persistence/*.csproj backend/EventDrivenDesign.Persistence/
COPY backend/EventDrivenDesign.Infrastructure/*.csproj backend/EventDrivenDesign.Infrastructure/
COPY tests/EventDrivenDesign.Tests/*.csproj tests/EventDrivenDesign.Tests/
RUN dotnet restore EventDrivenDesign.slnx
COPY . .
# shortcut: publish Api/Producer/Consumer hosts to /app/{api,producer,consumer} once those projects land.
RUN for lib in Shared Contracts Messaging Domain Application Persistence Infrastructure; do \
      dotnet publish backend/EventDrivenDesign.$lib -c Release --no-restore -o /app/libs; \
    done

# 2) React build (bun, never npm/node).
FROM oven/bun:1 AS web
WORKDIR /web
COPY frontend/package.json frontend/bun.lock ./
RUN bun install
COPY frontend/ ./
RUN bun run build

# 3) Kafka binaries and a JRE, taken from official images.
FROM apache/kafka:4.4.0 AS kafka
FROM eclipse-temurin:21-jre AS jre

# 4) Runtime: default (apt-based) ASP.NET image, not chiseled.
# No VOLUME: data is ephemeral (shortcut: add named volume when persistence matters).
# HTTP only: no TLS, no https redirection anywhere.
FROM mcr.microsoft.com/dotnet/aspnet:11.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends supervisor nginx curl \
 && rm -f /etc/nginx/sites-enabled/default \
 && rm -rf /var/lib/apt/lists/*
COPY --from=jre   /opt/java/openjdk /opt/java/openjdk
COPY --from=kafka /opt/kafka        /opt/kafka
ENV JAVA_HOME=/opt/java/openjdk PATH="$PATH:/opt/java/openjdk/bin"
COPY --from=dotnet /app/libs /app/libs
COPY --from=web /web/dist /usr/share/nginx/html
COPY deploy/supervisord.conf   /etc/supervisor/conf.d/edd.conf
COPY deploy/nginx.conf         /etc/nginx/conf.d/default.conf
COPY deploy/kafka.properties   /opt/kafka/config/edd.properties
COPY deploy/scripts/           /scripts/
RUN chmod +x /scripts/*.sh
EXPOSE 3000 8080 9092
HEALTHCHECK --interval=10s CMD curl -fs http://localhost:8080/healthz || exit 1
ENTRYPOINT ["/usr/bin/supervisord", "-n", "-c", "/etc/supervisor/supervisord.conf"]
