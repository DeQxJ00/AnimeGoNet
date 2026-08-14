# Configurable trusted Mikan EP offset threshold

The trusted offset source episode is the positive integer EP parsed locally from each Torrent video filename (`file_episode_candidate`). It is not the Bangumi Episode number and is never supplied by the caller or the AI response. The program calculates `target TMDB EP = filename EP + offset` only after TMDB verification.

The WebUI now presents **可信 EP Offset** in a dedicated configuration fieldset outside **匹配与兜底**. The cache remains disabled by default. Its distinct filename-EP threshold is configurable from 1 through 100 and defaults to 3; repeated evidence for the same filename EP does not increase the count.

Schema migration 51 relaxes the stored evidence-count constraint so an explicitly selected threshold below 3 can be represented. Runtime reads, learning promotion, cached resolution and `/api/v1/mikan/trusted-offsets` progress all use the same effective threshold. Raising the threshold immediately makes an under-threshold historical record ineligible without deleting its evidence.

Verification covers:

- two distinct filename EP observations becoming trusted at threshold 2;
- the same record becoming ineligible when read with threshold 3;
- API progress returning the configured threshold and effective state;
- private configuration persistence and rejection of values outside 1–100;
- schema migration/idempotence and the new positive evidence-count constraint;
- the static WebUI explanation, dedicated editor and generated TypeScript client.
