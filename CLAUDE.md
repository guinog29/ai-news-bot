# CLAUDE.md

Guidance for working in this repository. Rationale for each decision lives in
[docs/decisions.md](docs/decisions.md) (D1–D18); this file is the operating manual.

## What this is

A scheduled job that posts relevant AI news to Discord, in English, at no cost and without failing
silently. It is **not** an always-on bot: GitHub Actions runs it on a cron, it exits, done.
Public repository, also a portfolio piece.

**Status:** scaffold only. The solution compiles and tests pass, but there is no business logic yet.
Do not assume anything described under "Planned" exists.

## Architecture

```
src/AiNewsBot                  console app, entrypoint of the job   -> Core, Infrastructure
src/AiNewsBot.Core             models, IFeedSource, dedup, rules    -> (nothing)
src/AiNewsBot.Infrastructure   RSS, Anthropic HTML, Discord, state  -> Core
tests/AiNewsBot.Core.Tests
tests/AiNewsBot.Infrastructure.Tests   (fixtures in tests/*/Fixtures: saved XML/HTML, no network)
```

Dependency rule: **Core depends on nothing**; Infrastructure depends only on Core; only the
entrypoint wires them together. Architecture tests enforce the first two.

Planned flow of one run (D3–D8, D11, D15):

1. GitHub Actions fires on cron `17,47 * * * *` or `workflow_dispatch`.
2. Load feed config (versioned file) and previous state (JSON on the `state` branch).
3. Per source: fetch, parse, diff against state, keep only new items.
4. Post new items as embeds, oldest first, respecting Discord rate limits.
5. Commit updated state to the `state` branch.
6. Alert on `status` if a source failed N times in a row.

Planned, not built: feed config file, `IFeedSource` + RSS source, Anthropic HTML collector,
Discord webhook client, state store, workflows (job + keepalive), CI.

## Commands

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet build AiNewsBot.slnx
dotnet test AiNewsBot.slnx
dotnet format AiNewsBot.slnx --verify-no-changes   # drop the flag to fix
dotnet run --project src/AiNewsBot
```

Build, tests and the format check must all pass before committing.

## Conventions

- **Language:** code, comments, commit messages and docs in English. News content is posted
  untranslated (D14).
- **Secrets:** the repo is public. Webhook URLs only come from environment variables / GitHub
  Secrets, never from code, versioned config or logs (D9). Never log a webhook URL.
- **Channel keys (D18):** config references a logical key (`summary`, `official`, `status`); the app
  reads the webhook from `DISCORD_WEBHOOK_<KEY>` (upper-cased). A new channel needs no code change.
- **Idempotency (D5–D7):** process everything since the last run, not a time window. Dedup by item
  GUID, else hash of the normalized URL. A first run, or a lost state, seeds state without posting.
- **Fail loud (D11):** critical failures exit non-zero. A single failing source does not fail the run
  but counts toward the `status` alert. A scraper that finds zero items is a failure (D12).
- **Tests:** unit tests only, driven by saved fixtures; no network access. Test names follow
  `Method_condition_result`.
- **Style:** nullable enabled, warnings are errors, file-scoped namespaces, `.editorconfig` is the
  source of truth. LF line endings (`.gitattributes`).
- **Dependencies:** prefer the BCL (`System.ServiceModel.Syndication` for feeds). Add a package only
  with a reason, and record it in `docs/decisions.md` if it is a real decision.
- **Decisions:** when a decision changes or a new one is made, update `docs/decisions.md` in the same
  commit.
- **Git:** never push without the owner's explicit approval.

## Open questions (resolve during implementation)

Whether Mistral's feed responds; real Hugging Face volume; concrete values for N (failures before
alerting, items kept in state, post cap per run); whether pushes to `state` count as activity for the
60-day rule (harmless either way, see D10).
