#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: smoke-container.sh IMAGE}"
container_name="animegonet-smoke-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
smoke_root="$(mktemp -d)"
access_key="container-smoke-key"

cleanup() {
  docker rm --force "$container_name" >/dev/null 2>&1 || true
  rm -rf -- "$smoke_root"
}
trap cleanup EXIT

mkdir -p "$smoke_root/data" "$smoke_root/download/incomplete" "$smoke_root/download/anime"
docker run --detach \
  --name "$container_name" \
  --publish 127.0.0.1::7991 \
  --env "access_key=$access_key" \
  --volume "$smoke_root/data:/data" \
  --volume "$smoke_root/download:/download" \
  "$image" >/dev/null

host_port="$(docker port "$container_name" 7991/tcp | awk -F: 'NR==1 {print $NF}')"
for attempt in $(seq 1 40); do
  if curl --fail --silent "http://127.0.0.1:${host_port}/ping" >/dev/null; then
    break
  fi
  if [[ "$attempt" == 40 ]]; then
    docker logs "$container_name"
    exit 1
  fi
  sleep 0.25
done

curl --fail --silent \
  --header "X-AnimeGo-Access-Key: $access_key" \
  "http://127.0.0.1:${host_port}/api/v1/status" \
  | grep --fixed-strings '"native_aot":true' >/dev/null

test -f "$smoke_root/data/animegonet.db"
