CREATE TABLE IF NOT EXISTS "doc"."raw" (
   "g_ts_week" TIMESTAMP GENERATED ALWAYS AS date_trunc('week', current_timestamp(3)),
   "insert_ts" TIMESTAMP GENERATED ALWAYS AS current_timestamp(3),
   "iothub_enqueuedtime" TIMESTAMP,
   "iothub_connection_device_id" string,
   "payload" OBJECT (IGNORED)
)
CLUSTERED INTO 1 SHARDS
PARTITIONED BY ("g_ts_week")

CREATE USER edgeingest WITH (password = 'p@ssword')

GRANT ALL ON SCHEMA doc to edgeingest