# AI News Discord Bot — Decisions

Living document. It records what was decided, why, and what is still open. Technical operating rules
live in [`CLAUDE.md`](../CLAUDE.md); this file holds the rationale.

Last updated: 2026-11-15.

## Goal

A bot that posts relevant AI news to Discord, in English, at no cost and without failing silently. It
serves three purposes at once:

- **Staying current** on AI without hunting for news.
- **English practice**: daily reading of technical content at the source, untranslated.
- **Portfolio**: a public C#/.NET repository with visible engineering decisions (scheduling, state,
  resilience, tests, CI).

## v1 scope

**In:**

- Read RSS/Atom feeds configured in a file in the repository.
- A custom collector for Anthropic's news page (it has no RSS feed).
- Post each new item to the right channel through a Discord webhook, as an embed.
- No duplicate posts and no items lost between runs.
- Alert in a status channel when something breaks.

**Out (for now):**

- LLM summarization or translation.
- AI relevance filtering.
- Interactive Discord commands (slash commands). They would require an always-on bot, not a scheduled
  job.
- English-study extras (for example, a term of the day). Kept as a v2 idea.

## Channels and feeds

Each channel has a logical key (see D18). The feed config references the key; the webhook URL comes
from the matching secret.

| Key | Channel | Webhook name in Discord | Secret |
| --- | --- | --- | --- |
| `summary` | #ai-summary | AI News · Summary | `DISCORD_WEBHOOK_SUMMARY` |
| `official` | #ai-official | AI News · Official | `DISCORD_WEBHOOK_OFFICIAL` |
| `status` | #bot-status | AI News · Status | `DISCORD_WEBHOOK_STATUS` |

All three secrets are already configured in the repository.

| Key | Source | Feed | Notes |
| --- | --- | --- | --- |
| `summary` | AI News (Latent Space / smol.ai) | https://latent.space/feeds/news.xml | Daily digest; dense, good technical English |
| `official` | OpenAI | https://openai.com/news/rss.xml | Official feed |
| `official` | Google DeepMind | https://deepmind.google/blog/rss.xml | Official feed |
| `official` | Hugging Face | https://huggingface.co/blog/feed.xml | High volume; consider filtering |
| `official` | Mistral AI | https://mistral.ai/news/rss | Confirm that it responds |
| `official` | Anthropic | https://www.anthropic.com/news | No RSS: custom HTML collector |
| `status` | the bot itself | n/a | Failure alerts only, no news |

Meta AI has no RSS feed either and is out of scope for v1.

## Architecture

Flow of each run:

1. GitHub Actions triggers the job on a cron, every 30 minutes (or manually, via `workflow_dispatch`).
2. The .NET app loads the feed config and the previous state (items already posted), read from the
   `state` branch.
3. For each source: fetch, parse, compare against state, and keep only what is new.
4. Post new items to Discord as embeds, oldest to newest, respecting rate limits.
5. Save the updated state with a commit to the `state` branch.
6. If a source failed N times in a row, send an alert to #bot-status.

Solution structure:

- `src/AiNewsBot`: console app (job entrypoint).
- `src/AiNewsBot.Core`: models, source interfaces (`IFeedSource`), deduplication, rules.
- `src/AiNewsBot.Infrastructure`: RSS reading (`System.ServiceModel.Syndication`), Anthropic HTML
  collector, Discord webhook client, state persistence.
- `tests/`: unit tests with saved fixtures (XML and HTML), no network.

## Decisions

**D1 — Build our own bot instead of using MonitoRSS.** MonitoRSS would solve this with no code, but it
is a third-party service (if it goes down or changes its rules, we only find out when posts stop
arriving) and adds nothing to the portfolio. Building gives full control and shows engineering
decisions.

**D2 — C#/.NET.** The stack with the most depth and the easiest to defend in an interview. Version:
current .NET LTS. Rejected: Python (faster to write, less depth to show) and TypeScript.

**D3 — Run on GitHub Actions with cron.** Free on a public repository, no server to maintain, and logs
and run history come for free. It is not real time, and does not need to be. Rejected: a personal PC
(depends on the machine being on) and a VPS (cost).

**D4 — Cron every 30 minutes, at odd minutes (`17,47 * * * *`).** 30 minutes is enough for news and
comfortable for runtime and rate limits. GitHub warns that scheduled runs are delayed at peak times,
especially at the top of the hour; avoiding minute 0 reduces that. Rejected: 15 minutes (no real gain
for this content) and 1 hour (longer delay for no reason).

**D5 — Process "everything since the last run", not "the last X minutes".** If a run is late or skipped,
the next one picks up everything left behind. Nothing is lost, nothing is duplicated.

**D6 — Deduplicate by stable identifier.** Key = the item's GUID or, when missing, a hash of the
normalized URL. Keep only the last N per source so state does not grow forever.

**D7 — The first run only records, it does not post.** Without this, the first run dumps dozens of old
posts. The same rule applies if the state goes missing: re-seed instead of reposting everything.

**D8 — Per-source post limit per run.** Protection against floods (for example, a feed that republishes
everything after a migration). The excess is dropped with a log entry, not posted.

**D9 — Webhooks in GitHub Secrets.** The repository is public; webhook URLs never go into code,
versioned config or logs.

**D10 — Keepalive against inactivity shutdown.** On public repositories, scheduled workflows are
disabled automatically after 60 days without activity, with no warning (verified in GitHub's
documentation). A keepalive workflow (via the API, no fake commits) keeps the schedule alive. It stays
even if commits to the `state` branch count as activity: it is cheap redundancy.

**D11 — Fail loud.** The process exits with an error code when something critical fails, so GitHub sends
a failed-workflow email. Partial failures (one source down) do not fail the run, but count toward the
#bot-status alert.

**D12 — Anthropic collector isolated and tested with a fixture.** HTML scraping breaks when the site
changes. It sits behind the same interface as the other sources, with a test over a saved HTML file,
and "zero items found" counts as a failure (so it cannot break silently).

**D13 — Discord rate limits.** Respect `429` and the webhook's `Retry-After`, with limited retries.
Posts in chronological order.

**D14 — English content, no translation.** The best feeds are already in English and the bot doubles as
reading practice. This removes the LLM step from v1.

**D15 — State on a dedicated branch (`state`).** A JSON file committed by the job to a separate branch.
Durable, auditable and easy to inspect; the main branch history stays clean. Rejected: GitHub Actions
cache (GitHub can evict it; even though D7 prevents reposting, items published in the gap would be
lost).

**D16 — Posts as embeds.** A card with title, source, date and link. More readable in the channel and
better looking in a portfolio. Rejected: a plain message with title + link (relies on Discord's
automatic link preview, which varies by site).

**D17 — Repository `ai-news-bot` on the personal GitHub account (`guinog29`).** Short, descriptive name,
on the personal profile so it counts as portfolio. MIT license. Rejected: `discord-ai-news`.

**D18 — Naming convention by channel key.** Each channel has a logical key (`summary`, `official`,
`status`). The feed config references only the key; the app reads the URL from
`DISCORD_WEBHOOK_<KEY>`. Discord webhooks are named `AI News · <Function>`. Channels are in English,
consistent with the rest of the repository. Reason: versioned config without secrets, new channels
without code changes, clear authorship of posts. Rejected: hardcoded URLs or secret names.

## Open questions

These depend on testing during implementation:

- Whether the Mistral feed actually responds.
- Real Hugging Face volume; if too high, filter it or move it to its own channel.
- Concrete values for N (consecutive failures before alerting, items kept in state, post limit per run).
- Whether a push to the `state` branch counts as activity for the 60-day rule (changes nothing, thanks
  to D10, but worth recording).

## Next steps

1. ~~Create the Discord server with the three channels and a webhook for each.~~ Done.
2. ~~Create the repository and configure the secrets.~~ Done.
3. ~~Scaffold the solution, `CLAUDE.md`, README and this document.~~ Done.
4. Implement v1 following this document.
5. Run for one or two weeks and evaluate volume, English difficulty and failures.

## References

- GitHub Docs: workflows disabled for inactivity — https://docs.github.com/en/actions/how-tos/manage-workflow-runs/disable-and-enable-workflows
- keepalive-workflow — https://github.com/marketplace/actions/keepalive-workflow
- Latent Space feeds — https://latent.space/feeds/
- Feeder: AI RSS feeds — https://feeder.co/knowledge-base/rss-content/ai-news-rss-feeds/
