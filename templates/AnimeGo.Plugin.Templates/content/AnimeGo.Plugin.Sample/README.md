# AnimeGo.Plugin.Sample

This project is a minimal `__PLUGIN_TYPE__` external plugin for AnimeGoNet. It uses the
typed `AnimeGo.Plugin.Sdk` protocol loop and `System.Text.Json` source generation, so
the executable can be published with NativeAOT.

Generate another plugin type with .NET 10's namespaced template parameter (or the
equivalent short option `-t filter`):

```powershell
dotnet new animego-plugin --param:type filter --plugin-id com.example.filter
```

Build the current platform package with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

`plugin.json` must describe the same ID, version, type and capabilities as `Program`.
Secrets belong in `config.schema.json` properties marked `writeOnly`; never put them in
the manifest or source tree. The included workflow publishes native packages for all
five AnimeGoNet RIDs and rewrites the RID-specific entry point in each artifact.
