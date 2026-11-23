#!/usr/bin/env bash
set -euo pipefail

# Proves the boundaries hold: plants one breach at a time, builds, and expects the build to fail with the
# named error. Every planted file is removed again, even on failure.
#   1. vacation's src uses account's internals (Ctx.Account.Data)      -> CS0234 (no project reference)
#   2. plan's src uses a contract it did not list in needs (vacation)  -> CS0234
#   3. vacation's domain/ uses EF Core                                  -> LYR001
#   4. a vacation action without [Policy]                               -> the kernel refuses to start
# Builds go through the box slot queue when it exists (LAZYTAX_SLOT, default ../tools/slot.sh).

ROOT="$(git rev-parse --show-toplevel)"
cd "${ROOT}"
SLOT_BIN="${LAZYTAX_SLOT:-../tools/slot.sh}"
SLOT=()
[ -x "${SLOT_BIN}" ] && SLOT=("${SLOT_BIN}" dotnet)
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
FLAGS=(-nologo -m:1 -p:UseSharedCompilation=false -nodeReuse:false -v q)
PLANTED=""
cleanup() { [ -z "${PLANTED}" ] || rm -f "${PLANTED}"; } # must return 0: an EXIT trap sets the exit status
trap cleanup EXIT

expect_fail() { # <name> <file> <content> <project> <expected text>
  local name="$1" file="$2" content="$3" project="$4" want="$5" out
  PLANTED="${file}"
  printf '%s\n' "${content}" >"${file}"
  set +e
  out="$("${SLOT[@]}" timeout 600 dotnet build "${project}" "${FLAGS[@]}" 2>&1)"
  local code=$?
  set -e
  rm -f "${file}"
  PLANTED=""
  [ "${code}" -eq 0 ] && echo "❌ ${name}: the build passed, the breach was not caught" >&2 && exit 1
  ! grep -q "${want}" <<<"${out}" && echo "❌ ${name}: failed, but without ${want}:" >&2 && echo "${out}" | tail -20 >&2 && exit 1
  echo "✅ ${name}: refused (${want})"
}

expect_fail "src uses another context's internals" contexts/vacation/src/BreachInternals.cs \
  'namespace Ctx.Vacation; internal static class BreachInternals { internal static System.Type T => typeof(Ctx.Account.Data.AccountDb); }' \
  contexts/vacation/src/vacation.csproj "error CS0234"

expect_fail "src uses a contract not in needs" contexts/plan/src/BreachNeeds.cs \
  'namespace Ctx.Plan; internal static class BreachNeeds { internal static System.Type T => typeof(Ctx.Vacation.Contract.V1.IVacationV1); }' \
  contexts/plan/src/plan.csproj "error CS0234"

expect_fail "domain/ uses EF Core" contexts/vacation/src/domain/BreachLayer.cs \
  'using Microsoft.EntityFrameworkCore; namespace Ctx.Vacation.Domain; internal static class BreachLayer { internal static System.Type T => typeof(DbContext); }' \
  contexts/vacation/src/vacation.csproj "error LYR001"

# 4: an endpoint without a policy stops the app before it listens.
PLANTED=contexts/vacation/src/api/v1/BreachPolicy.cs
cat >"${PLANTED}" <<'CS'
using Microsoft.AspNetCore.Mvc;
namespace Ctx.Vacation.Api.V1;
[Route("breach")]
public sealed class BreachPolicyController : LazyTax.Platform.Http.ContextController
{
  [HttpGet] public string Get() => "no policy";
}
CS
"${SLOT[@]}" timeout 600 dotnet build kernel/Kernel.csproj "${FLAGS[@]}" >/dev/null
set +e
out="$(ConnectionStrings__Main='Host=localhost;Database=unused' timeout 120 dotnet kernel/bin/Debug/net10.0/LazyTax.Kernel.dll policies 2>&1)"
code=$?
set -e
rm -f "${PLANTED}"
PLANTED=""
"${SLOT[@]}" timeout 600 dotnet build kernel/Kernel.csproj "${FLAGS[@]}" >/dev/null
[ "${code}" -eq 0 ] && echo "❌ endpoint without a policy: the app started" >&2 && exit 1
! grep -q "has no \[Policy\]" <<<"${out}" && echo "❌ endpoint without a policy: exit ${code}, but no named problem:" >&2 && echo "${out}" | tail -10 >&2 && exit 1
echo "✅ endpoint without a policy: refused at start ($(grep -o "GET [^ ]* has no \[Policy\]" <<<"${out}" | head -1))"
