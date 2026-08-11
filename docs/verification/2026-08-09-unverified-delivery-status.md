# Generated but unverified delivery status — 2026-08-09

> 历史状态说明：本文锁定 2026-08-09 的未执行范围。2026-08-11 已完成 linux-x64
> NativeAOT Docker、双 qB、完整链路、外部插件与发布 WebUI 验收，剩余门禁以
> `TODO.md` 和 `2026-08-11-ubuntu-ct-docker-validation.md` 为准。

## Owner-approved boundary

The project owner explicitly allows Docker functionality to remain unexecuted
as long as all intended delivery files are generated for later self-testing.
This is not permission to report unexecuted behavior as passed.

`TODO.md` therefore adds a separate `[~]` state. It means “the feature or gate
is generated, but execution is unverified by owner instruction.” It is neither
`[x]` nor an implementation-in-progress marker. Every `[~]` row must name the
unverified scope.

The porting checklist uses the matching `未验证` status for generated Docker or
remote workflow surfaces. Product modules that already passed unit,
integration, NativeAOT or isolated local qB verification remain `已验证`, while
their container execution is called out separately as unverified.

## Reconciled status

The following generated gates are now explicitly `[~]`:

- pinned upstream Go Linux container baseline;
- isolated qBittorrent container Web API lifecycle;
- dual-qBittorrent unified-ingest dispatch and exact cleanup;
- amd64/arm64 NativeAOT Docker build and runtime smoke;
- non-root/read-only/health/SIGTERM container hardening;
- shared `/download` Compose mapping;
- external qB path probe;
- external NativeAOT C# plugin read-only mount, non-root execution and disable fallback;
- release-container Playwright WebUI E2E;
- full-chain unified ingest, legal qB WebSeed download, metadata, move organization,
  sidecars, API/WebUI projection and qB cleanup container E2E.

Mikan, the local feed-to-download pipeline, move organization, multifile dedup,
per-file TMDB Episode processing and TypeScript/WebUI are marked complete based
on their existing non-Docker evidence. The dual-container unified task driver is
now generated and separately marked unverified. Non-Windows RID execution
remains an ordinary unfinished item. The full-chain container driver is now
generated and separately marked unverified; its fixture has a real local JIT
process test, but neither its AOT image nor Compose path was executed. The
external-plugin container driver is likewise generated and separately marked
unverified.

## Automated evidence

`UnverifiedDeliveryStatusContractTests` locks the status legend, the exact ten
`[~]` rows, required `未验证` wording, completed local modules, checklist status
and the existence of every referenced workflow, Dockerfile, Compose file and
smoke script. It also rejects the stale checklist phrase that implied Docker
runner acceptance was still part of implementation completion.

The focused Release delivery/fixture suite passed 14/14 and the complete
solution passed 1467/1467 with zero skips. Formatting, `git diff --check`, Bash
syntax, YAML parsing and Node syntax passed. No Docker or remote runner command
was executed. Detailed evidence is recorded in
`docs/verification/2026-08-09-full-chain-container-e2e-delivery.md`.
