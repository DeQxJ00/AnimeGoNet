# Container runtime hardening smoke — 2026-08-08

> Status update (2026-08-11): this report preserves the verification boundary
> at generation time. The linux-x64 image has since passed the Ubuntu 24.04
> x86_64 CT runtime smoke, including non-root execution, read-only rootfs,
> healthcheck, SQLite creation and SIGTERM cleanup. See
> `2026-08-11-ubuntu-ct-docker-validation.md`. linux-arm64 remains unverified.

## Runtime contract

`eng/smoke-container.sh` now starts the actual `animegonet:ci` NativeAOT image
with the runner's non-root UID/GID rather than relying on the image's built-in
10001 identity. If the smoke itself runs as root, it selects and owns the
fixture tree for disposable UID/GID 12345. The container is always launched
with:

- a read-only root filesystem;
- `/tmp` as a bounded `rw,nosuid,nodev,noexec` tmpfs;
- `no-new-privileges`;
- only the `mktemp` data and shared-download fixtures mounted writable;
- loopback-only random host port publication and a disposable Access Key.

The smoke inspects the effective Docker HostConfig, then verifies the exact
non-root UID/GID inside the container. It creates and removes one recognizable
probe in `/data`, `/download` and `/tmp`, waits for the image healthcheck to
become healthy, checks the NativeAOT status API and SQLite creation, and sends
SIGTERM. Exit must occur within seven seconds with code zero; the mounted
database must remain present. Cleanup removes only the named smoke container and
its `mktemp` root.

## Evidence

`DockerQbittorrentIntegrationContractTests` locks the Dockerfile, Compose,
workflow and smoke flags/inspections/write probes/SIGTERM assertions together.
The shell syntax check and targeted contract passed 3/3; the complete solution
passed 1367/1367 with zero failures and zero skips. Actual namespace, mount and
health behavior runs in the existing Ubuntu Docker workflow and remains pending
until that runner result is observed. No local TestSpace, qB profile, Torrent
URL or credential is used by this base-container smoke.
