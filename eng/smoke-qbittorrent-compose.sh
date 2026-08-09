#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-qbittorrent-compose.sh IMAGE}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repository_root/docker-compose.qbittorrent-integration.yml"
fixture_base64="$repository_root/tests/fixtures/animegonet-ci.torrent.b64"
fixture_pt_base64="$repository_root/tests/fixtures/animegonet-ci-pt.torrent.b64"
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
  "$integration_root/fixtures" \
  "$integration_root/download/incomplete/bt" \
  "$integration_root/download/incomplete/pt" \
  "$integration_root/download/anime"

if [[ "$(id -u)" == "0" ]]; then
  chown -R "$test_uid:$test_gid" "$integration_root"
fi

base64 --decode "$fixture_base64" >"$integration_root/animegonet-ci.torrent"
base64 --decode "$fixture_base64" >"$integration_root/fixtures/animegonet-ci.torrent"
base64 --decode "$fixture_pt_base64" >"$integration_root/fixtures/animegonet-ci-pt.torrent"

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

compose up --detach torrent-fixture qbittorrent-bt qbittorrent-pt >/dev/null
for attempt in $(seq 1 40); do
  if compose exec --no-TTY torrent-fixture \
    wget --quiet --output-document=/dev/null \
    http://127.0.0.1:8088/animegonet-ci.torrent; then
    break
  fi
  if [[ "$attempt" == 40 ]]; then
    compose logs --no-color torrent-fixture
    echo "The isolated Torrent fixture service did not become ready" >&2
    exit 1
  fi
  sleep 0.1
done
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

prepare_route_identity() {
  local connection="$1"
  local instance="$2"
  local category="$3"
  local tag="$4"
  local base_url="${connection%%|*}"
  local cookie_jar="${connection#*|}"

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/createCategory" \
    --data-urlencode "category=$category" \
    --data-urlencode "savePath=/download/incomplete/$instance" >/dev/null
  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/createTags" \
    --data-urlencode "tags=$tag" >/dev/null
}

assert_unified_ingest_response() {
  local response="$1"
  local source_profile="$2"
  local downloader="$3"
  local expected_hash="$4"

  python3 -c '
import json
import sys
source_profile, downloader, expected_hash = sys.argv[1:]
result = json.load(sys.stdin)
assert result["source"] == source_profile
assert result["accepted_count"] == 1
assert result["rejected_count"] == 0
assert len(result["items"]) == 1
item = result["items"][0]
assert item["status"] == "staged"
assert item["ingest_id"]
assert item["source_profile_id"] == source_profile
assert item["source_profile_revision"] >= 1
assert item["downloader_id"] == downloader
assert item["info_hash"] == expected_hash
assert item["file_count"] == 1
assert len(item["torrent_url_fingerprint"]) == 64
assert item["errors"] == []
' "$source_profile" "$downloader" "$expected_hash" <<<"$response"

  if [[ "$response" == *"torrent-fixture.invalid"* || "$response" == *"http://"* ]]; then
    echo "Unified ingest response leaked the fixture Torrent URL" >&2
    exit 1
  fi
}

wait_for_routed_task() {
  local connection="$1"
  local other_connection="$2"
  local instance="$3"
  local hash="$4"
  local category="$5"
  local tag="$6"
  local expected_name="$7"
  local expected_size="$8"
  local base_url="${connection%%|*}"
  local cookie_jar="${connection#*|}"
  local other_base_url="${other_connection%%|*}"
  local other_cookie_jar="${other_connection#*|}"
  local tasks=""
  local other_tasks=""

  for attempt in $(seq 1 120); do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    if python3 -c 'import json,sys; sys.exit(0 if len(json.load(sys.stdin)) == 1 else 1)' \
      <<<"$tasks"; then
      break
    fi
    if [[ "$attempt" == 120 ]]; then
      compose logs --no-color animegonet
      echo "Unified ingest did not dispatch $hash to qBittorrent $instance" >&2
      exit 1
    fi
    sleep 0.25
  done

  python3 -c '
import json
import sys
instance, expected_hash, category, tag = sys.argv[1:]
item = json.load(sys.stdin)[0]
tags = {value.strip() for value in item["tags"].split(",") if value.strip()}
assert item["hash"] == expected_hash
assert item["category"] == category
assert tag in tags
assert item["save_path"].rstrip("/") == f"/download/incomplete/{instance}"
assert item["state"].lower().startswith(("stopped", "paused"))
' "$instance" "$hash" "$category" "$tag" <<<"$tasks"

  local files=""
  files="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/files?hash=$hash")"
  python3 -c '
import json
import sys
expected_name, expected_size = sys.argv[1], int(sys.argv[2])
files = json.load(sys.stdin)
assert len(files) == 1
assert files[0]["name"] == expected_name
assert files[0]["size"] == expected_size
' "$expected_name" "$expected_size" <<<"$files"

  other_tasks="$(
    authenticated_get \
      "$other_base_url" "$other_cookie_jar" "/api/v2/torrents/info?hashes=$hash"
  )"
  python3 -c 'import json,sys; assert json.load(sys.stdin) == []' <<<"$other_tasks"
}

cleanup_routed_task() {
  local connection="$1"
  local hash="$2"
  local category="$3"
  local tag="$4"
  local base_url="${connection%%|*}"
  local cookie_jar="${connection#*|}"
  local tasks=""

  authenticated_post \
    "$base_url" "$cookie_jar" "/api/v2/torrents/delete" \
    --data-urlencode "hashes=$hash" \
    --data-urlencode "deleteFiles=true" >/dev/null
  for attempt in $(seq 1 40); do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    if python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin) == [] else 1)' \
      <<<"$tasks"; then
      break
    fi
    if [[ "$attempt" == 40 ]]; then
      echo "Unified ingest fixture $hash was not deleted" >&2
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
    printf '{"id":"%s-ci","display_name":"%s CI","adapter":"%s","downloader_id":"pt","file_strategy":"link","allowed_torrent_hosts":["%s.invalid","torrent-fixture.invalid"],"category":"animegonet-ci-route-pt","tags":["animegonet-ci-route-pt"],"seeding_time_minutes":0,"rss_filter_enabled":true,"rss_priority_enabled":true,"enabled":true}' \
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

mikan_created="$(animegonet_post "/api/v1/sources" \
  '{"id":"mikan-ci","display_name":"Mikan CI","adapter":"mikan","downloader_id":"bt","file_strategy":"move","allowed_torrent_hosts":["torrent-fixture.invalid"],"category":"animegonet-ci-route-bt","tags":["animegonet-ci-route-bt"],"seeding_time_minutes":0,"rss_filter_enabled":true,"rss_priority_enabled":true,"enabled":true}')"
python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["id"] == "mikan-ci"
assert result["adapter"] == "mikan"
assert result["downloader_id"] == "bt"
assert result["file_strategy"] == "move"
' <<<"$mikan_created"

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

bt_category="animegonet-ci-route-bt"
pt_category="animegonet-ci-route-pt"
bt_tag="animegonet-ci-route-bt"
pt_tag="animegonet-ci-route-pt"
bt_hash="bcff48bafa9434c0062a4c2a45ed885f26701721"
pt_hash="9356dbb012e7d8a6999badefacfc74dd1d00593e"

prepare_route_identity "$bt_connection" bt "$bt_category" "$bt_tag"
prepare_route_identity "$pt_connection" pt "$pt_category" "$pt_tag"

mikan_ingest="$(
  animegonet_post "/api/v1/ingest" \
    '{"source":"mikan-ci","data":[{"torrent":"http://torrent-fixture.invalid:8088/animegonet-ci.torrent","info":{"title":"AnimeGoNet CI Mikan S01E01","source_item_id":"mikan-ci-episode-1","source_work_id":"3951","mikanid":3951,"bgmid":547888}}]}'
)"
assert_unified_ingest_response "$mikan_ingest" mikan-ci bt "$bt_hash"

u2_ingest="$(
  animegonet_post "/api/v1/ingest" \
    '{"source":"u2-ci","data":[{"torrent":"http://torrent-fixture.invalid:8088/animegonet-ci-pt.torrent","info":{"title":"AnimeGoNet CI U2 S01E02","source_item_id":"u2-ci-episode-2","source_work_id":"u2-ci-work"}}]}'
)"
assert_unified_ingest_response "$u2_ingest" u2-ci pt "$pt_hash"

wait_for_routed_task \
  "$bt_connection" "$pt_connection" bt "$bt_hash" "$bt_category" "$bt_tag" \
  animegonet-ci.bin 5
wait_for_routed_task \
  "$pt_connection" "$bt_connection" pt "$pt_hash" "$pt_category" "$pt_tag" \
  animegonet-ci-pt.bin 7

cleanup_routed_task "$bt_connection" "$bt_hash" "$bt_category" "$bt_tag"
cleanup_routed_task "$pt_connection" "$pt_hash" "$pt_category" "$pt_tag"

for connection in "$bt_connection" "$pt_connection"; do
  base_url="${connection%%|*}"
  cookie_jar="${connection#*|}"
  for hash in "$bt_hash" "$pt_hash"; do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    python3 -c 'import json,sys; assert json.load(sys.stdin) == []' <<<"$tasks"
  done
done
