#!/usr/bin/env bash
set -euo pipefail

requested_output="${1:?usage: export-external-plugin-fixture.sh OUTPUT_ROOT [amd64|arm64]}"
target_arch="${2:-amd64}"
case "$target_arch" in
  amd64) expected_rid="linux-x64" ;;
  arm64) expected_rid="linux-arm64" ;;
  *) echo "Unsupported fixture architecture: $target_arch" >&2; exit 1 ;;
esac

output_name="$(basename -- "$requested_output")"
if [[ "$output_name" == "." || "$output_name" == ".." ]]; then
  echo "Fixture output must name a new child directory" >&2
  exit 1
fi
output_parent="$(cd "$(dirname -- "$requested_output")" && pwd)"
output_root="$output_parent/$output_name"
if [[ -e "$output_root" ]]; then
  echo "Fixture output already exists: $output_root" >&2
  exit 1
fi

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture_id="com.animegonet.container-source"
image="animegonet-external-plugin-fixture:${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
container_id=""

cleanup() {
  if [[ -n "$container_id" ]]; then
    docker rm --force "$container_id" >/dev/null 2>&1 || true
  fi
  docker image rm --force "$image" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker build \
  --file "$repository_root/Dockerfile.external-plugin-fixture" \
  --build-arg "TARGETARCH=$target_arch" \
  --tag "$image" \
  "$repository_root" >/dev/null
container_id="$(docker create "$image")"
package_root="$output_root/$fixture_id"
mkdir -p "$package_root"
docker cp "$container_id:/plugin/." "$package_root"
docker rm "$container_id" >/dev/null
container_id=""

find "$package_root" -type l -print -quit | grep -q . \
  && { echo "Fixture export contains a symbolic link" >&2; exit 1; }
find "$package_root" -type d -exec chmod 0555 {} +
find "$package_root" -type f -exec chmod 0444 {} +
chmod 0555 "$package_root/AnimeGoNet.ContainerPluginFixture"

python3 -c '
import json
import pathlib
import sys
root, expected_rid = pathlib.Path(sys.argv[1]), sys.argv[2]
manifest = json.loads((root / "plugin.json").read_text(encoding="utf-8"))
assert manifest["id"] == "com.animegonet.container-source"
assert manifest["type"] == "source"
assert manifest["rid"] == expected_rid
assert manifest["entryPoint"] == "AnimeGoNet.ContainerPluginFixture"
assert (root / manifest["entryPoint"]).is_file()
assert (root / manifest["configSchema"]).is_file()
' "$package_root" "$expected_rid"
