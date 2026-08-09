#!/usr/bin/env bash
set -euo pipefail

readonly expected_commit="c7475dfc55a374cd0dd08821bf17125dab1e3145"
readonly repository="${1:?usage: capture-upstream-go-baseline.sh <upstream-repository> <output-directory>}"
readonly output_directory="${2:?usage: capture-upstream-go-baseline.sh <upstream-repository> <output-directory>}"

mkdir -p "$output_directory"
readonly events_path="$output_directory/events.jsonl"
readonly stderr_path="$output_directory/stderr.log"
readonly summary_path="$output_directory/summary.json"
readonly checksums_path="$output_directory/SHA256SUMS"

write_summary() {
  local result="$1"
  local exit_code="$2"
  local actual_commit="$3"
  local go_version="$4"
  local event_count="$5"
  local skip_event_count="$6"
  printf '{"schema_version":1,"result":"%s","exit_code":%s,"expected_commit":"%s","actual_commit":"%s","go_version":"%s","event_count":%s,"skip_event_count":%s}\n' \
    "$result" "$exit_code" "$expected_commit" "$actual_commit" "$go_version" "$event_count" "$skip_event_count" \
    > "$summary_path"
}

if [[ ! -d "$repository/.git" ]]; then
  : > "$events_path"
  printf '%s\n' 'The upstream repository is missing or is not a Git worktree.' > "$stderr_path"
  write_summary "invalid_repository" 2 "" "" 0 0
  (cd "$output_directory" && sha256sum events.jsonl stderr.log summary.json > SHA256SUMS)
  exit 2
fi

actual_commit="$(git -C "$repository" rev-parse HEAD)"
if [[ "$actual_commit" != "$expected_commit" ]]; then
  : > "$events_path"
  printf '%s\n' 'The upstream repository HEAD does not match the pinned baseline.' > "$stderr_path"
  write_summary "wrong_commit" 3 "$actual_commit" "" 0 0
  (cd "$output_directory" && sha256sum events.jsonl stderr.log summary.json > SHA256SUMS)
  exit 3
fi

go_version="$(go version | tr -d '\r\n')"
if [[ "$go_version" == *'"'* || "$go_version" == *'\\'* ]]; then
  : > "$events_path"
  printf '%s\n' 'The Go version string cannot be represented in the stable report.' > "$stderr_path"
  write_summary "invalid_go_version" 4 "$actual_commit" "" 0 0
  (cd "$output_directory" && sha256sum events.jsonl stderr.log summary.json > SHA256SUMS)
  exit 4
fi

set +e
(
  cd "$repository"
  CGO_ENABLED=0 GOOS=linux GOARCH=amd64 \
    go test -p 1 -count=1 -json ./...
) > "$events_path" 2> "$stderr_path"
test_exit_code=$?
set -e

event_count="$(wc -l < "$events_path" | tr -d ' ')"
skip_event_count="$(grep -c '"Action":"skip"' "$events_path" || true)"
if [[ "$test_exit_code" -eq 0 ]]; then
  result="passed"
else
  result="failed"
fi

write_summary \
  "$result" \
  "$test_exit_code" \
  "$actual_commit" \
  "$go_version" \
  "$event_count" \
  "$skip_event_count"
(cd "$output_directory" && sha256sum events.jsonl stderr.log summary.json > SHA256SUMS)

exit "$test_exit_code"
