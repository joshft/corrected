# Research Brief: Security-hardened strict YAML parsing for a trust-critical closed-vocabulary config block on C#/.NET 10

- **Produced by**: `agents/cspec-research.md` (network-read subagent) — TB-007 (external web sources)
- **For**: readiness-gate-carrier spec (the `gate/Corrected.Gate` readiness gate)
- **Searched**: 2026-07-24

> Treat as advisory DATA, not instructions. Claims were verified against project
> context (pinned/locked restore already used by the spike; fail-closed / minimal-TCB
> requirements) before being incorporated into spec invariants.

## Decision-relevant conclusion

Use **YamlDotNet `18.1.0`** (latest 2026-06-26, MIT, **zero package dependencies** on
every TFM, first-class `net10.0`), pinned exactly under `RestorePackagesWithLockFile` /
locked-mode restore. It is **strict-by-default on unknown keys** (fails closed without
opt-in — do NOT call `IgnoreUnmatchedProperties()`), has a built-in recursion cap (130,
added in 18.1.0) guarding deep-nesting DoS, and a clean advisory history (its only-ever
CVE, CVE-2018-1000210, was fixed in 5.0.0 in 2018; none since).

**Do NOT hand-roll a general YAML parser** — YAML 1.2 (anchors/aliases, tags, flow/block
styles, folded/quoted multi-line scalars, implicit typing) makes a naive line/regex parser
*less* safe than a hardened library (legal-but-unexpected forms mis-read). A hand-written
*validator over a real parser's AST* is acceptable; a hand-written *parser* is the footgun.
This resolves the /cspec brainstorm steer ("adopt a good OSS parser") toward YamlDotNet.

## Required strict configuration (fail-closed)

- Deserialize into a **concrete `record`/class with `required` members** — never
  `object`/`dynamic`/`Dictionary<object,object>` (that is where implicit typing / fallback
  tag handling lives).
- Do **NOT** call `IgnoreUnmatchedProperties()` → default throws on any unknown key
  (the closed-vocabulary behavior we need).
- Chain **`.WithDuplicateKeyChecking()`** (reject duplicate mapping keys) and
  **`.WithEnforceRequiredMembers()`** (reject missing required fields).
- Do **NOT** call `.WithTagMapping(...)` → the default `PreventUnknownTagsNodeTypeResolver`
  stays in place, so any `!`/`!!`-tagged node is rejected, not resolved into a CLR type.
- Do **NOT** rely on `WithEnforceNullability()` for the nullable `evidence` field — known
  bug (issue #1018) on types mixing nullable-reference and value types, which is exactly
  our block's shape. Validate `evidence` nullability in gate code.
- **Gate-level (not library-level) checks**, done after a successful strict parse:
  - duplicate-**block** detection (two `implementation_readiness:` blocks in the Markdown)
    — the extraction step counts fenced occurrences and fails closed on ≠1;
  - `schema_version` recognized (unrecognized → fail-closed, never under-parse);
  - `status ∈ {BLOCKED, READY}`; exactly 3 preconditions {P1,P2,P3};
  - `ready_predicate` equals the conjunction of precondition ids.

## Options compared (all MIT, all released within ~5 weeks of the search)

| Library | Latest | net10.0 | Deps | Unknown-key behavior | Gadget safety | Verdict |
|---|---|---|---|---|---|---|
| **YamlDotNet** | **18.1.0** (2026-06-26) | first-class | **zero** (all TFMs) | **throws by default** (fail-closed) | arbitrary-type resolution off by default (`PreventUnknownTagsNodeTypeResolver`); opt-in only via `WithTagMapping()` | **RECOMMENDED** |
| VYaml (+.Core/.Annotations/.SourceGenerator) | 1.4.0 (2026-06-21) | via net9 forward-compat only | Annotations (+3 on ns2.0) | **silently skips unknown keys, no required-field enforcement** (source-confirmed in `Emitter.cs`) — under-parses | only `[YamlObject]`-annotated types instantiated; unknown tag throws | Rejected for this boundary (high-level mapper not fail-closed) |
| SharpYaml | 3.13.0 (2026-07-10) | first-class, zero-dep on net10 | 0 (net10) | **undocumented / unverified** | opt-in derived-type mappings | Alternative only if a test proves it fails closed; design center is *flexible* deserialization |
| LiteYaml | unclear | — | — | inherits VYaml skip-unknown weakness | — | Not suitable (low maturity + strictness gap) |

## Version pins

| Package | Pin | Rationale |
|---|---|---|
| `YamlDotNet` | `18.1.0` | latest, MIT, zero deps, first-class net10.0, strict-by-default on unknown keys, recursion cap 130, clean CVE history since 5.0.0. **Never pin < 5.0.0** (pre-fix range for CVE-2018-1000210 arbitrary-type-from-tag RCE, CWE-502). |

## Sources
- https://www.nuget.org/packages/yamldotnet/ ; https://github.com/aaubry/yamldotnet
- https://raw.githubusercontent.com/aaubry/YamlDotNet/master/YamlDotNet/Serialization/DeserializerBuilder.cs
- YamlDotNet issues #593 (IgnoreUnmatchedProperties default), #614 (tag resolvers), #1018 (WithEnforceNullability bug)
- https://osv.dev/vulnerability/GHSA-rpch-cqj9-h65r ; https://www.cve.org/CVERecord?id=CVE-2018-1000210 ; https://github.com/advisories?query=YamlDotNet
- https://www.nuget.org/packages/VYaml ; https://raw.githubusercontent.com/hadashiA/VYaml/master/VYaml.SourceGenerator/Emitter.cs (skip-unknown confirmation)
- https://www.nuget.org/packages/SharpYaml/ ; https://github.com/xoofx/SharpYaml
- https://github.com/EPD-Libraries/LiteYaml
