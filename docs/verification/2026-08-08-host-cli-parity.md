# Host CLI parity — 2026-08-08

## Pinned upstream

- repository: `weteor/AnimeGo`
- branch/commit: `develop@c7475dfc55a374cd0dd08821bf17125dab1e3145`
- authoritative entry point: `cmd/animego/main.go`

The upstream main program defines exactly four business switches: `config`,
`debug`, `web`, and `backup`. Its environment aliases are `ANIMEGO_CONFIG`,
`ANIMEGO_DEBUG`, `ANIMEGO_WEB`, and `ANIMEGO_CONFIG_BACKUP`.

## Implemented contract

- Both `-name` and `--name` forms are accepted for the four pinned switches.
- A bare boolean switch becomes `true`; explicit `=true` and `=false` are preserved.
- `config` selects the deployment YAML and `backup` controls legacy upgrade backup.
- `debug` lowers both the host filter and rolling-file provider threshold to Debug.
- `web=false` installs a no-listener `IServer`; hosted workers still use the normal
  application lifetime.
- `-h`, `-help`, and `--help` return before configuration, directory, SQLite, or
  network initialization.
- Invalid boolean values fail before runtime directories are created and the value
  is not echoed in the exception.

The project-wide precedence of command line over environment variables is retained
as an intentional deployment consistency rule.

## Automated acceptance

- focused tests cover normalization, help, real single-dash config/backup loading,
  debug file output, headless start/stop, and pre-write invalid-value failure;
- the five-RID NativeAOT workflow invokes `smoke-native-cli.ps1`, which runs help,
  starts a real published headless process, waits for SQLite initialization, and
  proves that an explicitly reserved loopback port has no listener;
- Release build, full .NET regression, win-x64 NativeAOT publish, and local native
  CLI smoke must pass before commit.

## Result

- focused CLI/logging/configuration tests: 15/15 passed;
- Release solution build: 0 warnings, 0 errors;
- final full .NET regression: 1437/1437 passed;
- win-x64 NativeAOT publish: passed without trim/AOT warnings;
- published first-start/API, help/headless zero-listener, and AI metadata worker
  smokes: passed.
