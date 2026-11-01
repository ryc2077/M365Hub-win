#!/bin/bash
# CodeX tool-call test for the Windows gateway in CI.
#
# Runs after the Real account test (PKCE) step, against the SAME gateway
# the PKCE script started: it leaves the gateway running and writes its
# PID to $RUNNER_TEMP/m365hub-real-account-pkce-win.pid, and this script
# kills the gateway when it is done. The PKCE step has already added the
# real Microsoft 365 account, so the gateway has a healthy account for
# Codex to call through. (Same shape as the sandbox verification of
# 2026-10-26, where Codex created hello.txt, appended a line and cat it.)
#
# Flow: confirm the gateway + account are there -> admin login -> mint a
# /v1 API key -> npm install the Codex CLI -> write ~/.codex/config.toml
# (m365hub provider, wire_api=responses, model gpt-4-auto) -> two
# `codex exec` rounds: create hello.txt with one exact line, then append
# a second line and cat it. Both file contents are verified on disk, so
# a model merely claiming it did the work fails the test.
#
# Usage: scripts/test-codex-win.sh (run from the repo root)
# Exit 0 only if both tool-call rounds really wrote the files.
#
# YOLO mode (user instruction; run 19 was cancelled before its PKCE
# step). Run codex with --dangerously-bypass-approvals-and-sandbox:
# approvals never and sandbox danger-full-access. The workspace-write
# sandbox does not work on the Windows runner (run 18: -s
# workspace-write left it read-only, writes "blocked by policy"; run
# 19's config sandbox_mode was never given a chance). The runner is an
# ephemeral throwaway VM with no user data, so YOLO is contained here.
# Do NOT combine this flag with -c approval_policy or -s - the flag
# already sets both knobs. Do NOT use --approve-for-me here - its
# guardian review fails against this gateway (run 17).
set -u
BASE="http://127.0.0.1:8230"
TMP="${RUNNER_TEMP:-/tmp}"
JAR="$TMP/m365hub-codex-win-cookies.txt"
SUMMARY="$TMP/m365hub-codex-win-summary.txt"
DATA_DIR="$TMP/m365hub-real-account-pkce-win"
PID_FILE="$TMP/m365hub-real-account-pkce-win.pid"
ADMIN_PASS="ci-test-pass-20261101"
WORKDIR="$TMP/codex-win-test"
FAIL=0
: > "$SUMMARY"
PY="$(command -v python3 || command -v python || echo python3)"
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

# The PKCE script left the gateway running; this script owns it now and
# kills it on the way out, success or failure.
GW_PID="$(cat "$PID_FILE" 2>/dev/null || echo "")"
stop_gateway() {
  if [ -n "$GW_PID" ]; then kill "$GW_PID" 2>/dev/null || taskkill //F //PID "$GW_PID" 2>/dev/null || true; fi
}
trap stop_gateway EXIT

echo "== gateway still up (handed off by the PKCE step) =="
[ -n "$GW_PID" ] || fail "no gateway PID file (PKCE step did not hand off)"
CODE=$(curl -s -o /dev/null -w "%{http_code}" -m 5 "$BASE/api/admin/session" || echo "000")
[ "$CODE" = "200" ] && pass "gateway up (session answers)" || fail "gateway not answering: http $CODE"

echo "== admin login + mint a /v1 API key =="
rm -f "$JAR"
CODE=$(curl -s -m 10 -c "$JAR" -b "$JAR" -X POST "$BASE/api/admin/login" \
  -H 'Content-Type: application/json' -d "{\"password\":\"$ADMIN_PASS\"}" \
  -o "$TMP/c1.json" -w "%{http_code}")
[ "$CODE" = "200" ] && [ "$(jget "$TMP/c1.json" "['ok']")" = "true" ] \
  && pass "admin login" || fail "admin login: $(cat "$TMP/c1.json" | head -c 300)"
# The PKCE step's account must still be there, or Codex has nothing to
# call through.
curl -s -m 10 -b "$JAR" "$BASE/api/auth/status" -o "$TMP/c3.json"
[ "$(jget "$TMP/c3.json" "['count']")" = "1" ] \
  && pass "auth status count=1" || fail "auth status: $(cat "$TMP/c3.json" | head -c 300)"
KEY_JSON=$("$PY" -c "import json,sys; print(json.dumps({'name':'codex-win-ci'}))")
curl -s -m 10 -b "$JAR" -X POST "$BASE/api/admin/keys" \
  -H 'Content-Type: application/json' -d "$KEY_JSON" \
  -o "$TMP/c2.json" -w "%{http_code}" > "$TMP/c2.code"
API_KEY="$(jget "$TMP/c2.json" "['plaintext']")"
if [ "$(cat "$TMP/c2.code")" = "200" ] && [ -n "$API_KEY" ] && [ "$API_KEY" != "None" ]; then
  pass "API key minted"
else
  fail "API key mint failed: $(cat "$TMP/c2.json" | head -c 300)"
fi

echo "== install Codex CLI =="
command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1 \
  || fail "node/npm not on PATH"
# npm's global bin dir is on PATH on the runner; if codex is still not
# found after install, fall back to the %APPDATA%\npm location.
if ! command -v codex >/dev/null 2>&1; then
  npm install -g @openai/codex >"$TMP/npm-codex.log" 2>&1 \
    && pass "npm install -g @openai/codex" || { fail "npm install failed"; tail -n 20 "$TMP/npm-codex.log" | tee -a "$SUMMARY"; }
fi
if ! command -v codex >/dev/null 2>&1; then
  NPM_BIN="$(npm config get prefix 2>/dev/null)/codex"
  [ -x "$NPM_BIN" ] && export PATH="$PATH:$(dirname "$NPM_BIN")"
fi
CODEX_VER="$(codex --version 2>&1 | head -n 1)"
[ -n "$CODEX_VER" ] && pass "codex $CODEX_VER" || fail "codex binary not found after install"
# YOLO mode: the flag sets both knobs (approvals never, sandbox
# danger-full-access). Verify it exists in this codex build instead of
# guessing.
HELP="$(codex exec --help 2>&1 || true)"
APPROVE=""
if echo "$HELP" | grep -q "dangerously-bypass-approvals-and-sandbox"; then APPROVE="--dangerously-bypass-approvals-and-sandbox";
else fail "codex exec has no --dangerously-bypass-approvals-and-sandbox flag"; fi
[ -n "$APPROVE" ] && pass "approval mode: YOLO (--dangerously-bypass-approvals-and-sandbox)"
SKIP=""
echo "$HELP" | grep -q "skip-git-repo-check" && SKIP="--skip-git-repo-check"

echo "== Codex config (m365hub provider) =="
mkdir -p "$HOME/.codex"
cat > "$HOME/.codex/config.toml" <<'EOF'
model_provider = "m365hub"
model = "gpt-4-auto"
[model_providers.m365hub]
name = "m365hub"
base_url = "http://127.0.0.1:8230/v1"
env_key = "M365HUB_API_KEY"
wire_api = "responses"
EOF
export M365HUB_API_KEY="$API_KEY"
pass "codex config written ($HOME/.codex/config.toml)"

echo "== CodeX tool call, round 1: create hello.txt =="
rm -rf "$WORKDIR"; mkdir -p "$WORKDIR"
cd "$WORKDIR" && git init -q 2>/dev/null || true
# English prompt on purpose: a Chinese prompt through the Git Bash
# command line was mangled into question marks on the Windows runner
# (run 13), while the same Chinese message worked from macOS.
timeout 300 codex exec $APPROVE $SKIP "Create a file named hello.txt in the current directory containing exactly one line: codex-via-m365hub-win-ok. Do not write anything else into the file." < /dev/null > "$TMP/codex1.log" 2>&1
echo "codex round 1 exit: $?" | tee -a "$SUMMARY"
# The session header must show approval: never, or the guardian path
# is back and the run will fail the same way run 17 did.
grep -q "approval: never" "$TMP/codex1.log" \
  && pass "codex ran with approval: never" \
  || fail "codex did not run with approval: never (guardian path may be back)"
# YOLO sets the sandbox to danger-full-access; if the header shows
# anything else, the flag did not take effect.
grep -q "sandbox: danger-full-access" "$TMP/codex1.log" \
  && pass "codex ran with sandbox: danger-full-access (YOLO)" \
  || fail "codex sandbox is not danger-full-access (YOLO flag may not have taken effect)"
if [ -f "$WORKDIR/hello.txt" ]; then
  CONTENT="$(cat "$WORKDIR/hello.txt")"
  echo "hello.txt: [$CONTENT]" | tee -a "$SUMMARY"
  [ "$CONTENT" = "codex-via-m365hub-win-ok" ] \
    && pass "round 1: hello.txt created with the exact line" \
    || fail "round 1: hello.txt content wrong"
else
  fail "round 1: hello.txt was not created"
  tail -n 30 "$TMP/codex1.log" | tee -a "$SUMMARY"
fi

echo "== CodeX tool call, round 2: append + cat =="
cd "$WORKDIR"
timeout 300 codex exec $APPROVE $SKIP "Append a second line, second-line, to hello.txt in the current directory, then print the file contents with cat." < /dev/null > "$TMP/codex2.log" 2>&1
echo "codex round 2 exit: $?" | tee -a "$SUMMARY"
if grep -qx "codex-via-m365hub-win-ok" "$WORKDIR/hello.txt" && grep -qx "second-line" "$WORKDIR/hello.txt"; then
  pass "round 2: second line appended, both lines present"
  echo "hello.txt now:" | tee -a "$SUMMARY"
  cat "$WORKDIR/hello.txt" | tee -a "$SUMMARY"
else
  fail "round 2: hello.txt does not have both lines"
  cat "$WORKDIR/hello.txt" 2>&1 | tee -a "$SUMMARY"
  tail -n 20 "$TMP/codex2.log" | tee -a "$SUMMARY"
fi

echo "== summary =="
cat "$SUMMARY"
if [ "$FAIL" = "0" ]; then echo "CODEX-WIN-TEST: PASS"; exit 0
else echo "CODEX-WIN-TEST: FAIL"; exit 1; fi
