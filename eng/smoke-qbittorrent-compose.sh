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
test_uid="$(id -u)"
test_gid="$(id -g)"
container_e2e_fixture_image="animegonet-container-e2e-fixture:${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"

if [[ "$test_uid" == "0" ]]; then
  test_uid=10001
  test_gid=10001
fi

export ANIMEGONET_IMAGE="$image"
export ANIMEGONET_INTEGRATION_ROOT="$integration_root"
export ANIMEGONET_ACCESS_KEY="$access_key"
export ANIMEGONET_UID="$test_uid"
export ANIMEGONET_GID="$test_gid"
export ANIMEGONET_CONTAINER_E2E_FIXTURE_IMAGE="$container_e2e_fixture_image"

compose() {
  docker compose \
    --project-name "$project_name" \
    --file "$compose_file" \
    "$@"
}

cleanup() {
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  docker image rm --force "$container_e2e_fixture_image" >/dev/null 2>&1 || true
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
docker build \
  --file "$repository_root/Dockerfile.container-e2e-fixture" \
  --build-arg TARGETARCH=amd64 \
  --tag "$container_e2e_fixture_image" \
  "$repository_root"
"$repository_root/eng/export-external-plugin-fixture.sh" \
  "$integration_root/animegonet/data/plugins" amd64

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
      --header "Host: 127.0.0.1:8080" \
      --header "Referer: http://127.0.0.1:8080/" \
      --header "Origin: http://127.0.0.1:8080" \
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
    --header "Host: 127.0.0.1:8080" \
    --header "Referer: http://127.0.0.1:8080/" \
    --header "Origin: http://127.0.0.1:8080" \
    --cookie "$cookie_jar" \
    "$@" \
    "$base_url$endpoint"
}

authenticated_get() {
  local base_url="$1"
  local cookie_jar="$2"
  local endpoint="$3"
  curl --fail-with-body --silent --show-error \
    --header "Host: 127.0.0.1:8080" \
    --header "Referer: http://127.0.0.1:8080/" \
    --header "Origin: http://127.0.0.1:8080" \
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
    printf '{"save_path":"/download/incomplete/%s","temp_path":"/download/incomplete/%s","temp_path_enabled":false}' \
      "$instance" "$instance"
  )"
  authenticated_post \
    "$base_url" \
    "$cookie_jar" \
    "/api/v2/app/setPreferences" \
    --data-urlencode "json=$preferences" >/dev/null

  rm -f -- "$cookie_jar"
  login "$base_url" "$temporary_password" "$cookie_jar"

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

  printf '%s|%s|%s' "$base_url" "$cookie_jar" "$temporary_password"
}

exercise_fixture() {
  local instance="$1"
  local connection="$2"
  local base_url="${connection%%|*}"
  local connection_tail="${connection#*|}"
  local cookie_jar="${connection_tail%%|*}"
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

compose up --detach torrent-fixture container-e2e-fixture qbittorrent-bt qbittorrent-pt >/dev/null
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
for attempt in $(seq 1 80); do
  if compose exec --no-TTY torrent-fixture \
    wget --quiet --output-document=/dev/null \
    http://container-e2e-fixture.invalid:8089/ready; then
    break
  fi
  if [[ "$attempt" == 80 ]]; then
    compose logs --no-color container-e2e-fixture
    echo "The deterministic full-chain fixture did not become ready" >&2
    exit 1
  fi
  sleep 0.1
done
bt_connection="$(configure_qbittorrent qbittorrent-bt bt)"
pt_connection="$(configure_qbittorrent qbittorrent-pt pt)"
bt_password="${bt_connection##*|}"
pt_password="${pt_connection##*|}"
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
      "password": "$bt_password",
      "download_path": "/download/incomplete/bt",
      "enabled": true,
      "revision": 1,
      "updated_at_utc": "$updated_at"
    },
    "pt": {
      "base_url": "http://qbittorrent-pt:8080",
      "username": "admin",
      "password": "$pt_password",
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

animegonet_get() {
  local endpoint="$1"
  curl --fail-with-body --silent --show-error \
    --header "X-AnimeGo-Access-Key: $access_key" \
    "$animegonet_url$endpoint"
}

animegonet_put() {
  local endpoint="$1"
  local json="$2"
  curl --fail-with-body --silent --show-error \
    --request PUT \
    --header "X-AnimeGo-Access-Key: $access_key" \
    --header "Content-Type: application/json" \
    --data "$json" \
    "$animegonet_url$endpoint"
}

exercise_external_plugin() {
  local plugin_id="com.animegonet.container-source"
  local marker="$integration_root/animegonet/data/plugin-data/$plugin_id/container-smoke.txt"
  local status=""
  local enabled=""
  local created=""
  local rejected=""
  local disabled=""

  status="$(animegonet_get "/api/v1/status")"
  python3 -c '
import json
import sys
plugin_id = sys.argv[1]
external = json.load(sys.stdin)["external_plugins"]
assert external["errors"] == [], external["errors"]
package = next(item for item in external["packages"] if item["id"] == plugin_id)
runtime = next(item for item in external["runtimes"] if item["id"] == plugin_id)
assert package["type"] == "source", package
assert package["rid"] == "linux-x64", package
assert package["configured"] is False, package
assert package["enabled"] is False, package
assert runtime["state"] == "stopped", runtime
' "$plugin_id" <<<"$status"

  enabled="$(animegonet_put "/api/v1/plugins/$plugin_id/configuration" \
    '{"expected_revision":0,"enabled":true,"args":{},"vars":{},"clear_write_only_paths":[]}')"
  python3 -c '
import json
import sys
plugin_id = sys.argv[1]
result = json.load(sys.stdin)
assert result["revision"] == 1
assert result["item"]["id"] == plugin_id
assert result["item"]["enabled"] is True
assert result["item"]["entry_revision"] == 1
' "$plugin_id" <<<"$enabled"

  created="$(animegonet_post "/api/v1/sources" \
    '{"id":"container-source-ci","display_name":"Container source CI","adapter":"com.animegonet.container-source","downloader_id":"bt","file_strategy":"move","allowed_torrent_hosts":["allowed.invalid"],"category":"animegonet-ci-plugin","tags":["animegonet-ci-plugin"],"seeding_time_minutes":0,"rss_filter_enabled":true,"rss_priority_enabled":true,"enabled":true}')"
  python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["id"] == "container-source-ci"
assert result["adapter"] == "com.animegonet.container-source"
assert result["downloader_id"] == "bt"
' <<<"$created"

  rejected="$(animegonet_post "/api/v1/ingest" \
    '{"source":"container-source-ci","data":[{"torrent":"https://not-allowed.invalid/container-smoke.torrent","info":{"title":"Container plugin smoke","source_item_id":"container-plugin-smoke","source_work_id":"container-plugin-work"}}]}')"
  python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["source"] == "container-source-ci", result
assert result["accepted_count"] == 0, result
assert result["rejected_count"] == 1, result
item = result["items"][0]
assert item["status"] == "rejected", item
assert item["ingest_id"] is None, item
assert item["errors"] == ["torrent staging failed: HostNotAllowed"], item["errors"]
' <<<"$rejected"
  if [[ "$rejected" == *"not-allowed.invalid"* ]]; then
    echo "External plugin ingest response leaked the synthetic Torrent URL" >&2
    exit 1
  fi

  test -f "$marker"
  grep --fixed-strings --line-regexp "uid=$test_uid" "$marker" >/dev/null
  grep --fixed-strings --line-regexp "package_read_only=true" "$marker" >/dev/null

  status="$(animegonet_get "/api/v1/status")"
  python3 -c '
import json
import sys
plugin_id = sys.argv[1]
external = json.load(sys.stdin)["external_plugins"]
package = next(item for item in external["packages"] if item["id"] == plugin_id)
runtime = next(item for item in external["runtimes"] if item["id"] == plugin_id)
assert package["configured"] is True
assert package["enabled"] is True
assert runtime["state"] == "ready"
assert runtime["consecutive_failures"] == 0
assert runtime["last_failure_code"] is None
' "$plugin_id" <<<"$status"

  disabled="$(animegonet_put "/api/v1/plugins/$plugin_id/configuration" \
    '{"expected_revision":1,"enabled":false,"args":{},"vars":{},"clear_write_only_paths":[]}')"
  python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["revision"] == 2
assert result["item"]["enabled"] is False
assert result["item"]["entry_revision"] == 2
' <<<"$disabled"
  rm -f -- "$marker"

  rejected="$(animegonet_post "/api/v1/ingest" \
    '{"source":"container-source-ci","data":[{"torrent":"https://not-allowed.invalid/disabled.torrent","info":{"title":"Disabled container plugin smoke","source_item_id":"container-plugin-disabled","source_work_id":"container-plugin-work"}}]}')"
  python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["accepted_count"] == 0
assert result["rejected_count"] == 1
item = result["items"][0]
assert item["status"] == "rejected"
assert item["ingest_id"] is None
assert len(item["errors"]) == 1
assert "unavailable" in item["errors"][0].lower()
' <<<"$rejected"
  test ! -e "$marker"

  status="$(animegonet_get "/api/v1/status")"
  python3 -c '
import json
import sys
plugin_id = sys.argv[1]
external = json.load(sys.stdin)["external_plugins"]
package = next(item for item in external["packages"] if item["id"] == plugin_id)
runtime = next(item for item in external["runtimes"] if item["id"] == plugin_id)
assert package["enabled"] is False
assert runtime["state"] == "stopped"
' "$plugin_id" <<<"$status"
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

fixture_get() {
  local endpoint="$1"
  compose exec --no-TTY torrent-fixture \
    wget --quiet --output-document=- \
    "http://container-e2e-fixture.invalid:8089$endpoint"
}

exercise_full_chain_e2e() {
  local profile="container-e2e-ci"
  local category="animegonet-ci-full-chain"
  local tag="animegonet-ci-full-chain"
  local ready=""
  local expected_hash=""
  local expected_name=""
  local expected_size=""
  local expected_sha256=""
  local created=""
  local ingest=""
  local downloads=""
  local task_id=""
  local job_id=""
  local detail=""
  local library=""
  local fixture_state=""
  local target="$integration_root/download/anime/AnimeGoNet Container E2E/S01/E001.mkv"

  ready="$(fixture_get /ready)"
  IFS='|' read -r expected_hash expected_name expected_size expected_sha256 < <(
    python3 -c '
import json
import sys
value = json.load(sys.stdin)
print("|".join((value["info_hash"], value["file_name"], str(value["size_bytes"]), value["payload_sha256"])))
' <<<"$ready"
  )
  [[ -n "$expected_hash" && "$expected_size" == "131072" ]]

  prepare_route_identity "$bt_connection" bt "$category" "$tag"
  created="$(animegonet_post "/api/v1/sources" \
    '{"id":"container-e2e-ci","display_name":"Container full-chain E2E","adapter":"mikan","downloader_id":"bt","file_strategy":"move","allowed_torrent_hosts":["container-e2e-fixture.invalid"],"category":"animegonet-ci-full-chain","tags":["animegonet-ci-full-chain"],"seeding_time_minutes":0,"rss_filter_enabled":true,"rss_priority_enabled":true,"enabled":true}')"
  python3 -c '
import json
import sys
result = json.load(sys.stdin)
assert result["id"] == "container-e2e-ci"
assert result["adapter"] == "mikan"
assert result["downloader_id"] == "bt"
assert result["file_strategy"] == "move"
' <<<"$created"

  ingest="$(animegonet_post "/api/v1/ingest" \
    '{"source":"container-e2e-ci","data":[{"torrent":"http://container-e2e-fixture.invalid:8089/animegonet-container-e2e.torrent","info":{"title":"AnimeGoNet Container E2E S01E01","source_item_id":"container-e2e-episode-1","source_work_id":"9901","mikanid":9901,"bgmid":990001}}]}')"
  assert_unified_ingest_response "$ingest" "$profile" bt "$expected_hash"
  task_id="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["items"][0]["ingest_id"])' <<<"$ingest")"

  for attempt in $(seq 1 360); do
    downloads="$(animegonet_get "/api/v1/downloads?page=1&page_size=100&search=AnimeGoNet%20Container%20E2E")"
    job_id="$(python3 -c '
import json
import sys
task_id = sys.argv[1]
items = [item for item in json.load(sys.stdin)["items"] if item["task_id"] == task_id]
print(items[0]["job_id"] if len(items) == 1 and items[0]["business_status"] == "organized" else "")
' "$task_id" <<<"$downloads")"
    [[ -n "$job_id" ]] && break
    if [[ "$attempt" == 360 ]]; then
      compose logs --no-color animegonet || true
      authenticated_get "${bt_connection%%|*}" "${bt_connection#*|}" \
        "/api/v2/torrents/info?hashes=$expected_hash" || true
      animegonet_get "/api/v1/metadata/tasks/$task_id" || true
      fixture_get /__state || true
      echo "Full-chain task $task_id did not reach organized within 180 seconds" >&2
      exit 1
    fi
    sleep 0.5
  done

  python3 -c '
import json
import sys
task_id, job_id, expected_hash, expected_size = sys.argv[1:]
item = next(item for item in json.load(sys.stdin)["items"] if item["task_id"] == task_id)
assert item["job_id"] == job_id
assert item["source"] == "container-e2e-ci"
assert item["downloader_id"] == "bt"
assert item["info_hash"] == expected_hash
assert item["business_status"] == "organized"
assert item["progress"] == 1
assert item["total_bytes"] == int(expected_size)
' "$task_id" "$job_id" "$expected_hash" "$expected_size" <<<"$downloads"

  detail="$(animegonet_get "/api/v1/metadata/tasks/$task_id")"
  python3 -c '
import json
import sys
task_id = sys.argv[1]
value = json.load(sys.stdin)
summary = value["summary"]
assert summary["task_id"] == task_id
assert summary["status"] == "organized"
assert summary["tmdb_series_id"] == 990001
assert summary["tmdb_season_number"] == 1
assert summary["series_strategy"] == "tmdb_title"
assert summary["season_strategy"] == "tmdb_air_date"
assert summary["episode_strategy"] == "tmdb_episode_number"
assert summary["episode_file_count"] == 1
file = value["files"][0]
assert file["source_name"] == "AnimeGoNet.Container.E2E.S01E01.mkv"
assert file["disposition"] == "episode"
assert file["tmdb_series_id"] == 990001
assert file["tmdb_season_number"] == 1
assert file["tmdb_episode_number"] == 1
' "$task_id" <<<"$detail"

  library="$(animegonet_get "/api/v1/library/seasons?page=1&page_size=12&sort=last_updated&direction=desc")"
  python3 -c '
import json
import sys
items = [item for item in json.load(sys.stdin)["items"]
         if item["tmdb_series_id"] == 990001 and item["tmdb_season_number"] == 1]
assert len(items) == 1
item = items[0]
assert item["display_name"] == "AnimeGoNet Container E2E"
assert item["episode_downloaded"] == 1
assert item["episode_total"] == 1
' <<<"$library"

  test -f "$target"
  test "$(wc -c <"$target" | tr -d ' ')" == "$expected_size"
  test "$(sha256sum "$target" | awk '{print $1}')" == "$expected_sha256"
  test -f "$integration_root/download/anime/AnimeGoNet Container E2E/tvshow.nfo"
  test -f "$integration_root/download/anime/AnimeGoNet Container E2E/anime.a_json"
  test -f "$integration_root/download/anime/AnimeGoNet Container E2E/S01/anime.s_json"
  test -f "$integration_root/download/anime/AnimeGoNet Container E2E/S01/E001.e_json"

  fixture_state="$(fixture_get /__state)"
  python3 -c '
import json
import sys
value = json.load(sys.stdin)
for key in ("torrent_requests", "payload_requests", "tmdb_search_requests",
            "tmdb_series_requests", "tmdb_season_requests", "tmdb_episode_requests",
            "bangumi_subject_requests"):
    assert value[key] >= 1, (key, value[key])
assert value["tmdb_credential_failures"] == 0
' <<<"$fixture_state"

  for connection in "$bt_connection" "$pt_connection"; do
    local base_url="${connection%%|*}"
    local cookie_jar="${connection#*|}"
    local tasks=""
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$expected_hash")"
    python3 -c 'import json,sys; assert json.load(sys.stdin) == []' <<<"$tasks"
  done
  authenticated_post "${bt_connection%%|*}" "${bt_connection#*|}" \
    "/api/v2/torrents/deleteTags" --data-urlencode "tags=$tag" >/dev/null
  authenticated_post "${bt_connection%%|*}" "${bt_connection#*|}" \
    "/api/v2/torrents/removeCategories" --data-urlencode "categories=$category" >/dev/null

  export ANIMEGONET_FULL_CHAIN_TASK_ID="$task_id"
  export ANIMEGONET_FULL_CHAIN_TITLE="AnimeGoNet Container E2E"
  export ANIMEGONET_FULL_CHAIN_TMDB_SERIES_ID="990001"
}

exercise_external_plugin

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
exercise_full_chain_e2e

for connection in "$bt_connection" "$pt_connection"; do
  base_url="${connection%%|*}"
  cookie_jar="${connection#*|}"
  for hash in "$bt_hash" "$pt_hash"; do
    tasks="$(authenticated_get "$base_url" "$cookie_jar" "/api/v2/torrents/info?hashes=$hash")"
    python3 -c 'import json,sys; assert json.load(sys.stdin) == []' <<<"$tasks"
  done
done

if [[ "${ANIMEGONET_FULL_CHAIN_WEBUI:-0}" == "1" ]]; then
  export ANIMEGONET_WEBUI_BASE_URL="$animegonet_url"
  export ANIMEGONET_WEBUI_ACCESS_KEY="$access_key"
  npm run web:e2e:full-chain
fi
