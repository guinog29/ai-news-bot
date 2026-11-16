# CLAUDE.md

Guidance for working in this repository. Rationale for each decision lives in
[docs/decisions.md](docs/decisions.md) (D1–D22); this file is the operating manual.

## What this is

A scheduled job that posts relevant AI news to Discord, in English, at no cost and without failing
silently. It is **not** an always-on bot: GitHub Actions runs it on a cron, it exits, done.
Public repository, also a portfolio piece.

**Status:** v1 is implemented and covered by unit tests, and was smoke-tested against the live feeds
(seed, re-run, simulated new item) with fake webhook URLs. It has **not** yet posted to real Discord
channels, and the GitHub workflows have not run yet.

## Architecture

```
src/AiNewsBot                  console app: arguments, wiring, exit code    -> Core, Infrastructure
src/AiNewsBot.Core             models, interfaces, dedup, NewsJob           -> (no project refs)
src/AiNewsBot.Infrastructure   RSS, Anthropic HTML, Discord, config, state  -> Core
tests/AiNewsBot.Core.Tests
tests/AiNewsBot.Infrastructure.Tests   (fixtures in Fixtures/: saved XML/HTML, no network)
config/feeds.json              sources and limits (versioned, no secrets)
.github/workflows              ci.yml, run.yml (cron job), keepalive.yml
```

Dependency rule: **Core references no other project** (its only package is
`Microsoft.Extensions.Logging.Abstractions`); Infrastructure references only Core; only the entrypoint
wires them together. Architecture tests enforce this.

Key types:

- `IFeedSource` (`FetchAsync`), implemented by `RssFeedSource` (RSS/Atom via
  `System.ServiceModel.Syndication`) and `AnthropicNewsSource` (HTML via AngleSharp). Parsing lives in
  pure `RssFeedParser` / `AnthropicNewsParser`, which is what the fixture tests exercise.
- `NewItemSelector`: pure function deciding what is new (seed, dedup, window, cap, ordering).
- `NewsJob`: one run. Per source: fetch, select, post, update state. Isolates failures per source,
  alerts on `status`, always saves state.
- `INewsPublisher` → `DiscordWebhookPublisher` (embeds, 429/`Retry-After`, retries).
- `IStateStore` → `JsonFileStateStore` (atomic write, sorted keys, LF).
- `FeedConfigLoader` validates `config/feeds.json`; `DiscordWebhookConfig` reads secrets from env.

Flow of a run (D3–D8, D11, D15), as implemented in `run.yml`:

1. Cron `17,47 * * * *` (or `workflow_dispatch`), serialized by a `concurrency` group.
2. Check out the `state` branch into `state-data/` (created as an orphan branch on the first run).
3. `dotnet run -- --state state-data/state.json` runs `NewsJob`.
4. A final `if: always()` step commits `state.json` to the `state` branch if it changed.

## Commands

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet build AiNewsBot.slnx
dotnet test AiNewsBot.slnx
dotnet format AiNewsBot.slnx --verify-no-changes   # drop the flag to fix
dotnet run --project src/AiNewsBot -- --dry-run    # fetch every source and print counts; posts nothing, no state
dotnet run --project src/AiNewsBot -- --state state/state.json   # real run (needs the DISCORD_WEBHOOK_* variables)
```

Arguments: `--config <path>` (default `config/feeds.json`), `--state <path>` (default
`state/state.json`), `--dry-run`. Build, tests and the format check must all pass before committing.
To try a real run without Discord, set the three variables to a fake `https://….invalid/…` URL: the
first run only seeds and posts nothing.

## Conventions

- **Language:** code, comments, commit messages and docs in English. News content is posted
  untranslated (D14).
- **Secrets:** the repo is public. Webhook URLs only come from environment variables / GitHub
  Secrets, never from code, versioned config or logs (D9). Never log a webhook URL, never put one in
  an exception message, and do not use `IHttpClientFactory` (its default logging prints request URLs).
- **Channel keys (D18):** config references a logical key (`summary`, `official`, `status`); the app
  reads the webhook from `DISCORD_WEBHOOK_<KEY>` (upper-cased). A new channel needs no code change.
  `status` is reserved for alerts and cannot be a source's channel.
- **Idempotency (D5–D7, D19):** process everything since the last run, not a time window. Dedup by item
  GUID, else hash of the normalized URL. A source with no state is seeded without posting.
- **Fail loud (D11):** an unhandled error (bad config, missing secret, unreadable or unsavable state)
  exits 1, and so does a run where *every* source failed. Some sources failing is not an error: it is
  counted, and alerts `status` after `failureAlertThreshold` runs in a row. Zero items from a source is
  a failure, not "no news" (D12, D20).
- **State file:** `state.json`, camelCase, `version: 1`. A missing file means seed; a corrupt or
  unknown-version file is an error and is never silently overwritten.
- **Tests:** unit tests only, driven by saved fixtures and fakes (`StubHandler` for HTTP); no network
  access. Test names follow `Method_condition_result`.
- **Style:** nullable enabled, warnings are errors, file-scoped namespaces, `.editorconfig` is the
  source of truth. LF line endings (`.gitattributes`).
- **Dependencies:** prefer the BCL. Microsoft.Extensions.* and System.ServiceModel.Syndication are
  pinned to 10.0.x to match the LTS SDK. Add a package only with a reason, and record it in
  `docs/decisions.md` if it is a real decision.
- **Decisions:** when a decision changes or a new one is made, update `docs/decisions.md` in the same
  commit.
- **Git:** never push without the owner's explicit approval.

## Tunables (`config/feeds.json`, `options`)

| Option | Default | Meaning |
| --- | --- | --- |
| `considerLatest` | 50 | newest N items looked at per source per run |
| `maxSeenPerSource` | 300 | remembered item ids per source (must be ≥ `considerLatest`) |
| `maxPostsPerSourcePerRun` | 5 | most posts per source per run; the excess is dropped (D8) |
| `failureAlertThreshold` | 4 | failed runs in a row before alerting `status` (~2 h at a 30 min cadence) |

## Open questions

Real Hugging Face volume (see whether the cap of 5 per run is hit); whether the keepalive's API call
really resets the 60-day clock (D10, unverified until it has run in production); whether pushes to the
`state` branch count as activity for that rule. See `docs/decisions.md`.
