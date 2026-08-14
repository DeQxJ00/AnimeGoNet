# inner_plugin_mikan AccessKey configuration verification

## Canonical configuration

The AnimeGoHelper/Mikan plugin credential now belongs to the built-in plugin namespace:

```yaml
inner_plugin_mikan:
  access_key: '123456'
```

`web` contains only WebUI/binding configuration. New native and Docker YAML no longer
generate `web.access_key`. Runtime startup still accepts old `web.access_key`, flat
`access_key`, and the previous environment aliases so an old file can start; saving the
internal plugin card writes the canonical node and removes the old field.

Docker Compose maps `ANIMEGONET_ACCESS_KEY` to
`inner_plugin_mikan__access_key`. Standalone container smoke tests use the same canonical
double-underscore environment path.

## Verification

- TypeScript strict check and 29/29 WebUI tests passed.
- 293/293 targeted deployment, migration, API, Docker contract and WebUI tests passed.
- Release solution build completed with zero warnings and zero errors.
- The local deployment file was migrated without changing the separately configured
  `web.webui_access_key`.
- Runtime smoke confirmed both keys configured, legacy `web.access_key` absent, WebUI and
  plugin requests accepted only by their respective credentials.
- Chromium displayed `inner_plugin_mikan.access_key=123456` in the internal plugin card,
  preserved the independent WebUI key, and reported zero console errors.
