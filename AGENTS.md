# Agent Instructions — Corrected

## Public-Repo Hygiene — check every commit and every issue

This project is intended to be open source. Treat every commit here — and every
issue filed in any external repo (e.g. Correctless issues) — as public.

**Before every commit, review the full diff for anything that should not be
public:**

- No passwords, credentials, API keys, tokens, private keys, or secrets of any
  kind — not even expired or "test" ones.
- Nothing about the host system: no absolute local paths (`/home/...`), OS
  usernames, hostnames, machine or hardware details, local tool install
  locations, or shell/session artifacts.
- Nothing that exposes anything internal: private URLs, IPs, ports,
  infrastructure details, cloud account identifiers, non-public project names,
  or private business context.

The same rules apply to issues, PRs, and comments filed anywhere public (for
example, bug reports against the Correctless repo): review the title and body
before posting, and redact or generalize with placeholders (`<repo-root>`,
`<user>`) rather than including real values. When in doubt, leave it out — a
public leak cannot be undone even if later edited or deleted.

## Correctless

This project uses Correctless for structured development.
Read .correctless/AGENT_CONTEXT.md before starting any work.
Do NOT Read AGENT_CONTEXT.md from the project root — it may be stale or absent.
Available commands: /csetup, /cspec, /creview, /cmodel, /creview-spec, /ctdd, /cverify, /caudit, /cupdate-arch, /cdocs, /cpostmortem, /cdevadv, /credteam, /crefactor, /cpr-review, /ccontribute, /cmaintain, /cstatus, /csummary, /cmetrics, /cdebug, /chelp, /cwtf, /cquick, /crelease, /cexplain, /cauto, /carchitect, /cmodelupgrade, /cdashboard, /ctriage, /cprune
