#!/bin/bash
# Real-account test via PKCE for the Windows gateway in CI.
#
# PKCE uses the Office web Copilot client (c0ab8ce9...), not the FOCI
# "Microsoft Office" client (d3590ed6...) the device-code flow uses, so a
# Conditional Access policy that blocks the latter may still allow this
# one (verified on the Mac CI run 41283819042).
#
# Round trip (the runner cannot receive input mid-run, so it goes through
# a GitHub issue): the runner starts PKCE and posts the auth URL to an
# issue; the operator opens it in a browser, signs in, copies the full
# address-bar URL from the nativeclient landing page (it contains code=
# and state=), and posts it as a comment on the same issue; this script
# polls the comments and completes the callback. The pending login
# expires 10 minutes after the start, so the poll runs at most 9 minutes.
# The comment carrying the code is deleted right after use.
#
# The gateway is left RUNNING when this script ends: the CodeX
# tool-call step runs next against this same gateway (it needs the
# account this step just added), so this script writes the gateway PID
# to $RUNNER_TEMP/m365hub-real-account-pkce-win.pid and the CodeX
# script stops the gateway when it is done.
#
# Usage: scripts/test-real-account-pkce-win.sh (run from the repo root)
# Exit 0 only if the account is added and the chat returns real content.
set -u
BASE="http://127.0.0.1:8230"
TMP="${RUNNER_TEMP:-/tmp}"
JAR="$TMP/m365hub-real-win-cookies.txt"
SUMMARY="$TMP/m365hub-real-account-pkce-win-summary.txt"
DATA_DIR="$TMP/m365hub-real-account-pkce-win"
ADMIN_PASS="ci-test-pass-20261101"
REPO="ryc2077/M365Hub-win"
FAIL=0
: > "$SUMMARY"
PY="$(command -v python3 || command -v python || echo python3)"
# Fail loudly if there is no working python: a silent jget would fail
# every JSON check while the HTTP-code checks still pass, which looks
# like a gateway bug but is only a missing interpreter.
# Force UTF-8 for every python call in this script: Git Bash on the
# Windows runner runs under a C locale, so python's stdout defaults to
# ASCII and printing a Chinese chat reply raises UnicodeEncodeError,
# which makes jget return empty even though the content is there.
export PYTHONIOENCODING=utf-8
"$PY" -c "import sys; sys.exit(0)" 2>/dev/null \
  || { echo "FATAL: no working python ($PY) for the JSON checks"; exit 1; }

note() { echo "$1" | tee -a "$SUMMARY"; }
fail() { FAIL=1; note "FAIL: $1"; }
pass() { note "PASS: $1"; }
# Read one JSON value from a file. The path and the key expression travel
# as argv, never interpolated into the -c string: on Windows the temp dir
# is D:\a\_temp, and open('D:\a\_temp\r4.json') would turn \a into a bell
# character and fail every check while the HTTP-code checks still pass.
jget() { "$PY" -c 'import json,sys; d=json.load(open(sys.argv[1], encoding="utf-8")); v=eval("d"+sys.argv[2]); print(v if not isinstance(v,bool) else str(v).lower())' "$1" "$2" 2>/dev/null; }
gh_api() { curl -s -H "Authorization: Bearer ${GITHUB_TOKEN:?no GITHUB_TOKEN}" -H "Accept: application/vnd.github+json" "https://api.github.com/repos/$REPO$1" "${@:2}"; }

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
login_with() {
  curl -s -m 10 -c "$JAR" -b "$JAR" -X POST "$BASE/api/admin/login" \
    -H 'Content-Type: application/json' -d "{\"password\":\"$1\"}" -o "$TMP/r1.json" -w "%{http_code}"
}
CODE=$(login_with "$ADMIN_PASS")
if [ "$CODE" = "200" ] && [ "$(jget "$TMP/r1.json" "['ok']")" = "true" ]; then
  pass "admin login with the test password"
else
  curl -s -m 10 -X POST "$BASE/api/admin/login" \
    -H 'Content-Type: application/json' -d '{"password":"admin123"}' -o "$TMP/r1.json"
  if [ "$(jget "$TMP/r1.json" "['must_change_password']")" = "true" ]; then
    curl -s -m 10 -X POST "$BASE/api/admin/password" \
      -H 'Content-Type: application/json' \
      -d "{\"current\":\"admin123\",\"next\":\"$ADMIN_PASS\"}" -o "$TMP/r2.json" -w "%{http_code}" > "$TMP/r2.code"
    [ "$(cat "$TMP/r2.code")" = "200" ] && pass "changed default admin password" || fail "password change: $(cat "$TMP/r2.json")"
    CODE=$(login_with "$ADMIN_PASS")
    [ "$CODE" = "200" ] && pass "admin login with the test password" || fail "admin login: $(cat "$TMP/r1.json")"
  else
    fail "admin login with either password: $(cat "$TMP/r1.json")"
  fi
fi

echo "== accounts list before =="
curl -s -m 10 -b "$JAR" "$BASE/api/accounts" -o "$TMP/r3.json"
[ "$(jget "$TMP/r3.json" "['accounts']")" = "[]" ] \
  && pass "accounts list starts empty" || note "accounts before: $(cat "$TMP/r3.json")"

echo "== PKCE start =="
curl -s -m 30 -b "$JAR" -X POST "$BASE/api/auth/start" \
  -H 'Content-Type: application/json' -d '{"flow":"pkce"}' \
  -o "$TMP/r4.json" -w "%{http_code}" > "$TMP/r4.code"
[ "$(cat "$TMP/r4.code")" = "200" ] && pass "PKCE flow started" || fail "PKCE start: $(cat "$TMP/r4.json")"
AUTH_URL="$(jget "$TMP/r4.json" "['authUrl']")"
echo "PKCE-AUTH-URL: $AUTH_URL" | tee -a "$SUMMARY"
[ -n "$AUTH_URL" ] || fail "PKCE start returned no authUrl"

# Post the auth URL to an issue and wait for the callback URL as a comment.
ISSUE_NUM=""
if [ -n "${GITHUB_TOKEN:-}" ]; then
  ISSUE_BODY="Open this URL in a browser and sign in as copilot@wt3z38wld.onmicrosoft.com. After sign-in the browser lands on the nativeclient page; copy the FULL address-bar URL (it contains code= and state=) and post it as a comment on this issue, nothing else. The runner polls comments for up to 9 minutes (the pending login expires 10 minutes after the start).

AUTH-URL: $AUTH_URL"
  ISSUE_JSON=$("$PY" -c "import json,sys; print(json.dumps({'title':'Real-account test PKCE auth URL (Windows)','body':sys.argv[1]}))" "$ISSUE_BODY")
  ISSUE_NUM=$(gh_api "/issues" -X POST -d "$ISSUE_JSON" \
    | "$PY" -c "import json,sys; print(json.load(sys.stdin).get('number',''))" 2>/dev/null)
  echo "ISSUE: $ISSUE_NUM" | tee -a "$SUMMARY"
else
  fail "no GITHUB_TOKEN; the PKCE round trip needs it"
fi
[ -n "$ISSUE_NUM" ] || fail "issue creation failed"

echo "== poll issue comments for the callback URL (up to 9 minutes) =="
CALLBACK_URL=""
COMMENT_ID=""
for i in $(seq 1 27); do
  sleep 20
  FOUND=$(gh_api "/issues/$ISSUE_NUM/comments" \
    | "$PY" -c "
import json,sys
for c in json.load(sys.stdin):
    if 'code=' in (c.get('body') or ''):
        print(str(c['id']) + '\t' + c['body'])
        break
" 2>/dev/null)
  if [ -n "$FOUND" ]; then
    COMMENT_ID="${FOUND%%$'\t'*}"
    COMMENT_BODY="${FOUND#*$'\t'}"
    CALLBACK_URL=$(echo "$COMMENT_BODY" | grep -o 'https://[^[:space:]"]*code=[^[:space:]"]*' | head -n 1)
    [ -n "$CALLBACK_URL" ] || CALLBACK_URL="$COMMENT_BODY"
    echo "CALLBACK-URL found (comment $COMMENT_ID)" | tee -a "$SUMMARY"
    break
  fi
  echo "poll $i: no callback comment yet"
done
[ -n "$CALLBACK_URL" ] || fail "no callback URL comment appeared within 9 minutes"

echo "== PKCE callback =="
if [ -n "$CALLBACK_URL" ]; then
  # Build the JSON body in a variable first: nesting a double-quoted
  # python -c call inside "$(...)" inside curl's -d "..." breaks on some
  # shells (the dict braces get brace-expanded and python sees two
  # mangled fragments). This bit the Mac run 41283375197.
  CALLBACK_JSON=$("$PY" -c "import json,sys; print(json.dumps({'flow':'pkce','callbackUrl':sys.argv[1]}))" "$CALLBACK_URL")
  [ -n "$CALLBACK_JSON" ] || fail "could not build the callback JSON body"
  curl -s -m 60 -b "$JAR" -X POST "$BASE/api/auth/callback" \
    -H 'Content-Type: application/json' \
    -d "$CALLBACK_JSON" \
    -o "$TMP/r5.json" -w "%{http_code}" > "$TMP/r5.code"
  if [ "$(cat "$TMP/r5.code")" = "200" ]; then
    echo "ACCOUNT-ADDED: $(cat "$TMP/r5.json")" | tee -a "$SUMMARY"
    pass "PKCE callback completed (account added)"
  else
    fail "PKCE callback http $(cat "$TMP/r5.code"): $(cat "$TMP/r5.json" | head -c 500)"
  fi
  # Delete the comment carrying the one-shot code, best effort.
  if [ -n "$COMMENT_ID" ] && [ -n "${GITHUB_TOKEN:-}" ]; then
    gh_api "/issues/comments/$COMMENT_ID" -X DELETE >/dev/null 2>&1 || true
  fi
fi

echo "== accounts list after =="
curl -s -m 10 -b "$JAR" "$BASE/api/accounts" -o "$TMP/r6.json"
ACCOUNTS="$(jget "$TMP/r6.json" "['accounts']")"
echo "accounts: $ACCOUNTS" | tee -a "$SUMMARY"
echo "$ACCOUNTS" | grep -q "copilot@wt3z38wld.onmicrosoft.com" \
  && pass "account copilot@wt3z38wld.onmicrosoft.com is listed" \
  || fail "expected account not in list: $ACCOUNTS"
curl -s -m 10 -b "$JAR" "$BASE/api/auth/status" -o "$TMP/r7.json"
[ "$(jget "$TMP/r7.json" "['count']")" = "1" ] \
  && pass "auth status count=1" || fail "auth status: $(cat "$TMP/r7.json")"

echo "== real chat with the account =="
# Write the request body with python (guaranteed UTF-8) and send the file:
# passing the Chinese message through the Git Bash command line mangled
# it into "??,??????????" before it reached the gateway (run 13), while
# the identical message worked from macOS.
"$PY" -c 'import json,sys; open(sys.argv[1],"w",encoding="utf-8").write(json.dumps({"model":"gpt-5.6-reasoning","messages":[{"role":"user","content":"你好，请用一句话介绍你自己"}],"attachments":[]}, ensure_ascii=False))' "$TMP/r8body.json"
curl -s -m 180 -b "$JAR" -X POST "$BASE/api/chat" \
  -H 'Content-Type: application/json' \
  --data-binary "@$TMP/r8body.json" \
  -o "$TMP/r8.json" -w "%{http_code}" > "$TMP/r8.code"
[ "$(cat "$TMP/r8.code")" = "200" ] && pass "chat returned http 200" || fail "chat http $(cat "$TMP/r8.code"): $(cat "$TMP/r8.json" | head -c 500)"
CONTENT="$(jget "$TMP/r8.json" "['content']")"
if [ -n "$CONTENT" ] && [ "$CONTENT" != "None" ]; then
  echo "CHAT-REPLY: $(echo "$CONTENT" | head -c 500)" | tee -a "$SUMMARY"
  pass "chat returned real content"
else
  fail "chat content empty: $(cat "$TMP/r8.json" | head -c 500)"
fi

echo "== hand the gateway off to the CodeX step + close the issue =="
# Do NOT kill the gateway here: the CodeX tool-call step runs next
# against this same gateway and its freshly added account. Record the
# PID so that step can stop the gateway when it is done.
echo "$GW_PID" > "$TMP/m365hub-real-account-pkce-win.pid"
pass "gateway left running for the CodeX step (pid $GW_PID)"
if [ -n "$ISSUE_NUM" ] && [ -n "${GITHUB_TOKEN:-}" ]; then
  gh_api "/issues/$ISSUE_NUM" -X PATCH -d '{"state":"closed"}' >/dev/null 2>&1 || true
fi

echo "== summary =="
cat "$SUMMARY"
if [ "$FAIL" = "0" ]; then echo "REAL-ACCOUNT-PKCE-TEST: PASS"; exit 0
else echo "REAL-ACCOUNT-PKCE-TEST: FAIL"; exit 1; fi
