#!/usr/bin/env bash
# check-plan-state.sh — flags plan-bookkeeping that has drifted from git/GitHub reality.
# Catches the recurring rot: a plan marked "in-review" with nothing in review, or a
# PR described as "open"/"in flight" long after it merged. Exit 1 if anything is stale.
#
# Usage: scripts/check-plan-state.sh   (needs `gh` authenticated)
set -u
cd "$(git rev-parse --show-toplevel)"
fail=0
note(){ printf '  ⚠️  %s\n' "$1"; fail=1; }

# --- reality from GitHub ---
open_prs="$(gh pr list --state open  --json number --jq '.[].number' 2>/dev/null | sort -n)"
merged_prs="$(gh pr list --state merged --limit 200 --json number --jq '.[].number' 2>/dev/null | sort -n)"
is_open(){   grep -qx "$1" <<<"$open_prs"; }
is_merged(){ grep -qx "$1" <<<"$merged_prs"; }

echo "== 1. Plans marked in-review must have an open PR referencing them =="
for f in docs/plans/[0-9]*.md; do
  status_line="$(grep -m1 '^Status:' "$f" | sed -E 's/<!--.*-->//' || true)"
  grep -qi 'in-review' <<<"$status_line" || continue
  # any open PR number cited anywhere in this plan file?
  hit=""
  for n in $(grep -oE '#[0-9]+' "$f" | tr -d '#' | sort -un); do is_open "$n" && hit="$n"; done
  if [ -z "$hit" ]; then
    note "$(basename "$f") is Status: in-review but no PR it cites is open. $(sed -E 's/<!--.*//' <<<"$status_line")"
  fi
done

echo "== 2. PRs described as open/in-flight/in-review that have actually merged =="
# scan STATE.md prose for '#NN ... <open-word>' on the same line
while IFS= read -r line; do
  case "$line" in
    *[Oo]pen\ against*|*in\ flight*|*in-review*|*awaiting\ review*|*passed\ review*and\ is\ open*)
      for n in $(grep -oE '#[0-9]+' <<<"$line" | tr -d '#'); do
        if is_merged "$n" && ! is_open "$n"; then
          note "STATE.md calls #$n open/in-flight but it is MERGED — line: $(echo "$line" | cut -c1-90)…"
        fi
      done ;;
  esac
done < docs/plans/STATE.md

echo "== 3. The 'In flight' section should not list a merged PR as the current one =="
awk '/^## In flight/{f=1;next} /^### /{f=0} /^## /{f=0} f' docs/plans/STATE.md | \
while IFS= read -r line; do
  for n in $(grep -oE '#[0-9]+' <<<"$line" | tr -d '#'); do
    if is_merged "$n" && ! is_open "$n"; then
      note "In-flight section names #$n, which is merged (move it to Shipped)."
    fi
  done
done

echo
if [ "$fail" -eq 0 ]; then echo "✅ plan bookkeeping is consistent with GitHub."; else
  echo "❌ stale bookkeeping found — fix STATE.md / plan status lines above."; fi
exit "$fail"
