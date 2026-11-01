#!/bin/bash
# Account-add + chat test for the Windows gateway, through the exact
# endpoints the WinUI app calls: admin login (forced password change),
# accounts list, PKCE/device start, bogus callback rejection, and chat
# with zero accounts. Completing the Microsoft sign-in needs a human in
# the browser, so this proves the flows start and fail cleanly; it cannot
# mint an account by itself.
# Usage: scripts/test-account-chat-win.sh (run from the repo root)
# Exit 0 only if every check passes.
set -u
BASE="http://127.0.0.1:8230"
TMP="${RUNNER_TEMP:-/tmp}"
JAR="$TMP/m365hub-win-cookies.txt"
SUMMARY="$TMP/m365hub-account-chat-win-summary.txt"
DATA_DIR="$TMP/m365hub-account-chat-win"
ADMIN_PASS="ci-test-pass-20261101"
FAIL=0
: > "$SUMMARY"
PY="$(command -v python3 || command -v python || echo python3)"
# Fail loudly if there is no working python: a silent jget would fail
# every JSON check while the HTTP-code checks still pass, which looks
# like a gateway bug but is only a missing interpreter.
"$PY" -c "import sys; sys.exit(0)" 2>/dev/null \
  || { echo "FATAL: no working python ($PY) for the JSON checks"; exit 1; }

note() { echo "$1" | tee -a "$SUMMARY"; }
fail() { FAIL=1; note "FAIL: $1"; }
pass() { note "PASS: $1"; }
# Read one JSON value from a file. The path and the key expression travel
# as argv, never interpolated into the -c string: on Windows the temp dir
# is D:\a\_temp, and open('D:\a\_temp\w1.json') would turn \a into a bell
# character and fail every check while the HTTP-code checks still pass.
jget() { "$PY" -c 'import json,sys; d=json.load(open(sys.argv[1], encoding="utf-8")); v=eval("d"+sys.argv[2]); print(v if not isinstance(v,bool) else str(v).lower())' "$1" "$2" 2>/dev/null; }

echo "== start the gateway (fresh data dir) =="
rm -f "$JAR"
rm -rf "$DATA_DIR"
mkdir -p "$DATA_DIR"
M365_DATA_DIR="$DATA_DIR" M365_LISTEN="127.0.0.1:8230" \
  gateway/m365hub.exe serve >"$DATA_DIR/serve.log" 2>&1 &
GW_PID=$!
echo "gateway pid: $GW_PID"
UP=0
for i in $(seq 1 30); do
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -m 2 "$BASE/api/admin/session" || echo "000")
  if [ "$CODE" = "200" ]; then UP=1; break; fi
  sleep 1
done
[ "$UP" = "1" ] && pass "gateway up (session answers)" || fail "gateway never answered"

echo "== admin login =="
curl -s -m 10 -c "$JAR" -b "$JAR" -X POST "$BASE/api/admin/login" \
  -H 'Content-Type: application/json' -d '{"password":"admin123"}' \
  -o "$TMP/w1.json" -w "%{http_code}" > "$TMP/w1.code"
[ "$(cat "$TMP/w1.code")" = "200" ] && pass "login with default password (http 200)" || fail "login with default password: $(cat "$TMP/w1.json")"
[ "$(jget "$TMP/w1.json" "['must_change_password']")" = "true" ] \
  && pass "default password forces a change" || fail "no must_change_password: $(cat "$TMP/w1.json")"
curl -s -m 10 -X POST "$BASE/api/admin/password" \
  -H 'Content-Type: application/json' \
  -d "{\"current\":\"admin123\",\"next\":\"$ADMIN_PASS\"}" \
  -o "$TMP/w2.json" -w "%{http_code}" > "$TMP/w2.code"
[ "$(cat "$TMP/w2.code")" = "200" ] && pass "change admin password (http 200)" || fail "password change: $(cat "$TMP/w2.json")"
curl -s -m 10 -c "$JAR" -b "$JAR" -X POST "$BASE/api/admin/login" \
  -H 'Content-Type: application/json' -d "{\"password\":\"$ADMIN_PASS\"}" \
  -o "$TMP/w3.json" -w "%{http_code}" > "$TMP/w3.code"
[ "$(cat "$TMP/w3.code")" = "200" ] && [ "$(jget "$TMP/w3.json" "['ok']")" = "true" ] \
  && pass "login with new password (http 200)" || fail "login with new password: $(cat "$TMP/w3.json")"
[ -s "$JAR" ] && pass "session cookie issued" || fail "no session cookie"
curl -s -m 10 -b "$JAR" "$BASE/api/admin/session" -o "$TMP/w4.json"
[ "$(jget "$TMP/w4.json" "['loggedIn']")" = "true" ] \
  && pass "session loggedIn=true" || fail "session: $(cat "$TMP/w4.json")"

echo "== accounts =="
curl -s -m 10 -b "$JAR" "$BASE/api/accounts" -o "$TMP/w5.json" -w "%{http_code}" > "$TMP/w5.code"
[ "$(cat "$TMP/w5.code")" = "200" ] && pass "GET /api/accounts (http 200)" || fail "accounts: $(cat "$TMP/w5.json")"
[ "$(jget "$TMP/w5.json" "['accounts']")" = "[]" ] \
  && pass "accounts list is empty" || fail "accounts not empty: $(cat "$TMP/w5.json")"

echo "== auth start =="
curl -s -m 30 -b "$JAR" -X POST "$BASE/api/auth/start" \
  -H 'Content-Type: application/json' -d '{"flow":"pkce"}' \
  -o "$TMP/w6.json" -w "%{http_code}" > "$TMP/w6.code"
[ "$(cat "$TMP/w6.code")" = "200" ] && pass "POST /api/auth/start pkce (http 200)" || fail "PKCE start: $(cat "$TMP/w6.json")"
AUTH_URL="$(jget "$TMP/w6.json" "['authUrl']")"
echo "$AUTH_URL" | grep -q "login.microsoftonline.com" && echo "$AUTH_URL" | grep -q "code_challenge" \
  && pass "PKCE auth URL is a Microsoft login URL with a code challenge" \
  || fail "PKCE auth URL bad: $AUTH_URL"
curl -s -m 30 -b "$JAR" -X POST "$BASE/api/auth/start" \
  -H 'Content-Type: application/json' -d '{"flow":"device"}' \
  -o "$TMP/w7.json" -w "%{http_code}" > "$TMP/w7.code"
[ "$(cat "$TMP/w7.code")" = "200" ] && pass "POST /api/auth/start device (http 200)" || fail "device start: $(cat "$TMP/w7.json")"
[ -n "$(jget "$TMP/w7.json" "['userCode']")" ] \
  && pass "device flow returned a user code" || fail "no userCode: $(cat "$TMP/w7.json")"

echo "== bogus callback =="
curl -s -m 10 -b "$JAR" -X POST "$BASE/api/auth/callback" \
  -H 'Content-Type: application/json' \
  -d '{"flow":"pkce","callbackUrl":"https://login.microsoftonline.com/common/oauth2/nativeclient?code=bogus&state=bogus"}' \
  -o "$TMP/w8.json" -w "%{http_code}" > "$TMP/w8.code"
[ "$(cat "$TMP/w8.code")" = "400" ] && pass "POST /api/auth/callback (bogus) (http 400)" || fail "bogus callback: $(cat "$TMP/w8.json")"
[ "$(jget "$TMP/w8.json" "['error']['type']")" = "auth_failed" ] \
  && pass "bogus callback -> auth_failed (pkce verifier missing)" \
  || fail "bogus callback error type: $(cat "$TMP/w8.json")"

echo "== auth status + chat with no accounts =="
curl -s -m 10 -b "$JAR" "$BASE/api/auth/status" -o "$TMP/w9.json"
[ "$(jget "$TMP/w9.json" "['count']")" = "0" ] \
  && pass "auth status count=0" || fail "auth status: $(cat "$TMP/w9.json")"
curl -s -m 60 -b "$JAR" -X POST "$BASE/api/chat" \
  -H 'Content-Type: application/json' \
  -d '{"model":"gpt-6.1-sol-reasoning","messages":[{"role":"user","content":"hello"}],"attachments":[]}' \
  -o "$TMP/w10.json" -w "%{http_code}" > "$TMP/w10.code"
[ "$(cat "$TMP/w10.code")" = "502" ] && pass "POST /api/chat (no accounts) (http 502)" || fail "chat: $(cat "$TMP/w10.json")"
[ "$(jget "$TMP/w10.json" "['error']['type']")" = "upstream_error" ] \
  && pass "chat -> 502 upstream_error, no healthy account (wired, fails gracefully)" \
  || fail "chat error type: $(cat "$TMP/w10.json")"

echo "== stop the gateway =="
kill "$GW_PID" 2>/dev/null || taskkill //F //PID "$GW_PID" 2>/dev/null || true
sleep 2

echo "== summary =="
cat "$SUMMARY"
if [ "$FAIL" = "0" ]; then echo "ACCOUNT-CHAT-TEST: PASS"; exit 0
else echo "ACCOUNT-CHAT-TEST: FAIL"; exit 1; fi
