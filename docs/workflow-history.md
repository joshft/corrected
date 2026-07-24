# Workflow History

### 2026-07-22 — .NET 10 / Dafny 4.11.0 Package-Compatibility Spike (Phase 0.0, bullets 1–3)
Branch: feature/dafny-compat-spike. Rules: 19. QA rounds: 3. Findings fixed: 76. Permanent, non-production conformance harness proving Dafny 4.11.0's `net8.0` packages run in-process on a .NET 10 host across two integration routes (both COMPATIBLE, suite-attested 274/274); produced provisional ADR-0001 (Route A selected, promotion pending) and the committed evidence sample pair. High intensity: 3 QA rounds + probe round (mutation + config-fuzz) + 2 mini-audit rounds; 11 findings deferred to backlog (DF-002..DF-012), 1 upstream.
