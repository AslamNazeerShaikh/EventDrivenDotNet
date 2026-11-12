#!/bin/sh
# Creates the plant topics (doc 00 §4). Idempotent: safe to run on every `up`.
set -eu
BOOTSTRAP="${BOOTSTRAP:-plant-kafka:9092}"
KT=/opt/kafka/bin/kafka-topics.sh

$KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic machine.readings --partitions 12 --replication-factor 1 --config retention.ms=10800000
$KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic machine.alarms --partitions 3 --replication-factor 1 --config retention.ms=10800000
# The lease topics (doc 02 §4.3): one partition, never written to.
for SHOP in prs bdy pnt mch asm; do
  $KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic "gateway.lease.$SHOP" --partitions 1 --replication-factor 1
done
