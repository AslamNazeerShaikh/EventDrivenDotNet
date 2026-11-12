CREATE TABLE readings (
  machine_id   text        NOT NULL,
  seq          bigint      NOT NULL,
  device_time  timestamptz NOT NULL,
  machine_type text        NOT NULL,
  shop         text        NOT NULL,
  signals      jsonb       NOT NULL,
  stored_at    timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (machine_id, seq)
);
CREATE INDEX ix_readings_device_time ON readings (device_time);

CREATE TABLE alarms (
  machine_id  text             NOT NULL,
  alarm_seq   bigint           NOT NULL,
  reading_seq bigint           NOT NULL,
  device_time timestamptz      NOT NULL,
  kind        text             NOT NULL,
  severity    text             NOT NULL,
  signal      text,
  value       double precision,
  threshold   double precision,
  stored_at   timestamptz      NOT NULL DEFAULT now(),
  PRIMARY KEY (machine_id, alarm_seq)
);

CREATE TABLE machine_progress (
  machine_id      text        PRIMARY KEY,
  stored          bigint      NOT NULL,
  max_seq         bigint      NOT NULL,
  max_device_time timestamptz NOT NULL
);

CREATE TABLE ingest_totals (
  stream    text   NOT NULL,          -- 'readings' or 'alarms'
  writer_id text   NOT NULL,          -- consumer-1, consumer-2: no shared hot row
  received  bigint NOT NULL DEFAULT 0,
  inserted  bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (stream, writer_id)
);

CREATE TABLE rejected_records (
  topic           text        NOT NULL,
  kafka_partition int         NOT NULL,
  kafka_offset    bigint      NOT NULL,
  raw             bytea       NOT NULL,
  error           text        NOT NULL,
  rejected_at     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (topic, kafka_partition, kafka_offset)
);
