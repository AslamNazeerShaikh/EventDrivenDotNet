#!/bin/sh
# Creates the plant topics (doc 00 §4). Idempotent: safe to run on every `up`.
set -eu
BOOTSTRAP="${BOOTSTRAP:-plant-kafka:9092}"
KT=/opt/kafka/bin/kafka-topics.sh

$KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic machine.readings --partitions 12 --replication-factor 1 --config retention.ms=10800000
$KT --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic machine.alarms --partitions 3 --replication-factor 1 --config retention.ms=10800000
