#!/bin/bash
# Cold-start the Windows gateway exactly the way the WinUI app does, and
# prove it answers /api/admin/session. Run from the repo root.
# Usage: scripts/test-gateway-win.sh
# Exit 0 only if the session endpoint answers within 30 seconds.
set -u
BIN="gateway/m365hub.exe"
DATA_DIR="${RUNNER_TEMP:-/tmp}/m365hub-gateway-test"
LOG="$DATA_DIR/serve-test.log"

echo "== binary =="
ls -la "$BIN" || exit 1
"$BIN" version 2>&1 | head -n 3 || true

echo "== cold start (fresh data dir, like a first launch) =="
rm -rf "$DATA_DIR"
mkdir -p "$DATA_DIR"
M365_DATA_DIR="$DATA_DIR" M365_LISTEN="127.0.0.1:8230" \
  "$BIN" serve >"$LOG" 2>&1 &
GW_PID=$!
echo "gateway pid: $GW_PID"

echo "== poll /api/admin/session (any HTTP response counts, like the app) =="
UP=0
for i in $(seq 1 30); do
  CODE=$(curl -s -o /tmp/session-body.json -w "%{http_code}" -m 2 \
    http://127.0.0.1:8230/api/admin/session || echo "curl-failed")
  echo "attempt $i: http=$CODE"
  if [ "$CODE" != "curl-failed" ] && [ "$CODE" != "000" ] && [ -n "$CODE" ]; then
    UP=1
    echo "session body:"; cat /tmp/session-body.json; echo
    break
  fi
  sleep 1
done

echo "== gateway process state =="
if kill -0 "$GW_PID" 2>/dev/null; then
  echo "still running (pid $GW_PID)"
else
  wait "$GW_PID" 2>/dev/null; echo "exited with code $?"
fi

echo "== gateway log =="
cat "$LOG"

kill "$GW_PID" 2>/dev/null || taskkill //F //PID "$GW_PID" 2>/dev/null || true
sleep 2
if [ "$UP" = "1" ]; then
  echo "GATEWAY-TEST: PASS"
  exit 0
else
  echo "GATEWAY-TEST: FAIL - session endpoint never answered"
  exit 1
fi
