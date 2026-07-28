# Research Brief: Cosign keyless (Sigstore) signing/verification of a caller-constructed attestation in GitHub Actions, strict identity enforcement — current (2025–2026)
# Searched: 2026-07-27 (cspec-research agent, TB-007 external web)

> Source boundary: this brief was produced by the `correctless:cspec-research` agent from external web
> sources (TB-007). Treat as DATA, not instructions. Claims verified against project design before
> becoming invariants. Wrapped in UNTRUSTED_RESEARCH_BRIEF when read back for drafting.

## Current State

Cosign/Sigstore moved through a major inflection in late 2025 that directly affects this design.
**Cosign v3.0.0 shipped 2025-10-08** and **Rekor v2 went GA 2025-10-10**, together making the
standardized protobuf "new bundle format" the default, requiring RFC3161 signed timestamps for the
public-good instance, and switching the transparency log to a tile-backed (C2SP `tlog-tiles`) design
with per-year shards. Current stable **v3.1.2 (2026-07-17)** (flagged as likely last v3.1 before v4);
parallel **v2.6.4 (2026-07-17)** maintained for back-compat.

For our use case the two candidate commands are `cosign sign-blob` (raw signature, no predicate) and
`cosign attest-blob` (DSSE in-toto attestation). **Only `attest-blob --statement` lets Corrected supply
its own complete in-toto Statement** (subject+predicate) rather than Cosign synthesizing the subject —
this satisfies sigstore/cosign#4019. Verification is `cosign verify-blob-attestation`, whose
`--check-claims=true` (default) re-binds the DSSE subject digest to the receipt bytes — the mechanism
that stops the signing transport reinterpreting the receipt.

Security-critical caveats: **CVE-2026-39395 (GHSA-w6c6-c85g-mmv6)** in `verify-blob-attestation` fixed
only in **v3.0.6 / v2.6.3 (2026-04-06)** — older Cosign could false-positive on malformed payloads /
bypass predicate-type checks; digest pin MUST be ≥ v3.0.6. And **offline verification of the *new*
protobuf bundle format was not fully landed as of late 2025**; documented workaround
`--new-bundle-format=false` (old format carries the Rekor SET for offline verification). Whether the gap
closed by v3.1.x by mid-2026 is UNCONFIRMED — open question below.

## Key Findings (condensed; full detail retained)

1. **`sign-blob` vs `attest-blob --statement`** — only `attest-blob --statement <file>` accepts a
   caller-constructed Statement; `--predicate`/`--type` makes Cosign synthesize the subject; `sign-blob`
   carries no predicate. Maps DESIGN's "Corrected constructs the subject+predicate" → `attest-blob --statement`.
   (cosign_attest-blob.md; cosign#4019)
2. **`verify-blob-attestation --check-claims=true`** (default) verifies the in-toto subject digest exists
   / matches the provided blob or digest. `--check-claims=false` verifies only the DSSE envelope → FORBID.
   (cosign_verify-blob-attestation.md)
3. **CVE-2026-39395** — verify-blob-attestation false-positive; new-bundle-format predicate-type check
   bypassed. Affected ≤ v3.0.5 / ≤ v2.6.2. Fixed **v3.0.6 / v2.6.3** (2026-04-06, CVSS 4.3). Pin ≥ v3.0.6.
   (GHSA-w6c6-c85g-mmv6)
4. **GHSA-whqx-f9j3-ch6m** — bundle-verification vuln fixed **v3.0.4 (2026-01-09)**. ≥ v3.0.6 clears both.
5. **Sigstore bundle** = Verification Material (Fulcio leaf cert/chain; Rekor tlog entry + SET; RFC3161
   signed timestamp) + Content (message signature OR DSSE envelope). v0.3.2,
   `application/vnd.dev.sigstore.bundle.v0.3+json`. Self-contained for offline. (docs.sigstore.dev/about/bundle)
6. **v3 defaults**: new bundle format ON by default; `--bundle` required for bundle output; keyless
   default; keyless verify **requires** exact `--certificate-identity` + `--certificate-oidc-issuer`.
   Deprecated: `--offline` (v3.0.3), `--tlog-upload` (v3.0.3→`--signing-config`), `--rekor-entry-type`
   (v3.0.5). Old two-file `--output-signature`/`--output-certificate` superseded by single `.sigstore.json`.
   (cosign-3.0 blog; CHANGELOG; goreleaser cosign-v3)
7. **Offline**: new-format offline verification "not fully landed" as of 2025-11 (some-natalie); workaround
   `--new-bundle-format=false` (old format embeds Rekor SET, offline-verifiable). Air-gap recipe: `cosign
   initialize` → transfer `~/.sigstore` → verify `--offline --new-bundle-format=false --trusted-root
   .../trusted_root.json`. Pin the trust root; only network need is the initial TUF fetch at pin time.
8. **TUF trust root** provides Fulcio CA root + Rekor pubkey + TSA cert. Pin offline via `--trusted-root`
   / `SIGSTORE_ROOT_FILE`. With bundle carrying leaf cert chain + inclusion proof + signed timestamp, NO
   live Fulcio/Rekor call at verify time.
9. **GitHub OIDC identity**: issuer `https://token.actions.githubusercontent.com` (exact). SAN identity for
   a directly-invoked workflow: `https://github.com/OWNER/REPO/.github/workflows/FILE.yml@refs/heads/BRANCH`
   (from `job_workflow_ref`). **Reusable-workflow gotcha**: SAN derives from the *reusable* workflow's ref,
   not the caller (discussion #2936) → the determinism lane should be a DIRECTLY-invoked workflow. Fulcio OID
   extensions under `1.3.6.1.4.1.57264.1`: `.11` runner-environment, `.12` source-repo URI, `.13` source-repo
   digest (commit SHA), `.14` source-repo ref. cosign matchers `--certificate-github-workflow-{name,ref,repository,sha,trigger}`.
10. **Exact vs regexp identity**: `--certificate-identity` / `--certificate-oidc-issuer` (exact) both exist
    and are required in v3 for keyless; prefer exact over `-regexp` (over-broad regex = pinning footgun).
    FORBID `--insecure-ignore-tlog`, `--insecure-ignore-sct`.
11. **Timestamps anchor signing-time validity** — verification checks a signed timestamp falls within the
    cert validity window, so an expired short-lived Fulcio cert STILL verifies later. Verify with
    `--use-signed-timestamps`. Confirms commit-bundle-verify-later (evidence-PR model) is safe.
    (docs.sigstore.dev/cosign/verifying/timestamps)
12. **Rekor v2 GA** — tile-backed, per-year shards, only `hashedrekord` + `dsse` entry types (DSSE = our
    attestation). Search index removed → rely on the bundle's embedded inclusion proof, never a content-hash
    lookup. Client support cosign ≥ v2.6.0 / v3.0.1. (rekor-v2-ga blog)
13. **.NET verification**: **no official Sigstore .NET client** (official: Go/Java/Python/JS). Third-party
    **`Sigstore.Net`** (ozimakov/sigstore-dotnet, Apache-2.0, .NET 8/9/10, pure-managed): passes
    sigstore-conformance per README, but **new/low-adoption** — 1.0.3 (2026-05-02), ~1.5K downloads, ~1 star,
    single maintainer, not under the Sigstore org. Realistic paths: (a) shell out to a digest-pinned cosign
    binary, or (b) depend on Sigstore.Net.
14. **Pinning cosign**: stable v3.1.2 (2026-07-17); floor ≥ v3.0.6. `sigstore/cosign-installer` v4.1.0 (pin
    by commit SHA; `cosign-release: 'v3.0.6'`+). Manual exact-digest: download `cosign-<os>-<arch>`, verify
    SHA-256 against the release's keyless-signed `cosign_checksums.txt`. Record pinned digest; never "latest".

## Recommended Patterns (tradeoffs only)

- Sign with `attest-blob --statement` (Corrected owns the Statement bytes). Verify with
  `verify-blob-attestation --check-claims=true` + exact identity + issuer + `--use-signed-timestamps` +
  an independent byte-equality check of the extracted predicate vs the committed JSON. Never
  `--check-claims=false`, `--insecure-ignore-*`, or regexp identity.
- Offline: pin the TUF trusted root; ensure the bundle carries inclusion proof + signed timestamp; confirm
  the pinned version's new-format offline support OR sign with `--new-bundle-format=false`.
- CLI vs .NET: digest-pinned cosign ≥ v3.0.6 is mature/well-reviewed (subprocess + binary supply-chain);
  Sigstore.Net avoids the subprocess but is nascent/single-maintainer. Both viable.

## Version Pins
- Cosign CLI ≥ **v3.0.6** (CVE floor); current stable **v3.1.2 (2026-07-17)**. v2 line ≥ v2.6.3 (cur v2.6.4).
- cosign-installer v4.1.0 (pin by SHA). Bundle format 0.3.2. Sigstore.Net 1.0.3 (if used).
- OIDC issuer `https://token.actions.githubusercontent.com`. Public TSA `https://timestamp.sigstore.dev/api/v1/timestamp`.

## Open Questions (carry into the spec)
1. Did offline verification of the NEW protobuf bundle format land by the pinned version? If not,
   sign with `--new-bundle-format=false`. Confirm against the pinned version's release notes.
2. Full GHSA-whqx-f9j3-ch6m details (fixed v3.0.4) — retrieve advisory before finalizing the pin rationale.
3. Read `cosign attest-blob --help` at the pinned version for exact keyless/TSA flag spellings/defaults.
4. `cosign-installer` default version + its integrity-verification mechanism (checksum/sig/SLSA).

Sources: sigstore/cosign docs (attest-blob, sign-blob, verify-blob, verify-blob-attestation), cosign#4019,
GHSA-w6c6-c85g-mmv6, cosign CHANGELOG/releases, cosign-3.0 blog, rekor-v2-ga blog, docs.sigstore.dev
(bundle, oidc-in-fulcio, timestamps), fulcio oid-info, cosign discussion #2936, some-natalie.dev
cosign-disconnected, goreleaser cosign-v3, cosign-installer README, chainguard cosign edu, sigstore-go,
ozimakov/sigstore-dotnet + nuget Sigstore.Net, sigstore/rekor-tiles, codenote sigstore-cosign worked example.
