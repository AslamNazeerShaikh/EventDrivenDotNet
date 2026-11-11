# 1) .NET build. SDK 11 image; hosts (Api/Producer/Consumer) land in Tasks 2-4.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS dotnet
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props EventDrivenDesign.slnx global.json ./
COPY src/backend/EventDrivenDesign.Shared/*.csproj src/backend/EventDrivenDesign.Shared/
COPY src/backend/EventDrivenDesign.Contracts/*.csproj src/backend/EventDrivenDesign.Contracts/
COPY src/backend/EventDrivenDesign.Messaging/*.csproj src/backend/EventDrivenDesign.Messaging/
COPY src/backend/EventDrivenDesign.Domain/*.csproj src/backend/EventDrivenDesign.Domain/
COPY src/backend/EventDrivenDesign.Application/*.csproj src/backend/EventDrivenDesign.Application/
COPY src/backend/EventDrivenDesign.Persistence/*.csproj src/backend/EventDrivenDesign.Persistence/
COPY src/backend/EventDrivenDesign.Infrastructure/*.csproj src/backend/EventDrivenDesign.Infrastructure/
COPY tests/EventDrivenDesign.Tests/*.csproj tests/EventDrivenDesign.Tests/
RUN dotnet restore EventDrivenDesign.slnx
COPY . .
# shortcut: publish Api/Producer/Consumer hosts to /app/{api,producer,consumer} once those projects land.
RUN for lib in Shared Contracts Messaging Domain Application Persistence Infrastructure; do \
      dotnet publish src/backend/EventDrivenDesign.$lib -c Release --no-restore -o /app/libs; \
    done

# 2) React build (bun, never npm/node).
FROM oven/bun:1 AS web
WORKDIR /web
COPY src/frontend/package.json src/frontend/bun.lock ./
RUN bun install
COPY src/frontend/ ./
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
