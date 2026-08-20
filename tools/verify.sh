#!/usr/bin/env bash
#
# The package's quality gate, and what CI runs.
#
#   1. Build every project, including the two Unity compile-checks, with warnings as errors.
#   2. Run the whole test suite.
#   3. Regenerate the coverage manifest from the backend and fail if any API operation is unmapped.
#
# Step 3 needs the backend checkout; pass --skip-coverage (or set STARHERMIT_BACKEND) when it is
# somewhere else. Everything else runs anywhere .NET 8 does - no Unity licence required.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backend="${STARHERMIT_BACKEND:-$HOME/pi/dashboard/projects/starhermit}"
skip_coverage=0

for argument in "$@"; do
  case "$argument" in
    --skip-coverage) skip_coverage=1 ;;
    *) echo "unknown option: $argument" >&2; exit 2 ;;
  esac
done

echo "==> Building (warnings are errors, XML docs required)"
dotnet build "$root/Starhermit.Sdk.sln" --nologo -v minimal

echo "==> Testing"
dotnet test "$root/build/tests/Starhermit.Tests.csproj" --nologo -v minimal --no-build

# The live contract tests skip themselves unless a deployment is named, so this is a no-op by default
# and a real contract check when STARHERMIT_TEST_BASE_URL points somewhere.
if [ -n "${STARHERMIT_TEST_BASE_URL:-}" ]; then
  echo "==> Checking the live contract against $STARHERMIT_TEST_BASE_URL"
fi

if [ "$skip_coverage" -eq 1 ]; then
  echo "==> Skipping API coverage (--skip-coverage)"
elif [ ! -d "$backend" ]; then
  echo "==> Skipping API coverage: no backend checkout at $backend (set STARHERMIT_BACKEND)"
else
  echo "==> Checking API coverage against $backend"
  python3 "$root/tools/generate_coverage.py" --backend "$backend" --check

  # Only meaningful where this directory is its own repository; the generated files are committed so
  # that CI, which has no backend checkout, still verifies the manifest the SDK was built against.
  if [ "$(git -C "$root" rev-parse --show-toplevel 2>/dev/null)" = "$root" ] &&
     ! git -C "$root" diff --quiet -- contracts Packages/com.starhermit.sdk/Tests/Runtime/Generated Packages/com.starhermit.sdk/Documentation~/api-coverage.md; then
    echo "The generated contracts changed. Review and commit them." >&2
    exit 1
  fi
fi

echo "All checks passed."
