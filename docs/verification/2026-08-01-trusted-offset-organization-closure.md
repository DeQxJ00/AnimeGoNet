# Trusted offset → subtitle → organization closure (2026-08-01)

## Scope

This fixture connects the previously isolated metadata, download-preparation and
organization stages for a Mikan task. It uses SQLite, the real processors, rename
plugin, safe mover and directory writers, a fake qBittorrent client, and disposable
files. It does not access the local TestSpace qBittorrent, a Torrent URL, passkey,
cookie, TMDB/Bangumi endpoint, AI provider, or user API key.

## End-to-end evidence

1. Three different source Episodes establish the trusted `(mikanid=3951,
   groupid=7, tmdbid=72517, S02, offset=+13)` signature.
2. Source video EP4 and its `.zh-Hans.ass` subtitle resolve locally to TMDB EP17.
3. AI, TMDB search, and TMDB Episode request counts remain zero.
4. Subtitle association inherits EP17 and stores `.zh-Hans.ass` as the rename suffix.
5. Download preparation validates the immutable fake-qB manifest, assigns wanted
   priority 1 to both files, and resumes the captured task.
6. The test writes the completed files under the job's persisted download root;
   snapshot sync marks the task downloaded.
7. The real organization processor moves them to `来自深渊/S02/E017.mkv` and
   `E017.zh-Hans.ass` under the persisted save root.
8. Exactly one Episode completion/alias is created; the subtitle does not create a
   duplicate completion.
9. The independent cleanup claim calls the downloader with `deleteFiles=false`.

## Targeted test

`AutomaticMetadataResolutionProcessorTests.TrustedMikanOffsetBypassesAiAndTmdbEpisodeRequests`
passed 1/1 after being extended through the complete fake-qB/file-system closure.

## Release gate

- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1092/1092: Plugin Abstractions 11, Core 324,
  Data 172, App 585.
- Production code is unchanged from preceding commit `81143af`, whose `win-x64`
  NativeAOT publish generated native code and passed the isolated schema-v36
  first-start smoke. This module changes only the cross-stage test and traceability
  documentation.
