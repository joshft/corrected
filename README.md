# Corrected

**Corrected** is an open-source, proof-directed verification worker and certification
toolchain for [Dafny](https://dafny.org). Given a versioned Dafny program, frozen formal
obligations, and an explicit edit policy, it searches for an allowed implementation or
proof patch, rejects unapproved proof shortcuts, and emits reproducible evidence — an
**assurance receipt** of what was proved, built, tested, assumed, and still trusted.

> **Status: design-stage.** No production code exists yet (`src/` is empty). The
> authoritative design is [`DESIGN.md`](DESIGN.md) (v1.13) — read it before speccing any
> feature. The first build to land is the Phase 0.0 compatibility spike (below).

## What exists today

| Area | State |
|------|-------|
| [`DESIGN.md`](DESIGN.md) | Authoritative design (v1.13): architecture, delivery model, phased plan. |
| [`spikes/dafny-compat/`](spikes/dafny-compat/) | **Phase 0.0 package-compatibility spike** — permanent, non-production conformance harness proving Dafny 4.11.0 runs in-process on .NET 10. Both integration routes **COMPATIBLE**. See its [README](spikes/dafny-compat/README.md) and [feature doc](docs/features/dafny-compat-spike.md). |
| [`docs/adr/ADR-0001`](docs/adr/ADR-0001-dafny-integration-boundary.md) | The Dafny integration-boundary decision (provisional; Route A selected, promotion pending). |
| `src/` | Empty — the production worker begins in Phase 0.1, informed by ADR-0001. |

## Intended architecture (design-stage)

- **C# core worker** on .NET 10 LTS — the deterministic policy/acceptance core: intake and
  lock resolution, ownership classification, verification, acceptance evaluation, and
  receipt emission.
- **`corrected` CLI** — reference acceptance implementation (`corrected init` / `check` /
  `certify`), runnable with no model, Node.js, or agent session.
- **TypeScript Pi adapter** — a thin integration package realizing the core-defined
  methodology inside the Pi agent runtime. Integration code only — *not* a second policy
  implementation.
- **Worker ↔ adapter protocol** — strict LF-delimited JSON over stdin/stdout; versioned
  commands/events/results; large artifacts by content-addressed descriptor.

See [`.correctless/ARCHITECTURE.md`](.correctless/ARCHITECTURE.md) for the frozen design
patterns, prohibitions, and trust boundaries that specs and reviews enforce from the first
feature.

## Running the compatibility spike

```bash
env -i HOME="$HOME" bash -p spikes/dafny-compat/scripts/run-spike.sh
```

~13–15 min: provisions Z3, does a locked restore + build under the pinned .NET SDK, runs
the test suite, and prints a per-route COMPATIBLE / INCOMPLETE verdict. The hardened
`env -i … bash -p` invocation is mandatory (the controller refuses an unhardened call). See
the [feature doc](docs/features/dafny-compat-spike.md) for the full operator surface.

## License

Not yet added. The project is described as open-source; a `LICENSE` file will accompany the
first production release.
