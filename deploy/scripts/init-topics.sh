#!/usr/bin/env bash
# wait for the broker, create topics idempotently, exit 0
set -euo pipefail
T=/opt/kafka/bin/kafka-topics.sh; B=localhost:9092
until $T --bootstrap-server $B --list >/dev/null 2>&1; do sleep 1; done
$T --bootstrap-server $B --create --if-not-exists --topic demo.events          --partitions 6 --replication-factor 1 --config retention.ms=86400000
$T --bootstrap-server $B --create --if-not-exists --topic demo.events.dlq      --partitions 1 --replication-factor 1 --config retention.ms=604800000
$T --bootstrap-server $B --create --if-not-exists --topic control.commands     --partitions 1 --replication-factor 1 --config cleanup.policy=compact
$T --bootstrap-server $B --create --if-not-exists --topic control.acks         --partitions 1 --replication-factor 1 --config retention.ms=3600000
$T --bootstrap-server $B --create --if-not-exists --topic telemetry.heartbeats --partitions 1 --replication-factor 1 --config retention.ms=3600000
