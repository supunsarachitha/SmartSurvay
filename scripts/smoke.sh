#!/usr/bin/env bash
# ---------------------------------------------------------------------------------------------
# SmartSurvey smoke test: starts the web app on SQLite with demo data, logs in (optional) and
# fetches pages, reporting HTTP status and server-side render errors found in the HTML.
#
# Usage:  scripts/smoke.sh [--port 5200] [--user admin|user|superadmin|acme|anon] [--no-build] <path> [<path> ...]
# Example: scripts/smoke.sh --port 5201 --user admin / /admin /admin/surveys /faq
#          scripts/smoke.sh --user superadmin /system /system/workspaces /system/accounts
# Users: admin/user = the "Default workspace" demo accounts, acme = admin of the second demo workspace,
#        superadmin = the seeded super admin.
#
# Blazor prerenders interactive pages on the server, so a 200 response without error markers means
# OnInitialized(Async) ran successfully against real services and a real (SQLite) database.
# Exit code: 0 when every page returned 2xx/3xx without error markers, 1 otherwise.
# ---------------------------------------------------------------------------------------------
set -u
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT=5200
USER_KIND=anon
BUILD=1
PATHS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --user) USER_KIND="$2"; shift 2 ;;
    --no-build) BUILD=0; shift ;;
    *) PATHS+=("$1"); shift ;;
  esac
done
[ ${#PATHS[@]} -eq 0 ] && PATHS=("/")

BASE="http://localhost:$PORT"
WORK="$ROOT/.smoke"
mkdir -p "$WORK"
DB="$WORK/smoke-$PORT.db"
DB_NATIVE="$(cygpath -m "$DB" 2>/dev/null || echo "$DB")"
JAR="$WORK/cookies-$PORT.txt"
LOG="$WORK/app-$PORT.log"
rm -f "$DB" "$DB-shm" "$DB-wal" "$JAR"

WEB="$ROOT/src/SmartSurvey.Web"
if [ "$BUILD" = "1" ]; then
  dotnet build "$WEB" -nologo -v q >"$WORK/build-$PORT.log" 2>&1 || { echo "BUILD FAILED (see $WORK/build-$PORT.log)"; tail -30 "$WORK/build-$PORT.log"; exit 1; }
fi

# Run the compiled app directly (so we can reliably stop exactly this process).
(
  cd "$WEB" || exit 1
  ASPNETCORE_ENVIRONMENT=Development \
  Database__Provider=Sqlite \
  ConnectionStrings__DefaultConnection="Data Source=$DB_NATIVE" \
  Seed__DemoData=true \
  Seed__AdminPassword='Admin123!' \
  Seed__SuperAdminPassword='SuperAdmin123!' \
  Https__Redirect=false \
  exec dotnet "bin/Debug/net10.0/SmartSurvey.Web.dll" --urls "$BASE"
) >"$LOG" 2>&1 &
APP_PID=$!

cleanup() {
  kill "$APP_PID" 2>/dev/null
  if [ -r "/proc/$APP_PID/winpid" ]; then taskkill //F //PID "$(cat /proc/$APP_PID/winpid 2>/dev/null)" >/dev/null 2>&1; fi
  wait "$APP_PID" 2>/dev/null
}
trap cleanup EXIT

for _ in $(seq 1 90); do
  curl -s -o /dev/null "$BASE/health" && break
  kill -0 "$APP_PID" 2>/dev/null || break
  sleep 1
done
if ! curl -s -o /dev/null "$BASE/health"; then
  echo "APP DID NOT START (see $LOG)"; tail -40 "$LOG"; exit 1
fi

login() { # $1=email $2=password
  local html token
  html=$(curl -s -c "$JAR" -b "$JAR" "$BASE/Account/Login")
  token=$(printf '%s' "$html" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//; s/"$//')
  [ -z "$token" ] && token=$(printf '%s' "$html" | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | head -1 | sed 's/.*value="//; s/"$//')
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' -c "$JAR" -b "$JAR" -X POST "$BASE/Account/Login" \
    --data-urlencode "_handler=login" \
    --data-urlencode "__RequestVerificationToken=$token" \
    --data-urlencode "Input.Email=$1" \
    --data-urlencode "Input.Password=$2" \
    --data-urlencode "Input.RememberMe=false")
  if grep -q "Identity.Application" "$JAR" 2>/dev/null; then echo "Logged in as $1 (HTTP $code)"; else echo "LOGIN FAILED for $1 (HTTP $code)"; return 1; fi
}

case "$USER_KIND" in
  admin) login "admin@smartsurvey.local" "Admin123!" || exit 1 ;;
  user) login "user@smartsurvey.local" "User123!" || exit 1 ;;
  superadmin) login "superadmin@smartsurvey.local" "SuperAdmin123!" || exit 1 ;;
  acme) login "admin@acme.local" "Admin123!" || exit 1 ;;
esac

FAIL=0
for p in "${PATHS[@]}"; do
  out="$WORK/page-$PORT.html"
  code=$(curl -s -o "$out" -w '%{http_code}' -b "$JAR" "$BASE$p")
  markers=$(grep -oE "blazor-error-boundary\"|An unhandled exception|Exception:|NotImplementedException|InvalidOperationException|An error occurred while processing your request|Something went wrong" "$out" | sort -u | tr '\n' ' ')
  title=$(grep -o '<title>[^<]*</title>' "$out" | head -1 | sed 's/<[^>]*>//g')
  if [[ "$code" =~ ^(2|3) ]] && [ -z "$markers" ]; then
    echo "OK   $code $p  [$title]"
  else
    echo "FAIL $code $p  [$title] $markers"
    FAIL=1
  fi
done

if grep -qE "fail: |Unhandled exception" "$LOG"; then
  echo "---- server log errors ----"
  grep -E -A6 "fail: |Unhandled exception" "$LOG" | head -60
  FAIL=1
fi
exit $FAIL
