# AnimeGo.PluginTool

Validate, execute synthetic fixtures against, and deterministically package AnimeGoNet
external executable plugins.

```text
animego-plugin validate <package-directory> [--rid <rid>]
animego-plugin run <package-directory> --fixture <fixture.json> [--rid <rid>] [--data-path <path>] [--timeout-seconds <1..3600>]
animego-plugin pack <package-directory> --output <package.zip> [--rid <rid>] [--force]
```

The tool uses the same strict manifest, configuration schema, process protocol, and typed
result validation as the AnimeGoNet host. Use synthetic fixtures and never store tracker
passkeys, cookies, downloader credentials, or other secrets in fixture files.

See `docs/PLUGIN_ARCHITECTURE.md` in the AnimeGoNet source repository for the fixture
contract, exit codes, package safety limits, and NativeAOT verification process.
