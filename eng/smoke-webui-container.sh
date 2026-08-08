#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-webui-container.sh IMAGE}"
container_name="animegonet-webui-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
smoke_root="$(mktemp -d)"
access_key="animegonet-webui-e2e-key"
test_uid="$(id -u)"
test_gid="$(id -g)"
if [[ "$test_uid" == 0 ]]; then
  test_uid=12345
  test_gid=12345
fi

cleanup() {
  status=$?
  if [[ "$status" != 0 ]]; then
    docker logs "$container_name" 2>/dev/null || true
  fi
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
  --env "access_key=$access_key" \
  --volume "$smoke_root/data:/data" \
  --volume "$smoke_root/download:/download" \
  "$image" >/dev/null

host_port="$(docker port "$container_name" 7991/tcp | awk -F: 'NR==1 {print $NF}')"
for attempt in $(seq 1 160); do
  if curl --fail --silent "http://127.0.0.1:${host_port}/ping" >/dev/null; then
    break
  fi
  if [[ "$attempt" == 160 ]]; then
    exit 1
  fi
  sleep 0.25
done

export ANIMEGONET_WEBUI_BASE_URL="http://127.0.0.1:${host_port}"
export ANIMEGONET_WEBUI_ACCESS_KEY="$access_key"
npm run web:e2e

docker stop --signal SIGTERM --time 7 "$container_name" >/dev/null
exit_code="$(docker inspect --format '{{.State.ExitCode}}' "$container_name")"
if [[ "$exit_code" != 0 ]]; then
  echo "AnimeGoNet WebUI fixture did not exit cleanly: $exit_code" >&2
  exit 1
fi
