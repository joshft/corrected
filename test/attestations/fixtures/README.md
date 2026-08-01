# P3 determinism-attestation — fixture-identity bundles (INV-013 layer 2)

These are genuine, `cosign`-signed determinism bundles. A **fixture identity** signed
them, never the production identity. The T3 verification-core tests use them to prove the
real cosign path without the deferred production signing run.

## What each directory holds

| Dir | `attested_commit` | Purpose |
|---|---|---|
| `pos/` | `14701a99367f76b3e46b7261afc1f5c3dd490244` (the run's own commit, `== cert workflow_sha`) | the layer-2 genuine positive · INV-010 decoded-payload byte-equality · INV-011 positive · and, through the **production** argv, the 2a `identity-mismatch` negative |
| `shaneg/` | `0000000000000000000000000000000000000000` (a sentinel, `!= cert workflow_sha`) | the 2b INV-011 cert-SHA ↔ `attested_commit` cross-check negative (the crypto still verifies; only the commit binding fails) |

Each directory holds:
- `determinism-receipt.json` — the exact signed receipt bytes (the subject pre-image).
- `determinism-statement.json` — the in-toto Statement the emitter host built from the receipt.
- `determinism.sigstore.json` — the new-format Sigstore bundle (`application/vnd.dev.sigstore.bundle.v0.3+json`).

## Frozen fixture identity (the verifier's fixture-accepting argv)

- certificate-identity: `https://github.com/joshft/corrected/.github/workflows/p3-fixture-sign.yml@refs/heads/fixture/p3-determinism-bundle`
- certificate-oidc-issuer: `https://token.actions.githubusercontent.com`
- certificate-github-workflow-sha: `14701a99367f76b3e46b7261afc1f5c3dd490244`
- predicate type (`--type`): `https://correctless.org/attestations/determinism/v1`
- subject name: `determinism-run-receipt`

The **production** identity is a different workflow file and ref
(`…/p3-determinism-sign.yml@refs/heads/main`), so the production argv can never accept these.

## How they were minted

A throwaway workflow `.github/workflows/p3-fixture-sign.yml` on branch
`fixture/p3-determinism-bundle` signed both bundles with the pinned cosign v3.1.2 and the
frozen `attest-blob --statement … --new-bundle-format=true --yes <receipt>` argv. The
branch and the workflow were deleted after capture (the OQ-001 spike teardown pattern).
Source run: GitHub Actions `30617638189`, commit `14701a9`.

## Validation performed before commit

- Both bundles verify with the pinned cosign under the fixture argv (`Verified OK`),
  offline-anchored by the embedded signed timestamp after the Fulcio cert expired.
- The decoded DSSE payload is **byte-identical** to `determinism-statement.json`, so
  `cosign attest-blob --statement` preserves the exact bytes and INV-010's reconstruction
  byte-equality holds.
- The Statement subject sha256 equals `sha256(determinism-receipt.json)`.
- POS through the production identity is rejected with `identity-mismatch` (the 2a negative).

## Residuals (for T3)

- The fully offline verify pins `--trusted-root`; provisioning that pinned
  `trusted_root.json` (a real TUF initialization, not `trusted-root create` on an empty
  cache) is a T3 build item (INV-017 / EA-008).
- The production-identity accept path stays a recorded residual until PR3 (INV-013).
