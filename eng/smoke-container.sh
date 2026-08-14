#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-container.sh IMAGE}"
container_name="animegonet-smoke-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
smoke_root="$(mktemp -d)"
plugin_access_key="container-plugin-smoke-key"
webui_access_key="container-webui-smoke-key"
test_uid="$(id -u)"
test_gid="$(id -g)"
if [[ "$test_uid" == 0 ]]; then
  test_uid=12345
  test_gid=12345
fi

cleanup() {
  docker rm --force "$container_name" >/dev/null 2>&1 || true
  rm -rf -- "$smoke_root"
}
trap cleanup EXIT

mkdir -p "$smoke_root/data" "$smoke_root/download/incomplete" "$smoke_root/download/anime"
if [[ "$(id -u)" == 0 ]]; then
  chown -R "$test_uid:$test_gid" "$smoke_root"
fi
docker run --detach \
  --name "$container_name" \
  --publish 127.0.0.1::7991 \
  --user "$test_uid:$test_gid" \
  --read-only \
  --tmpfs /tmp:rw,nosuid,nodev,noexec,size=64m \
  --security-opt no-new-privileges:true \
  --env "access_key=$plugin_access_key" \
  --env "webui_access_key=$webui_access_key" \
  --volume "$smoke_root/data:/data" \
  --volume "$smoke_root/download:/download" \
  "$image" >/dev/null

host_port="$(docker port "$container_name" 7991/tcp | awk -F: 'NR==1 {print $NF}')"
for attempt in $(seq 1 160); do
  if curl --fail --silent "http://127.0.0.1:${host_port}/ping" >/dev/null; then
    break
  fi
  if [[ "$attempt" == 160 ]]; then
    docker logs "$container_name"
    exit 1
  fi
  sleep 0.25
done

[[ "$(docker inspect --format '{{.Config.User}}' "$container_name")" == "$test_uid:$test_gid" ]]
[[ "$(docker inspect --format '{{.HostConfig.ReadonlyRootfs}}' "$container_name")" == true ]]
docker inspect --format '{{json .HostConfig.Tmpfs}}' "$container_name" \
  | grep --fixed-strings '"/tmp"' >/dev/null
docker inspect --format '{{json .HostConfig.SecurityOpt}}' "$container_name" \
  | grep --fixed-strings 'no-new-privileges' >/dev/null

docker exec "$container_name" sh -eu -c '
  test "$(id -u)" -ne 0
  test "$(id -u)" = "$1"
  test "$(id -g)" = "$2"
  touch /data/.animegonet-smoke-write
  touch /download/.animegonet-smoke-write
  touch /tmp/.animegonet-smoke-write
  rm /data/.animegonet-smoke-write /download/.animegonet-smoke-write /tmp/.animegonet-smoke-write
' sh "$test_uid" "$test_gid"

for attempt in $(seq 1 160); do
  health="$(docker inspect --format '{{.State.Health.Status}}' "$container_name")"
  if [[ "$health" == healthy ]]; then
    break
  fi
  if [[ "$attempt" == 160 ]]; then
    docker inspect "$container_name"
    docker logs "$container_name"
    exit 1
  fi
  sleep 0.25
done

curl --fail --silent \
  --header "X-AnimeGo-WebUI-Access-Key: $webui_access_key" \
  "http://127.0.0.1:${host_port}/api/v1/status" \
  | grep --fixed-strings '"native_aot":true' >/dev/null

test -f "$smoke_root/data/animegonet.db"

docker stop --signal SIGTERM --time 7 "$container_name" >/dev/null
exit_code="$(docker inspect --format '{{.State.ExitCode}}' "$container_name")"
if [[ "$exit_code" != 0 ]]; then
  docker logs "$container_name"
  echo "AnimeGoNet did not exit cleanly after SIGTERM: $exit_code" >&2
  exit 1
fi

test -f "$smoke_root/data/animegonet.db"
