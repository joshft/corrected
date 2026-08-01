using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corrected.Provenance.Determinism;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation INV-007 (~316-321) — the RUNNABLE test-host emitter that writes
/// handoff/determinism-statement.json. Today INV-007's "emit the receipt + Corrected-built
/// Statement as workflow artifacts" is a no-op echo in the producer job; the chosen host is a
/// [Fact] the workflow triggers via <c>dotnet test --filter EmitDeterminismStatement</c>, driven
/// by env vars. The emission LOGIC is
/// <see cref="DeterminismStatementEmitter"/> in Corrected.Provenance (BCL-only); the single
/// canonical <see cref="DeterminismAttestation.SerializeStatementJson"/> is the ONLY byte-source,
/// so the emitted Statement is byte-identical to the (future T3, INV-010) verifier reconstruction.
///
/// PURE in-process file I/O over the REAL Corrected.Provenance substrate (no subprocess, no mocks)
/// driven by the committed REAL PR1 determinism RunReceipt fixture (AP-031). This class must NOT
/// carry [Collection("Subprocess")].
///
/// FIXED, LOAD-BEARING CONTRACT (GREEN: the workflow, the fact name, and the env keys MUST agree):
///   * Fact/filter target : EmitDeterminismStatement
///   * env input receipt  : EMIT_STATEMENT_RECEIPT
///   * env output stmt    : EMIT_STATEMENT_OUT
///   * committed fixture  : gate/Corrected.Gate.Tests/fixtures/provenance/determinism-receipt.sample.json
///   * predicateType URI  : https://correctless.org/attestations/determinism/v1
///   * subject name       : determinism-run-receipt
///
/// RED: DeterminismStatementEmitter.Emit / EmitFromEnvironment are STUB:TDD and throw
/// NotImplementedException, so the happy-path cells (A/B/C/D) fail as thrown exceptions and the
/// fail-closed cells (E/F) fail because a NotImplementedException is NOT the real missing/parse
/// fail-closed throw GREEN owes. (The reflection cell G is a structural filter-target GUARD; it is
/// green once this file authors the [Fact], by design — it exists to bind the workflow --filter
/// substring to a real invocable target and catch a future rename, not to drive RED.)
///
/// AP-031: the subject bytes are the committed verbatim PR1 producer receipt fixture — the SAME
/// real artifact DeterminismStatementCanonicalTests / P3SignerHarness pin.
/// Source: gate/Corrected.Gate.Tests/fixtures/provenance/determinism-receipt.sample.json
/// </summary>
public class DeterminismStatementEmitterHostTests
{
    // ---- Pinned contract literals — INDEPENDENT of the production consts (A4). Pinning them here
    // means a GREEN that silently re-freezes either value fails this suite, not merely echoes itself.
    private const string PredicateTypeUri = "https://correctless.org/attestations/determinism/v1";
    private const string SubjectName = "determinism-run-receipt";
    private const string EnvKeyReceipt = "EMIT_STATEMENT_RECEIPT";
    private const string EnvKeyOut = "EMIT_STATEMENT_OUT";

    // A3: the ONE shared source of the workflow --filter target name. Both C# test classes reference
    // THIS const, and the wiring test binds the committed workflow literal to it — so a C#-side
    // rename is a single-place change, not three drifting copies (host test / wiring test / workflow).
    public const string FilterTargetName = "EmitDeterminismStatement";

    private static string FixtureReceiptPath()
        => TestPaths.Fixture("provenance", "determinism-receipt.sample.json");

    private static byte[] FixtureBytes() => File.ReadAllBytes(FixtureReceiptPath());

    /// <summary>The canonical bytes the emitted file MUST equal, computed independently per call.</summary>
    private static byte[] ExpectedStatementBytes(byte[] receiptBytes)
        => Encoding.UTF8.GetBytes(
            DeterminismAttestation.SerializeStatementJson(receiptBytes, RunReceipt.FromJson(receiptBytes)));

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "p3-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string NewTempOut() => Path.Combine(NewTempDir(), "determinism-statement.json");

    private static void CleanupParent(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort — OS temp cleanup is the backstop
        }
    }

    // =====================================================================
    // The LOAD-BEARING workflow --filter target. The producer job triggers this exact fact via
    // `dotnet test --filter 'FullyQualifiedName~EmitDeterminismStatement'`. Mode-agnostic so it
    // passes both in the normal gate (env unset -> suite mode) and in the CI producer (env set ->
    // writes the injected handoff/determinism-statement.json). RED: EmitFromEnvironment throws.
    // DO NOT RENAME without updating the workflow --filter AND env-key contract in lockstep.
    // =====================================================================

    // Tests INV-007 [integration]: the runnable host emits the Corrected-built Statement through
    // the ONE canonical serializer, driven by the real process environment.
    [Fact]
    public void EmitDeterminismStatement()
    {
        string? receiptEnv = Environment.GetEnvironmentVariable(EnvKeyReceipt);
        string? outEnv = Environment.GetEnvironmentVariable(EnvKeyOut);
        bool ciMode = !string.IsNullOrEmpty(receiptEnv) && !string.IsNullOrEmpty(outEnv);

        string suiteTempOut = NewTempOut();
        string expectedReceiptPath = ciMode ? receiptEnv! : FixtureReceiptPath();
        string expectedOutPath = ciMode ? outEnv! : suiteTempOut;

        try
        {
            DeterminismStatementEmitter.EmitFromEnvironment(
                Environment.GetEnvironmentVariable, FixtureReceiptPath(), suiteTempOut);

            Assert.True(
                File.Exists(expectedOutPath),
                $"INV-007: the host must write the determinism Statement to '{expectedOutPath}' ({(ciMode ? "CI" : "suite")} mode).");

            // The emitted file goes through the ONE canonical byte-source over the receipt actually read.
            byte[] receiptBytes = File.ReadAllBytes(expectedReceiptPath);
            Assert.Equal(ExpectedStatementBytes(receiptBytes), File.ReadAllBytes(expectedOutPath));

            using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(expectedOutPath)); // parses
        }
        finally
        {
            // Never delete a CI-mode injected out (the workflow hand-off needs it); only clean the suite temp.
            if (!ciMode)
            {
                CleanupParent(suiteTempOut);
            }
        }
    }

    // =====================================================================
    // A. Byte-equality anchor (pure unit) — Emit(fixture, temp) goes through the ONE serializer.
    // =====================================================================

    // Tests INV-006/INV-010 [unit]: Emit writes a file whose EXACT bytes equal
    // SerializeStatementJson(fixtureBytes, RunReceipt.FromJson(fixtureBytes)) — UTF-8, NO BOM, no
    // extra trailing newline. This is the core guarantee: the emitted file is the single canonical
    // byte-source, so it is byte-identical to the (future T3) verifier reconstruction. RED: Emit
    // throws NotImplementedException -> unhandled -> fails.
    [Fact]
    public void Emit_writes_exact_canonical_serializer_bytes_no_bom_no_trailing_newline()
    {
        byte[] fixtureBytes = FixtureBytes();
        byte[] expected = ExpectedStatementBytes(fixtureBytes);
        string temp = NewTempOut();
        try
        {
            DeterminismStatementEmitter.Emit(FixtureReceiptPath(), temp);

            byte[] got = File.ReadAllBytes(temp);
            Assert.Equal(expected, got); // exact byte-for-byte equality with the canonical serializer
            Assert.Equal(expected.Length, got.Length); // no extra trailing newline / trailing bytes

            // No UTF-8 BOM prefix (SerializeStatementJson emits none).
            bool hasBom = got.Length >= 3 && got[0] == 0xEF && got[1] == 0xBB && got[2] == 0xBF;
            Assert.False(hasBom, "INV-010: the emitted Statement must carry NO UTF-8 BOM.");
        }
        finally
        {
            CleanupParent(temp);
        }
    }

    // =====================================================================
    // B. Subject / predicate binding (pure unit, INDEPENDENT literal expectations).
    // =====================================================================

    // Tests INV-006 (b) [unit]: parse the emitted file; the single subject digest sha256 equals an
    // independently-computed BCL SHA-256 of the fixture receipt bytes; predicateType == the frozen
    // literal; subject name == the frozen literal. Expectations are LITERAL strings, NOT the code's
    // consts, so a GREEN that re-freezes either value fails here. RED: Emit throws -> fails.
    [Fact]
    public void Emit_binds_subject_digest_predicate_type_and_subject_name_to_frozen_literals()
    {
        byte[] fixtureBytes = FixtureBytes();
        string temp = NewTempOut();
        try
        {
            DeterminismStatementEmitter.Emit(FixtureReceiptPath(), temp);

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(temp));
            JsonElement root = doc.RootElement;

            JsonElement subjects = root.GetProperty("subject");
            Assert.Equal(1, subjects.GetArrayLength()); // exactly ONE subject (INV-006)

            string subjectSha = subjects[0].GetProperty("digest").GetProperty("sha256").GetString()!;
            string independentSha = Convert.ToHexString(SHA256.HashData(fixtureBytes)).ToLowerInvariant();
            Assert.Matches("^[0-9a-f]{64}$", subjectSha);
            Assert.Equal(independentSha, subjectSha);

            Assert.Equal(PredicateTypeUri, root.GetProperty("predicateType").GetString());
            Assert.Equal(SubjectName, subjects[0].GetProperty("name").GetString());
        }
        finally
        {
            CleanupParent(temp);
        }
    }

    // =====================================================================
    // C. Env-driven CI mode — the host reads the INJECTED paths (proves the producer wiring seam).
    // =====================================================================

    // Tests INV-007 [integration]: EmitFromEnvironment with a FAKE getenv returning an injected temp
    // receipt + a temp out writes the INJECTED out path (NOT the suite path), and the emitted
    // Statement binds the INJECTED receipt bytes — NOT the fixture. The injected receipt is the
    // fixture bytes plus a trailing newline: System.Text.Json allows trailing whitespace after the
    // root object, so RunReceipt.FromJson still parses, but the raw bytes (and thus the sha256) MOVE.
    // B1: this is what catches a wrong GREEN that honours EMIT_STATEMENT_OUT but IGNORES
    // EMIT_STATEMENT_RECEIPT (re-reading the fixture in both branches) — a silent INV-006/010 break
    // that would sign a Statement bound to the fixture's subject instead of the real run's receipt.
    // RED: EmitFromEnvironment throws NotImplementedException -> fails before the assertions.
    [Fact]
    public void EmitFromEnvironment_ci_mode_reads_the_injected_receipt_and_out_paths()
    {
        string dir = NewTempDir();
        string injectedReceipt = Path.Combine(dir, "in-receipt.json");

        byte[] fixtureBytes = FixtureBytes();
        // DISTINCT from the fixture (trailing newline -> still valid JSON, different bytes/sha256).
        byte[] injectedBytes = fixtureBytes.Concat(new byte[] { (byte)'\n' }).ToArray();
        File.WriteAllBytes(injectedReceipt, injectedBytes);

        string injectedSha = Convert.ToHexString(SHA256.HashData(injectedBytes)).ToLowerInvariant();
        string fixtureSha = Convert.ToHexString(SHA256.HashData(fixtureBytes)).ToLowerInvariant();
        Assert.NotEqual(fixtureSha, injectedSha); // the injected receipt is genuinely a different subject.

        string injectedOut = Path.Combine(dir, "injected-statement.json");
        string suitePath = Path.Combine(dir, "SUITE-must-not-be-written.json");

        Func<string, string?> fakeEnv = key => key switch
        {
            EnvKeyReceipt => injectedReceipt,
            EnvKeyOut => injectedOut,
            _ => null,
        };

        try
        {
            DeterminismStatementEmitter.EmitFromEnvironment(fakeEnv, FixtureReceiptPath(), suitePath);

            Assert.True(File.Exists(injectedOut), "INV-007: CI mode must write the INJECTED EMIT_STATEMENT_OUT path.");
            Assert.False(File.Exists(suitePath), "INV-007: CI mode must NOT write the suite fallback path.");

            // Byte-exact over the INJECTED receipt bytes (the canonical serializer's oracle).
            Assert.Equal(ExpectedStatementBytes(injectedBytes), File.ReadAllBytes(injectedOut));

            // DISCRIMINATING: the emitted subject sha256 binds the INJECTED receipt, and is NOT the
            // fixture's sha256 — so "reads the fixture, ignores EMIT_STATEMENT_RECEIPT" FAILS here.
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(injectedOut));
            string subjectSha = doc.RootElement
                .GetProperty("subject")[0].GetProperty("digest").GetProperty("sha256").GetString()!;
            Assert.Equal(injectedSha, subjectSha);
            Assert.NotEqual(fixtureSha, subjectSha);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // =====================================================================
    // D. Suite mode — no env set -> writes the suite temp path (proves the fact passes in the gate).
    // =====================================================================

    // Tests INV-007 [integration]: EmitFromEnvironment with a getenv returning null for BOTH keys
    // writes the suite temp path and it byte-matches the canonical serializer over the fixture —
    // proving the host [Fact] passes in the normal gate with no env set. RED: throws -> fails.
    [Fact]
    public void EmitFromEnvironment_suite_mode_null_env_writes_the_suite_fallback_path()
    {
        Func<string, string?> nullEnv = _ => null;
        string suiteOut = NewTempOut();
        try
        {
            DeterminismStatementEmitter.EmitFromEnvironment(nullEnv, FixtureReceiptPath(), suiteOut);

            Assert.True(File.Exists(suiteOut), "INV-007: suite mode must write the suite fallback path.");
            Assert.Equal(ExpectedStatementBytes(FixtureBytes()), File.ReadAllBytes(suiteOut));
        }
        finally
        {
            CleanupParent(suiteOut);
        }
    }

    // Tests INV-007 [integration] (boundary): an env value present but EMPTY is NOT "both non-empty",
    // so the host falls back to suite mode (writes the suite path, leaves the injected out unwritten).
    // Guards the "if BOTH non-empty" gate against an empty-string false-positive CI-mode dispatch.
    // RED: throws -> fails.
    [Fact]
    public void EmitFromEnvironment_empty_env_value_falls_back_to_suite_mode()
    {
        string dir = NewTempDir();
        string injectedOut = Path.Combine(dir, "should-stay-absent.json");
        string suiteOut = Path.Combine(dir, "suite-statement.json");

        // receipt key EMPTY, out key SET -> NOT both non-empty -> suite mode.
        Func<string, string?> partialEnv = key => key switch
        {
            EnvKeyReceipt => "",
            EnvKeyOut => injectedOut,
            _ => null,
        };

        try
        {
            DeterminismStatementEmitter.EmitFromEnvironment(partialEnv, FixtureReceiptPath(), suiteOut);

            Assert.True(File.Exists(suiteOut), "INV-007: an empty EMIT_STATEMENT_RECEIPT must fall back to suite mode.");
            Assert.False(File.Exists(injectedOut), "INV-007: an empty env value must NOT trigger CI mode.");
            Assert.Equal(ExpectedStatementBytes(FixtureBytes()), File.ReadAllBytes(suiteOut));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // =====================================================================
    // E. Fail-closed — missing receipt: Emit THROWS and leaves NO output file (AP-001 accept-side).
    // =====================================================================

    // Tests INV-007 [unit] (fail-closed): Emit over a nonexistent receipt path THROWS a REAL error
    // and writes NO output file. The exception must NOT be a NotImplementedException — that is the
    // RED-driver: the stub throws NotImplementedException, so this fails now; GREEN throws a genuine
    // file/parse error (e.g. FileNotFoundException) and writes nothing, so this passes. Asserting
    // "not NotImplementedException" (rather than a pinned type) keeps GREEN free to choose the exact
    // fail-closed exception while still proving a partial/empty output is never left behind.
    [Fact]
    public void Emit_missing_receipt_throws_and_writes_no_output_file()
    {
        string dir = NewTempDir();
        string missingReceipt = Path.Combine(dir, "does-not-exist.json");
        string temp = Path.Combine(dir, "out-statement.json");
        try
        {
            Exception ex = Assert.ThrowsAny<Exception>(
                () => DeterminismStatementEmitter.Emit(missingReceipt, temp));
            Assert.IsNotType<NotImplementedException>(ex); // RED: stub throws NotImplementedException.
            Assert.False(File.Exists(temp), "AP-001: a missing receipt must leave NO output file (fail closed).");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // =====================================================================
    // F. Fail-closed — malformed receipt: Emit THROWS and writes no output file.
    // =====================================================================

    // Tests INV-007 [unit] (fail-closed): a receipt file whose bytes are NOT valid JSON makes Emit
    // THROW (a real parse error, NOT NotImplementedException) and write no output. RED-driver: the
    // stub throws NotImplementedException. GREEN: RunReceipt.FromJson -> JsonException, nothing written.
    [Fact]
    public void Emit_malformed_receipt_throws_and_writes_no_output_file()
    {
        string dir = NewTempDir();
        string badReceipt = Path.Combine(dir, "malformed-receipt.json");
        File.WriteAllText(badReceipt, "not json");
        string temp = Path.Combine(dir, "out-statement.json");
        try
        {
            Exception ex = Assert.ThrowsAny<Exception>(
                () => DeterminismStatementEmitter.Emit(badReceipt, temp));
            Assert.IsNotType<NotImplementedException>(ex); // RED: stub throws NotImplementedException.
            Assert.False(File.Exists(temp), "AP-001: a malformed receipt must leave NO output file (fail closed).");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // =====================================================================
    // G. Filter-target existence + shape (reflection, NON-nesting — does NOT run `dotnet test`).
    // =====================================================================

    // Tests INV-007 [integration] (workflow contract GUARD): the test assembly contains a public
    // method whose full name contains "EmitDeterminismStatement", it carries [Fact], and is
    // parameterless — so the workflow `--filter 'FullyQualifiedName~EmitDeterminismStatement'` binds
    // to a REAL invocable target (PMB-001 spirit, without nesting dotnet test). This is a structural
    // GUARD: it is green once this file authors the [Fact] and stays green through GREEN; its job is
    // to catch a future rename that would silently break the workflow filter, not to drive RED.
    [Fact]
    public void Filter_target_method_exists_is_a_fact_and_is_parameterless()
    {
        MethodInfo[] candidates = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains(FilterTargetName, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(candidates); // the workflow --filter substring must bind to a real method.

        MethodInfo target = Assert.Single(candidates);
        Assert.True(
            target.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute"),
            "INV-007: the EmitDeterminismStatement filter target must be an xUnit [Fact].");
        Assert.Empty(target.GetParameters()); // parameterless -> the plain --filter runs it directly.

        // Full name (Type.Method) really contains the filter substring the workflow greps.
        string fullName = target.DeclaringType!.FullName + "." + target.Name;
        Assert.Contains(FilterTargetName, fullName);
    }
}
