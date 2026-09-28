#!/usr/bin/env bash
# ---------------------------------------------------------------------------------------------
# Smoke test for a running SmartSurvey container stack (docker compose up): health, public pages,
# admin sign-in over the REST API and a PDF export (proves QuestPDF's native libraries and fonts
# work inside the image). Requires curl and python3.
#
# Usage:   scripts/container-smoke.sh [base-url] [admin-email] [admin-password]
# Example: scripts/container-smoke.sh http://localhost:8080 admin@smartsurvey.local 'ChangeMe123!'
# Exit code: 0 when every check passed, 1 otherwise.
# ---------------------------------------------------------------------------------------------
set -u
BASE="${1:-http://localhost:8080}"
EMAIL="${2:-admin@smartsurvey.local}"
PASSWORD="${3:-ChangeMe123!}"
FAIL=0

check() { # $1=description $2=condition result (0 = ok)
  if [ "$2" -eq 0 ]; then echo "OK   $1"; else echo "FAIL $1"; FAIL=1; fi
}

for _ in $(seq 1 90); do
  curl -fs -o /dev/null "$BASE/health" && break
  sleep 2
done
curl -fs "$BASE/health" | grep -q Healthy; check "health endpoint" $?

for path in / /surveys /faq /s/customer-satisfaction-survey /Account/Login; do
  code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE$path")
  [ "$code" = "200" ]; check "GET $path ($code)" $?
done

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
