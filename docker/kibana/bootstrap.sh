#!/usr/bin/env sh
set -eu

# Alpine image: install curl
if command -v apk >/dev/null 2>&1; then
  apk add --no-cache curl >/dev/null
fi

KIBANA_URL="${KIBANA_URL:-http://kibana:5601}"
SAVED_OBJECTS_FILE="${SAVED_OBJECTS_FILE:-/bootstrap/pv_saved_objects.ndjson}"

_echo() { printf "%s
" "[kibana-setup] $*"; }

_echo "Waiting for Kibana at $KIBANA_URL..."
for i in $(seq 1 180); do
  if curl -s "$KIBANA_URL/api/status" | grep -q '"overall".*"available"'; then
    break
  fi
  sleep 2
done

if ! curl -s "$KIBANA_URL/api/status" | grep -q '"overall".*"available"'; then
  _echo "Kibana did not become available in time."
  exit 1
fi

_echo "Importing saved objects from $SAVED_OBJECTS_FILE..."
HTTP_CODE=$(curl -s -o /tmp/import_resp.json -w "%{http_code}"   -X POST "$KIBANA_URL/api/saved_objects/_import?overwrite=true"   -H "kbn-xsrf: true"   -F file=@"$SAVED_OBJECTS_FILE")

if [ "$HTTP_CODE" -lt 200 ] || [ "$HTTP_CODE" -ge 300 ]; then
  _echo "Import failed (HTTP $HTTP_CODE). Response:"
  cat /tmp/import_resp.json || true
  exit 1
fi

_echo "Setting default data view to pv-filebeat..."
curl -s -o /dev/null -X POST "$KIBANA_URL/api/kibana/settings"   -H "kbn-xsrf: true"   -H "Content-Type: application/json"   -d '{"changes":{"defaultIndex":"pv-filebeat"}}' || true

_echo "Bootstrap completed."
