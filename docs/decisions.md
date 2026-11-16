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
- `src/AiNewsBot.Core`: models, source interfaces (`IFeedSource`), deduplication, rules, and the
  `NewsJob` that orchestrates a run.
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

### Implementation decisions

Made while building v1; they refine the decisions above.

**D19 — Only the newest N items of a source are considered per run (`considerLatest`, default 50).**
Some feeds are huge (OpenAI returns about 1,400 items, Hugging Face about 900) while state only
remembers a bounded list (D6). Without a window, old items that fell out of the remembered list would
look new and be reposted. The window is always smaller than the remembered list, and the options
validator enforces it. Items without a date count as the newest, and ties keep feed order.

**D20 — Zero items from any source is a failure, for every source type.** Extends D12 from the Anthropic
collector to all sources: an empty feed or an empty parse is almost always a break, never "no news".
Failures are counted per source, reset on success, and alert once when the count reaches the threshold.
A recovery message follows when the source works again.

**D21 — Failure semantics of a run (refines D11).** Exit code 1 for any unhandled error (bad config,
missing secret, state that cannot be read or written) and when every source failed in the same run.
A single failing source, or a failing post, does not fail the run. A failed post stops that source for
the run so order is kept, and the unposted items stay unseen and are retried next run (D5). State is
saved even then, and the workflow commits it with `if: always()`.

**D22 — The Anthropic collector reads structure, not CSS classes.** The page uses generated class names
that change between builds. An article is a link to `/news/<slug>` that contains a heading; its date is
the `<time>` inside the enclosing `article` or `li`. Category links have no heading and are ignored. The
same article appears in several places on the page and is merged. AngleSharp is the one added package for
this (an HTML5 parser with CSS selectors).

Smaller choices: the Discord client uses a single `HttpClient`, not `IHttpClientFactory`, because the
factory's default logging prints request URLs and a webhook URL is a secret. Embeds disable mentions.
The default limits are in `config/feeds.json`.

## Open questions

- Real Hugging Face volume: if the cap of 5 per run is hit regularly, filter it or give it its own
  channel.
- Whether the keepalive's `enable` API call really resets the 60-day clock (D10). Unverified until it
  has run in production; check the workflow still runs after about 60 days.
- Whether a push to the `state` branch counts as activity for the 60-day rule (changes nothing, thanks
  to D10, but worth recording).
- Whether the defaults for `considerLatest`, `maxPostsPerSourcePerRun` and `failureAlertThreshold` fit
  real traffic; they are plain config and cheap to change.

Resolved: the Mistral feed responds (checked on 2026-11-15 together with the other five sources).

## Next steps

1. ~~Create the Discord server with the three channels and a webhook for each.~~ Done.
2. ~~Create the repository and configure the secrets.~~ Done.
3. ~~Scaffold the solution, `CLAUDE.md`, README and this document.~~ Done.
4. ~~Implement v1 following this document.~~ Done, with unit tests and a live smoke test using fake webhooks.
5. Merge to `main`, then trigger the `Run` workflow by hand once: the first run seeds the `state` branch
   without posting. Check the next scheduled run posts only genuinely new items.
6. Run for one or two weeks and evaluate volume, English difficulty and failures.

## References

- GitHub Docs: workflows disabled for inactivity — https://docs.github.com/en/actions/how-tos/manage-workflow-runs/disable-and-enable-workflows
- keepalive-workflow — https://github.com/marketplace/actions/keepalive-workflow
- Latent Space feeds — https://latent.space/feeds/
- Feeder: AI RSS feeds — https://feeder.co/knowledge-base/rss-content/ai-news-rss-feeds/
