#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-qbittorrent-compose.sh IMAGE}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repository_root/docker-compose.qbittorrent-integration.yml"
fixture_base64="$repository_root/tests/fixtures/animegonet-ci.torrent.b64"
integration_root="$(mktemp -d)"
project_name="animegonet-qbt-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
access_key="animegonet-compose-smoke"
runtime_password="animegonet-ci-password"
test_uid="$(id -u)"
test_gid="$(id -g)"

if [[ "$test_uid" == "0" ]]; then
  test_uid=10001
  test_gid=10001
fi

export ANIMEGONET_IMAGE="$image"
export ANIMEGONET_INTEGRATION_ROOT="$integration_root"
export ANIMEGONET_ACCESS_KEY="$access_key"
export ANIMEGONET_UID="$test_uid"
export ANIMEGONET_GID="$test_gid"

compose() {
  docker compose \
    --project-name "$project_name" \
    --file "$compose_file" \
    "$@"
}

cleanup() {
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf -- "$integration_root"
}
trap cleanup EXIT

mkdir -p \
  "$integration_root/animegonet/data/config" \
  "$integration_root/qbittorrent/bt" \
  "$integration_root/qbittorrent/pt" \
  "$integration_root/download/incomplete/bt" \
  "$integration_root/download/incomplete/pt" \
  "$integration_root/download/anime"

if [[ "$(id -u)" == "0" ]]; then
  chown -R "$test_uid:$test_gid" "$integration_root"
fi

base64 --decode "$fixture_base64" >"$integration_root/animegonet-ci.torrent"

service_port() {
  local service="$1"
  local container_port="$2"
  compose port "$service" "$container_port" | awk -F: 'NR==1 { print $NF }'
}

wait_for_temporary_password() {
  local service="$1"
  local password=""
  for attempt in $(seq 1 120); do
    password="$(
      compose logs --no-color "$service" 2>/dev/null \
        | sed -nE 's/.*[Tt]emporary password is provided for this session:[[:space:]]*//p' \
        | tail -n 1
    )"
    if [[ -n "$password" ]]; then
      printf '%s' "$password"
      return
    fi
    if [[ "$attempt" == 120 ]]; then
      compose logs --no-color "$service"
      echo "qBittorrent temporary WebUI password was not found for $service" >&2
      exit 1
    fi
    sleep 0.25
  done
}

login() {
  local base_url="$1"
  local password="$2"
  local cookie_jar="$3"
  local body=""
  body="$(
    curl --silent --show-error \
      --header "Referer: $base_url/" \
      --header "Origin: $base_url" \
      --cookie-jar "$cookie_jar" \
      --data-urlencode "username=admin" \
      --data-urlencode "password=$password" \
      "$base_url/api/v2/auth/login"
  )"
  [[ "$body" == "Ok." ]]
}

authenticated_post() {
  local base_url="$1"
  local cookie_jar="$2"
  local endpoint="$3"
  shift 3
  curl --fail-with-body --silent --show-error \
    --header "Referer: $base_url/" \
    --header "Origin: $base_url" \
    --cookie "$cookie_jar" \
    "$@" \
    "$base_url$endpoint"
}

authenticated_get() {
  local base_url="$1"
  local cookie_jar="$2"
  local endpoint="$3"
  curl --fail-with-body --silent --show-error \
    --header "Referer: $base_url/" \
    --header "Origin: $base_url" \
    --cookie "$cookie_jar" \
    "$base_url$endpoint"
}

configure_qbittorrent() {
  local service="$1"
  local instance="$2"
  local port=""
  local base_url=""
  local temporary_password=""
  local cookie_jar="$integration_root/${instance}.cookies"
  local preferences=""

  port="$(service_port "$service" 8080)"
  base_url="http://127.0.0.1:$port"
  temporary_password="$(wait_for_temporary_password "$service")"
  login "$base_url" "$temporary_password" "$cookie_jar"

  preferences="$(
    printf '{"save_path":"/download/incomplete/%s","temp_path":"/download/incomplete/%s","temp_path_enabled":false,"web_ui_password":"%s"}' \
      "$instance" "$instance" "$runtime_password"
  )"
  authenticated_post \
    "$base_url" \
    "$cookie_jar" \
    "/api/v2/app/setPreferences" \
    --data-urlencode "json=$preferences" >/dev/null

  compose restart "$service" >/dev/null
  rm -f -- "$cookie_jar"
  for attempt in $(seq 1 80); do
    if login "$base_url" "$runtime_password" "$cookie_jar" 2>/dev/null; then
      break
    fi
    if [[ "$attempt" == 80 ]]; then
      compose logs --no-color "$service"
      echo "qBittorrent persistent credential reconnect failed for $service" >&2
      exit 1
    fi
    sleep 0.25
  done

  local version=""
  local api_version=""
  local preferences_after=""
  version="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/app/version")"
  api_version="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/app/webapiVersion")"
  preferences_after="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/app/preferences")"
  python3 -c '
import json
import sys
instance, version, api_version = sys.argv[1:]
preferences = json.load(sys.stdin)
expected = f"/download/incomplete/{instance}"
assert version.strip(), "qBittorrent version is empty"
assert api_version.strip(), "qBittorrent Web API version is empty"
assert preferences["save_path"].rstrip("/") == expected
assert not preferences["temp_path_enabled"]
' "$instance" "$version" "$api_version" <<<"$preferences_after"

  printf '%s|%s' "$base_url" "$cookie_jar"
}

exercise_fixture() {
  local instance="$1"
  local connection="$2"
  local base_url="${connection%%|*}"
  local cookie_jar="${connection#*|}"
  local category="animegonet-ci-${instance}-${GITHUB_RUN_ID:-local}-$$"
  local tag="animegonet-ci-${instance}-${GITHUB_RUN_ID:-local}-$$"
  local tasks=""
  local hash=""

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/createCategory" \
    --data-urlencode "category=$category" \
    --data-urlencode "savePath=/download/incomplete/$instance" >/dev/null
  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/createTags" \
    --data-urlencode "tags=$tag" >/dev/null
  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/add" \
    --form "torrents=@$integration_root/animegonet-ci.torrent;type=application/x-bittorrent" \
    --form-string "savepath=/download/incomplete/$instance" \
    --form-string "category=$category" \
    --form-string "tags=$tag" \
    --form-string "stopped=true" >/dev/null

  for attempt in $(seq 1 40); do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?filter=all&tag=$tag")"
    hash="$(
      python3 -c \
        'import json,sys; items=json.load(sys.stdin); print(items[0]["hash"] if len(items)==1 else "")' \
        <<<"$tasks"
    )"
    [[ -n "$hash" ]] && break
    if [[ "$attempt" == 40 ]]; then
      echo "The isolated fixture was not listed by tag for $instance" >&2
      exit 1
    fi
    sleep 0.1
  done
  [[ "$hash" == "bcff48bafa9434c0062a4c2a45ed885f26701721" ]]

  local files=""
  files="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/files?hash=$hash")"
  python3 -c \
    'import json,sys; files=json.load(sys.stdin); assert len(files)==1; assert files[0]["name"]=="animegonet-ci.bin"; assert files[0]["size"]==5' \
    <<<"$files"

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/filePrio" \
    --data-urlencode "hash=$hash" \
    --data-urlencode "id=0" \
    --data-urlencode "priority=0" >/dev/null
  files="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/files?hash=$hash")"
  python3 -c \
    'import json,sys; files=json.load(sys.stdin); assert files[0]["priority"]==0' \
    <<<"$files"

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/filePrio" \
    --data-urlencode "hash=$hash" \
    --data-urlencode "id=0" \
    --data-urlencode "priority=1" >/dev/null
  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/start" \
    --data-urlencode "hashes=$hash" >/dev/null
  for attempt in $(seq 1 20); do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    if python3 -c '
import json
import sys
state = json.load(sys.stdin)[0]["state"].lower()
sys.exit(1 if state.startswith(("stopped", "paused")) else 0)
' <<<"$tasks"; then
      break
    fi
    if [[ "$attempt" == 20 ]]; then
      echo "The isolated fixture did not leave its stopped state for $instance" >&2
      exit 1
    fi
    sleep 0.1
  done
  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/stop" \
    --data-urlencode "hashes=$hash" >/dev/null

  for attempt in $(seq 1 20); do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    if python3 -c '
import json
import sys
state = json.load(sys.stdin)[0]["state"].lower()
sys.exit(0 if state.startswith(("stopped", "paused")) else 1)
' <<<"$tasks"; then
      break
    fi
    if [[ "$attempt" == 20 ]]; then
      echo "The isolated fixture did not enter its stopped state for $instance" >&2
      exit 1
    fi
    sleep 0.1
  done

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/delete" \
    --data-urlencode "hashes=$hash" \
    --data-urlencode "deleteFiles=true" >/dev/null

  for attempt in $(seq 1 40); do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    if python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin)==[] else 1)' <<<"$tasks"; then
      break
    fi
    if [[ "$attempt" == 40 ]]; then
      echo "The isolated fixture was not deleted for $instance" >&2
      exit 1
    fi
    sleep 0.1
  done

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/deleteTags" \
    --data-urlencode "tags=$tag" >/dev/null
  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/removeCategories" \
    --data-urlencode "categories=$category" >/dev/null

  tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?filter=all&tag=$tag")"
  python3 -c 'import json,sys; assert json.load(sys.stdin)==[]' <<<"$tasks"
}

compose up --detach qbittorrent-bt qbittorrent-pt >/dev/null
bt_connection="$(configure_qbittorrent qbittorrent-bt bt)"
pt_connection="$(configure_qbittorrent qbittorrent-pt pt)"
exercise_fixture bt "$bt_connection"
exercise_fixture pt "$pt_connection"

updated_at="$(date --utc '+%Y-%m-%dT%H:%M:%S.0000000+00:00')"
cat >"$integration_root/animegonet/data/config/downloaders.private.json" <<JSON
{
  "format_version": 1,
  "revision": 1,
  "downloaders": {
    "bt": {
      "base_url": "http://qbittorrent-bt:8080",
      "username": "admin",
      "password": "$runtime_password",
      "download_path": "/download/incomplete/bt",
      "enabled": true,
      "revision": 1,
      "updated_at_utc": "$updated_at"
    },
    "pt": {
      "base_url": "http://qbittorrent-pt:8080",
      "username": "admin",
      "password": "$runtime_password",
      "download_path": "/download/incomplete/pt",
      "enabled": true,
      "revision": 1,
      "updated_at_utc": "$updated_at"
    }
  }
}
JSON
chmod 600 "$integration_root/animegonet/data/config/downloaders.private.json"
if [[ "$(id -u)" == "0" ]]; then
  chown "$test_uid:$test_gid" "$integration_root/animegonet/data/config/downloaders.private.json"
fi

compose up --detach animegonet >/dev/null
animegonet_port="$(service_port animegonet 7991)"
animegonet_url="http://127.0.0.1:$animegonet_port"
for attempt in $(seq 1 80); do
  if curl --fail --silent "$animegonet_url/ping" >/dev/null; then
    break
  fi
  if [[ "$attempt" == 80 ]]; then
    compose logs --no-color animegonet
    exit 1
  fi
  sleep 0.25
done

animegonet_post() {
  local endpoint="$1"
  local json="${2:-}"
  local arguments=(
    --fail-with-body
    --silent
    --show-error
    --header "X-AnimeGo-Access-Key: $access_key"
  )
  if [[ -n "$json" ]]; then
    arguments+=(--header "Content-Type: application/json" --data "$json")
  else
    arguments+=(--request POST)
  fi
  curl "${arguments[@]}" "$animegonet_url$endpoint"
}

for instance in bt pt; do
  connection_test="$(animegonet_post "/api/v1/downloaders/$instance/test")"
  python3 -c '
import json
import sys
instance = sys.argv[1]
result = json.load(sys.stdin)
assert result["id"] == instance
assert result["connected"] is True
assert result["task_count"] == 0
assert result["client_default_save_path"].rstrip("/") == f"/download/incomplete/{instance}"
' "$instance" <<<"$connection_test"

  path_probe="$(animegonet_post "/api/v1/downloaders/$instance/path-probe")"
  python3 -c '
import json
import sys
instance = sys.argv[1]
result = json.load(sys.stdin)
assert result["id"] == instance
assert result["success"] is True
assert result["hard_link_supported"] is True
assert result["download_path"].rstrip("/") == f"/download/incomplete/{instance}"
assert result["save_path"].rstrip("/") == "/download/anime"
' "$instance" <<<"$path_probe"
done

for source in u2 ttg; do
  create_body="$(
    printf '{"id":"%s-ci","display_name":"%s CI","adapter":"%s","downloader_id":"pt","file_strategy":"link","allowed_torrent_hosts":["%s.invalid"],"category":"animegonet-ci","tags":["animegonet-ci"],"seeding_time_minutes":0,"rss_filter_enabled":true,"rss_priority_enabled":true,"enabled":true}' \
      "$source" "${source^^}" "$source" "$source"
  )"
  created="$(animegonet_post "/api/v1/sources" "$create_body")"
  python3 -c '
import json
import sys
source = sys.argv[1]
result = json.load(sys.stdin)
assert result["id"] == f"{source}-ci"
assert result["downloader_id"] == "pt"
' "$source" <<<"$created"

  preview_body='{"title":"AnimeGoNet CI route","source_work_id":"animegonet-ci"}'
  preview="$(animegonet_post "/api/v1/sources/$source-ci/route-preview" "$preview_body")"
  python3 -c '
import json
import sys
source = sys.argv[1]
result = json.load(sys.stdin)
assert result["valid"] is True
assert result["source_profile_id"] == f"{source}-ci"
assert result["downloader_id"] == "pt"
assert result["download_path"].rstrip("/") == "/download/incomplete/pt"
assert result["save_path"].rstrip("/") == "/download/anime"
' "$source" <<<"$preview"
done

mikan_preview="$(
  animegonet_post \
    "/api/v1/sources/mikan/route-preview" \
    '{"title":"AnimeGoNet CI Mikan","mikanid":3951,"bgmid":547888}'
)"
python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["valid"] is True
assert result["source_profile_id"] == "mikan"
assert result["downloader_id"] == "bt"
assert result["download_path"].rstrip("/") == "/download/incomplete/bt"
assert result["save_path"].rstrip("/") == "/download/anime"
' <<<"$mikan_preview"
