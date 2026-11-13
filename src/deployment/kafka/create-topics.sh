#!/bin/sh
# Creates the plant topics (doc 00 §4). Idempotent: safe to run on every `up`.
set -eu
BOOTSTRAP="${BOOTSTRAP:-plant-kafka:9092}"
KT=/opt/kafka/bin/kafka-topics.sh

# Readings: 8 h covers the exercise's 6-hour central outage (about 450 MB per hour). Alarms are kept for good:
# a few KB per hour, and a gateway rebuilding its alarm counter from the log must find each machine's newest one.
$KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic machine.readings --partitions 12 --replication-factor 1 --config retention.ms=28800000
$KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic machine.alarms --partitions 3 --replication-factor 1 --config retention.ms=-1
# Topics created by an older version keep their settings, so apply them again (idempotent).
KC=/opt/kafka/bin/kafka-configs.sh
$KC --bootstrap-server "$BOOTSTRAP" --alter --entity-type topics --entity-name machine.readings --add-config retention.ms=28800000
$KC --bootstrap-server "$BOOTSTRAP" --alter --entity-type topics --entity-name machine.alarms --add-config retention.ms=-1
# The lease topics (doc 02 §4.3): one partition, never written to.
for SHOP in prs bdy pnt mch asm; do
  $KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic "gateway.lease.$SHOP" --partitions 1 --replication-factor 1
done
