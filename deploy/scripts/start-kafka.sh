#!/usr/bin/env bash
# format storage on first boot only, then run in the foreground
set -euo pipefail
CFG=/opt/kafka/config/edd.properties
if [ ! -f /data/kafka/meta.properties ]; then
  /opt/kafka/bin/kafka-storage.sh format --standalone \
    -t "$(/opt/kafka/bin/kafka-storage.sh random-uuid)" -c "$CFG"
fi
exec /opt/kafka/bin/kafka-server-start.sh "$CFG"
