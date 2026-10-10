#!/usr/bin/env bash
#
# Runs the live contract tests - anonymous and signed-in - against a throwaway backend built from the
# backend checkout, then takes all of it down again:
#
#   1. Postgres and Redis containers (sh-unity-pg, sh-unity-redis) on loopback ports.
#   2. The backend Api, built into a temporary directory so the checkout gains no bin/ or obj/, run in
#      Development with scripts in-process, game hosting on a temporary directory, and the
#      once-a-day-per-address registration throttle off (every run registers fresh accounts).
#   3. tools/smtp_sink.py, with the deployment's SMTP settings pointed at it, so the tests can read
#      the verification link a player would be emailed.
#   4. dotnet test --filter Category=Live, with STARHERMIT_TEST_BASE_URL and STARHERMIT_TEST_MAILBOX.
#   5. With STARHERMIT_LIVE_AOT=1, the same deployment driven by build/aot-smoke: the package's runtime
#      published with Native AOT and full trimming - no JIT, every unreferenced member removed, the
#      nearest thing to a stripped IL2CPP player that runs without a Unity editor. Needs clang, or gcc
#      (used automatically when clang is absent).
#
# Extra arguments go to dotnet test. Needs docker, python3 and the .NET 8 SDK; STARHERMIT_BACKEND
# names the backend checkout when it is not at ~/pi/dashboard/projects/starhermit. Only containers
# this script started are removed.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backend="${STARHERMIT_BACKEND:-$HOME/pi/dashboard/projects/starhermit}"
pg_port="${STARHERMIT_LIVE_PG_PORT:-55442}"
redis_port="${STARHERMIT_LIVE_REDIS_PORT:-56442}"
api_port="${STARHERMIT_LIVE_API_PORT:-5099}"
smtp_port="${STARHERMIT_LIVE_SMTP_PORT:-2526}"
pg=sh-unity-pg
redis=sh-unity-redis

work="$(mktemp -d "${TMPDIR:-/tmp}/starhermit-sdk-live.XXXXXX")"
started=()
api_pid=""
sink_pid=""

cleanup() {
  [ -n "$api_pid" ] && kill "$api_pid" 2>/dev/null || true
  [ -n "$sink_pid" ] && kill "$sink_pid" 2>/dev/null || true
  for container in "${started[@]}"; do docker rm -f "$container" >/dev/null 2>&1 || true; done
  rm -rf "$work"
}
trap cleanup EXIT

if [ ! -d "$backend/src/Platform.Api" ]; then
  echo "No backend checkout at $backend (set STARHERMIT_BACKEND)." >&2
  exit 2
fi

echo "==> Starting Postgres and Redis"
docker run -d --name "$pg" -e POSTGRES_PASSWORD=pg -p "127.0.0.1:$pg_port:5432" postgres:16 >/dev/null
started+=("$pg")
docker run -d --name "$redis" -p "127.0.0.1:$redis_port:6379" redis:7 >/dev/null
started+=("$redis")
# Over TCP: during initialisation the image runs a socket-only server, which pg_isready on the socket
# would report ready just before it restarts.
until docker exec "$pg" pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1; do sleep 1; done

echo "==> Building the backend Api"
dotnet build "$backend/src/Platform.Api/Platform.Api.csproj" --artifacts-path "$work/api" --nologo -v quiet
api_dll="$(find "$work/api/bin/Platform.Api" -name Platform.Api.dll | head -n 1)"

mkdir -p "$work/mail" "$work/host"
python3 "$root/tools/smtp_sink.py" --port "$smtp_port" --dir "$work/mail" >"$work/sink.log" 2>&1 &
sink_pid=$!

echo "==> Starting the Api on 127.0.0.1:$api_port"
(
  cd "$(dirname "$api_dll")"
  exec env \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://127.0.0.1:$api_port" \
    "ConnectionStrings__Postgres=Host=127.0.0.1;Port=$pg_port;Database=starhermit;Username=postgres;Password=pg" \
    "ConnectionStrings__Redis=127.0.0.1:$redis_port" \
    Games__Scripts__Isolation=inprocess \
    Games__HostRoot="$work/host" \
    PublicKeyAuth__RegistrationThrottleHours=0 \
    Logging__LogLevel__Default=Warning \
    dotnet Platform.Api.dll
) >"$work/api.log" 2>&1 &
api_pid=$!

for _ in $(seq 1 120); do
  if curl -sf "http://127.0.0.1:$api_port/api/v1/time" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$api_pid" 2>/dev/null; then
    echo "The Api exited during startup:" >&2
    tail -n 40 "$work/api.log" >&2
    exit 1
  fi
  sleep 1
done
curl -sf "http://127.0.0.1:$api_port/api/v1/time" >/dev/null

# The Api migrated the schema on startup; outbound mail is configured in the database, as an
# operator would through the admin backend.
docker exec "$pg" psql -q -U postgres -d starhermit -c \
  "INSERT INTO \"SmtpSettings\" (\"Host\", \"Port\", \"FromAddress\", \"FromName\", \"UseSsl\")
   VALUES ('127.0.0.1', $smtp_port, 'noreply@sdk-live.test', 'Starhermit SDK live tests', false);"

echo "==> Running the live contract tests"
status=0
STARHERMIT_TEST_BASE_URL="http://127.0.0.1:$api_port/api/v1/" \
STARHERMIT_TEST_MAILBOX="$work/mail" \
  dotnet test "$root/build/tests/Starhermit.Tests.csproj" --nologo --filter "Category=Live" "$@" || status=$?

if [ "$status" -eq 0 ] && [ "${STARHERMIT_LIVE_AOT:-0}" = "1" ]; then
  echo "==> Publishing the SDK with Native AOT and full trimming"
  linker=()
  command -v clang >/dev/null 2>&1 || linker=(-p:CppCompilerAndLinker=gcc)
  dotnet publish "$root/build/aot-smoke/Starhermit.AotSmoke.csproj" -c Release -r linux-x64 \
    --artifacts-path "$work/aot" -o "$work/aot-out" --nologo -v quiet ${linker[@]+"${linker[@]}"}
  echo "==> Running the AOT build against the deployment"
  STARHERMIT_TEST_BASE_URL="http://127.0.0.1:$api_port/api/v1/" \
  STARHERMIT_TEST_MAILBOX="$work/mail" \
    "$work/aot-out/starhermit-aot-smoke" || status=$?
fi

if [ "$status" -ne 0 ]; then
  echo "==> Api log (last 40 lines)" >&2
  tail -n 40 "$work/api.log" >&2
fi
exit "$status"
