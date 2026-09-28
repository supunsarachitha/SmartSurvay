#!/usr/bin/env bash
# ---------------------------------------------------------------------------------------------
# Smoke test for a running SmartSurvey container stack (docker compose up): health, public pages, the scripts
# the pages load (without _framework/blazor.web.js no button works), admin sign-in over the REST API and a PDF
# export (proves QuestPDF's native libraries and fonts work inside the image). Requires curl and python3.
#
# Usage:   scripts/container-smoke.sh [base-url] [admin-email] [admin-password] [super-admin-email] [super-admin-password]
# Example: scripts/container-smoke.sh http://localhost:8080 admin@smartsurvey.local 'ChangeMe123!'
#          Pass an empty password ('') to skip the admin checks, e.g. on a stack whose admin changed the password
#          (every wrong attempt counts towards the account lockout). The same applies to the super admin password.
# Exit code: 0 when every check passed, 1 otherwise.
# ---------------------------------------------------------------------------------------------
set -u
BASE="${1:-http://localhost:8080}"
EMAIL="${2:-admin@smartsurvey.local}"
PASSWORD="${3-ChangeMe123!}" # only an omitted argument gets the default; '' skips the admin checks
SUPER_EMAIL="${4:-superadmin@smartsurvey.local}"
SUPER_PASSWORD="${5-ChangeMe123!}"
FAIL=0

check() { # $1=description $2=condition result (0 = ok)
  if [ "$2" -eq 0 ]; then echo "OK   $1"; else echo "FAIL $1"; FAIL=1; fi
}

for _ in $(seq 1 90); do
  curl -fs -o /dev/null "$BASE/health" && break
  sleep 2
done
curl -fs "$BASE/health" | grep -q Healthy; check "health endpoint" $?

for path in / /surveys /faq /s/customer-satisfaction-survey /Account/Login /signup /Account/Register /w/default; do
  code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE$path")
  [ "$code" = "200" ]; check "GET $path ($code)" $?
done

# Every script the home page loads, and Blazor's script in any case, must be served as JavaScript.
scripts=$( { echo "_framework/blazor.web.js"; curl -fs "$BASE/" | grep -oE '<script[^>]* src="[^"]+"' | sed -E 's/.* src="([^"]+)".*/\1/'; } \
  | grep -vE '^(https?:)?//' | sed 's#^/##' | sort -u)
for src in $scripts; do
  result=$(curl -s -o /dev/null -w '%{http_code} %{content_type}' "$BASE/$src")
  case "$result" in "200 "*javascript*) ok=0 ;; *) ok=1 ;; esac
  check "script /$src ($result)" $ok
done

curl -fs "$BASE/api/v1/public/settings" | grep -q allowWorkspaceSignup; check "public system settings" $?

if [ -n "$SUPER_PASSWORD" ]; then
  SUPER=$(curl -fs -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$SUPER_EMAIL\",\"password\":\"$SUPER_PASSWORD\"}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["accessToken"])' 2>/dev/null)
  [ -n "$SUPER" ]; check "super admin sign-in (bearer token)" $?
  if [ -n "$SUPER" ]; then
    curl -fs "$BASE/api/v1/system/overview" -H "Authorization: Bearer $SUPER" | grep -q workspaceCount; check "system overview" $?
    code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/surveys" -H "Authorization: Bearer $SUPER")
    [ "$code" = "403" ]; check "super admin cannot open workspace content ($code)" $?
  fi
else
  echo "SKIP super admin checks (no super admin password given)"
fi

if [ -z "$PASSWORD" ]; then
  echo "SKIP admin sign-in and PDF export (no admin password given)"
  exit $FAIL
fi

TOKEN=$(curl -fs -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["accessToken"])' 2>/dev/null)
[ -n "$TOKEN" ]; check "admin sign-in (bearer token)" $?

REPORT=$(curl -fs "$BASE/api/v1/reports" -H "Authorization: Bearer $TOKEN" \
  | python3 -c 'import sys,json; items=json.load(sys.stdin)["items"]; print(items[0]["id"] if items else "")' 2>/dev/null)
[ -n "$REPORT" ]; check "demo report available" $?

if [ -n "$REPORT" ]; then
  PDF=$(mktemp)
  curl -fs "$BASE/api/v1/reports/$REPORT/export?format=pdf" -H "Authorization: Bearer $TOKEN" -o "$PDF"
  head -c 5 "$PDF" | grep -q '%PDF-'; check "PDF export ($(wc -c < "$PDF" | tr -d ' ') bytes)" $?
  rm -f "$PDF"
fi

exit $FAIL
