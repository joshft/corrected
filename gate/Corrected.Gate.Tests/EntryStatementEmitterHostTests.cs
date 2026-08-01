using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corrected.Provenance.Entry;
using Corrected.Provenance.InToto;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 phase-entry INV-030 (Group G / MA-C part b) — the RUNNABLE entry-statement emitter host, the
/// entry analog of <see cref="DeterminismStatementEmitterHostTests"/>. The mint / CI producer triggers
/// the [Fact] <see cref="EmitEntryStatement"/> via <c>dotnet test --filter EmitEntryStatement</c>,
/// driven by env vars, to emit entry-statement.json + the commit-X blob. The emission LOGIC is
/// <see cref="EntryStatementEmitter"/> in Corrected.Provenance (BCL-only); the SINGLE canonical
/// <see cref="EntryStatementCodec.SerializeEntryStatementJson"/> is the ONLY byte-source, so the emitted
/// statement is byte-identical to what the gate-side EntryVerifier parses.
///
/// PURE in-process file I/O over the REAL Corrected.Provenance substrate (no subprocess, no mocks),
/// driven by the committed synthetic entry-receipt spec fixture. NOT [Collection("Subprocess")].
///
/// FIXED, LOAD-BEARING CONTRACT (the workflow, the fact name, and the env keys MUST agree):
///   * Fact/filter target : EmitEntryStatement
///   * env input spec      : EMIT_ENTRY_SPEC
///   * env output stmt     : EMIT_ENTRY_STATEMENT_OUT
///   * env output blob     : EMIT_ENTRY_BLOB_OUT
///   * committed fixture   : gate/Corrected.Gate.Tests/fixtures/provenance/entry-receipt.sample.json
///   * predicateType URI   : https://correctless.org/attestations/phase-entry/v1
///   * commit subject name : phase-entry-commit
/// </summary>
public class EntryStatementEmitterHostTests
{
    // Pinned contract literals — INDEPENDENT of the production consts (A4): a GREEN that re-freezes
    // either value fails here, not merely echoes itself.
    private const string EntryPredicateTypeUri = "https://correctless.org/attestations/phase-entry/v1";
    private const string CommitSubjectName = "phase-entry-commit";

    // The ONE shared source of the workflow --filter target name (A3).
    public const string FilterTargetName = "EmitEntryStatement";

    private static string FixtureSpecPath()
        => TestPaths.Fixture("provenance", "entry-receipt.sample.json");

    // Independently parse the committed spec (BCL) into commit-X + the three closures — the oracle
    // inputs, never via the emitter.
    private static (string CommitX, Dictionary<string, byte[]> P1, Dictionary<string, byte[]> P2, Dictionary<string, byte[]> P3)
        ParseFixtureSpec()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(FixtureSpecPath()));
        JsonElement root = doc.RootElement;
        string commitX = root.GetProperty("commit_x").GetString()!;
        JsonElement pre = root.GetProperty("preconditions");
        return (commitX, Closure(pre, "P1"), Closure(pre, "P2"), Closure(pre, "P3"));

        static Dictionary<string, byte[]> Closure(JsonElement pre, string key)
        {
            var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (JsonElement e in pre.GetProperty(key).EnumerateArray())
            {
                d[e.GetProperty("path").GetString()!] = Encoding.UTF8.GetBytes(e.GetProperty("content").GetString()!);
            }
            return d;
        }
    }

    // The canonical statement bytes the emitted file MUST equal (through the ONE serializer over the
    // spec's closures). The codec is pinned byte-for-byte against an independent oracle in
    // Inv030EntryVerifierTests, so it is a trusted expectation source here.
    private static byte[] ExpectedStatementBytes()
    {
        var (commitX, p1, p2, p3) = ParseFixtureSpec();
        InTotoStatement stmt = EntryAttestation.BuildEntryStatement(commitX, p1, p2, p3);
        return Encoding.UTF8.GetBytes(EntryStatementCodec.SerializeEntryStatementJson(stmt));
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "p3-entry-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* OS temp cleanup is the backstop */ }
    }

    // =====================================================================
    // The LOAD-BEARING workflow --filter target. Mode-agnostic: passes both in the normal gate (env
    // unset -> suite mode) and in the CI/mint producer (env set -> writes the injected outputs). DO
    // NOT RENAME without updating the workflow --filter AND env-key contract in lockstep.
    // =====================================================================

    // Tests INV-030 [integration]: the runnable host emits the entry Statement + commit-X blob through
    // the ONE canonical serializer, driven by the real process environment.
    [Fact]
    public void EmitEntryStatement()
    {
        string? specEnv = Environment.GetEnvironmentVariable(EntryStatementEmitter.EnvKeySpec);
        string? stmtEnv = Environment.GetEnvironmentVariable(EntryStatementEmitter.EnvKeyStatementOut);
        string? blobEnv = Environment.GetEnvironmentVariable(EntryStatementEmitter.EnvKeyBlobOut);
        bool ciMode = !string.IsNullOrEmpty(specEnv) && !string.IsNullOrEmpty(stmtEnv) && !string.IsNullOrEmpty(blobEnv);

        string dir = NewTempDir();
        string suiteStmt = Path.Combine(dir, "entry-statement.json");
        string suiteBlob = Path.Combine(dir, "entry-commit.blob");
        string expectedStmt = ciMode ? stmtEnv! : suiteStmt;
        string expectedBlob = ciMode ? blobEnv! : suiteBlob;

        try
        {
            EntryStatementEmitter.EmitFromEnvironment(
                Environment.GetEnvironmentVariable, FixtureSpecPath(), suiteStmt, suiteBlob);

            Assert.True(File.Exists(expectedStmt), $"INV-030: the host must write the entry Statement ({(ciMode ? "CI" : "suite")} mode).");
            Assert.True(File.Exists(expectedBlob), $"INV-030: the host must write the commit-X blob ({(ciMode ? "CI" : "suite")} mode).");
            using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(expectedStmt)); // parses
        }
        finally
        {
            if (!ciMode) { CleanupDir(dir); } // never delete a CI-mode injected out (the workflow hand-off needs it)
        }
    }

    // =====================================================================
    // A. Byte-equality anchor — Emit(fixtureSpec, temp) goes through the ONE serializer; the blob is
    //    the commit-X bytes whose sha256 == the emitted subjects[0] digest.
    // =====================================================================

    [Fact]
    public void Emit_writes_exact_canonical_statement_bytes_and_the_commit_blob()
    {
        byte[] expected = ExpectedStatementBytes();
        string dir = NewTempDir();
        string stmt = Path.Combine(dir, "entry-statement.json");
        string blob = Path.Combine(dir, "entry-commit.blob");
        try
        {
            EntryStatementEmitter.Emit(FixtureSpecPath(), stmt, blob);

            byte[] got = File.ReadAllBytes(stmt);
            Assert.Equal(expected, got);           // exact byte-for-byte equality with the canonical serializer
            Assert.Equal(expected.Length, got.Length); // no extra trailing newline / trailing bytes
            bool hasBom = got.Length >= 3 && got[0] == 0xEF && got[1] == 0xBB && got[2] == 0xBF;
            Assert.False(hasBom, "INV-030: the emitted Statement must carry NO UTF-8 BOM.");

            // The blob is the commit-X bytes; its sha256 equals the emitted commit subject digest.
            var (commitX, _, _, _) = ParseFixtureSpec();
            byte[] blobBytes = File.ReadAllBytes(blob);
            Assert.Equal(Encoding.UTF8.GetBytes(commitX), blobBytes);

            using JsonDocument doc = JsonDocument.Parse(got);
            string subject0Sha = doc.RootElement.GetProperty("subject")[0].GetProperty("digest").GetProperty("sha256").GetString()!;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(blobBytes)).ToLowerInvariant(), subject0Sha);
        }
        finally { CleanupDir(dir); }
    }

    // =====================================================================
    // B. The emitted statement is schema-VALID + carries the pinned predicate/subject (the emitter and
    //    the verifier's schema validator agree — the producer/consumer round-trip).
    // =====================================================================

    [Fact]
    public void Emit_output_is_schema_valid_and_binds_frozen_predicate_and_subject()
    {
        string dir = NewTempDir();
        string stmt = Path.Combine(dir, "entry-statement.json");
        string blob = Path.Combine(dir, "entry-commit.blob");
        try
        {
            EntryStatementEmitter.Emit(FixtureSpecPath(), stmt, blob);

            byte[] stmtBytes = File.ReadAllBytes(stmt);
            using (JsonDocument doc = JsonDocument.Parse(stmtBytes))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal(EntryPredicateTypeUri, root.GetProperty("predicateType").GetString()); // frozen literal
                Assert.Equal(4, root.GetProperty("subject").GetArrayLength());
                Assert.Equal(CommitSubjectName, root.GetProperty("subject")[0].GetProperty("name").GetString());
            }

            // The producer/consumer round-trip: the emitted statement PARSES + VALIDATES through the
            // gate-side codec + schema validator (what EntryVerifier runs on the decoded DSSE payload).
            (InTotoStatement? parsed, string? error) = EntryStatementCodec.ParseEntryStatement(stmtBytes);
            Assert.Null(error);
            Assert.NotNull(parsed);
            EntrySchemaResult schema = EntryAttestation.ValidateEntrySchema(parsed);
            Assert.True(schema.Valid, $"emitted entry statement must validate; reason='{schema.Reason}'");
        }
        finally { CleanupDir(dir); }
    }

    // =====================================================================
    // C. Env-driven CI/mint mode — the host reads the INJECTED paths (the producer wiring seam).
    // =====================================================================

    [Fact]
    public void EmitFromEnvironment_ci_mode_reads_the_injected_spec_and_out_paths()
    {
        string dir = NewTempDir();
        string injectedStmt = Path.Combine(dir, "injected-statement.json");
        string injectedBlob = Path.Combine(dir, "injected-commit.blob");
        string suiteStmt = Path.Combine(dir, "SUITE-stmt-must-not-be-written.json");
        string suiteBlob = Path.Combine(dir, "SUITE-blob-must-not-be-written.blob");

        Func<string, string?> fakeEnv = key => key switch
        {
            EntryStatementEmitter.EnvKeySpec => FixtureSpecPath(),
            EntryStatementEmitter.EnvKeyStatementOut => injectedStmt,
            EntryStatementEmitter.EnvKeyBlobOut => injectedBlob,
            _ => null,
        };

        try
        {
            EntryStatementEmitter.EmitFromEnvironment(fakeEnv, FixtureSpecPath(), suiteStmt, suiteBlob);

            Assert.True(File.Exists(injectedStmt), "CI mode must write the INJECTED statement path.");
            Assert.True(File.Exists(injectedBlob), "CI mode must write the INJECTED blob path.");
            Assert.False(File.Exists(suiteStmt), "CI mode must NOT write the suite fallback statement.");
            Assert.False(File.Exists(suiteBlob), "CI mode must NOT write the suite fallback blob.");
            Assert.Equal(ExpectedStatementBytes(), File.ReadAllBytes(injectedStmt));
        }
        finally { CleanupDir(dir); }
    }

    // =====================================================================
    // D. Suite mode — no env / empty env -> writes the suite paths (the fact passes in the gate).
    // =====================================================================

    [Fact]
    public void EmitFromEnvironment_null_env_writes_the_suite_fallback_paths()
    {
        Func<string, string?> nullEnv = _ => null;
        string dir = NewTempDir();
        string stmt = Path.Combine(dir, "entry-statement.json");
        string blob = Path.Combine(dir, "entry-commit.blob");
        try
        {
            EntryStatementEmitter.EmitFromEnvironment(nullEnv, FixtureSpecPath(), stmt, blob);
            Assert.True(File.Exists(stmt) && File.Exists(blob), "suite mode must write the suite fallback paths.");
            Assert.Equal(ExpectedStatementBytes(), File.ReadAllBytes(stmt));
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void EmitFromEnvironment_empty_env_value_falls_back_to_suite_mode()
    {
        string dir = NewTempDir();
        string injectedStmt = Path.Combine(dir, "should-stay-absent.json");
        string suiteStmt = Path.Combine(dir, "suite-statement.json");
        string suiteBlob = Path.Combine(dir, "suite-commit.blob");

        // spec key EMPTY, the two out keys SET -> NOT all three non-empty -> suite mode.
        Func<string, string?> partialEnv = key => key switch
        {
            EntryStatementEmitter.EnvKeySpec => "",
            EntryStatementEmitter.EnvKeyStatementOut => injectedStmt,
            EntryStatementEmitter.EnvKeyBlobOut => Path.Combine(dir, "should-also-stay-absent.blob"),
            _ => null,
        };

        try
        {
            EntryStatementEmitter.EmitFromEnvironment(partialEnv, FixtureSpecPath(), suiteStmt, suiteBlob);
            Assert.True(File.Exists(suiteStmt), "an empty EMIT_ENTRY_SPEC must fall back to suite mode.");
            Assert.False(File.Exists(injectedStmt), "an empty env value must NOT trigger CI mode.");
        }
        finally { CleanupDir(dir); }
    }

    // =====================================================================
    // E. Fail-closed — a missing / malformed / incomplete spec THROWS and leaves NO output (AP-001).
    // =====================================================================

    [Fact]
    public void Emit_missing_spec_throws_and_writes_no_output()
    {
        string dir = NewTempDir();
        string missing = Path.Combine(dir, "does-not-exist.json");
        string stmt = Path.Combine(dir, "out-statement.json");
        string blob = Path.Combine(dir, "out-commit.blob");
        try
        {
            Assert.ThrowsAny<Exception>(() => EntryStatementEmitter.Emit(missing, stmt, blob));
            Assert.False(File.Exists(stmt), "AP-001: a missing spec must leave NO statement file.");
            Assert.False(File.Exists(blob), "AP-001: a missing spec must leave NO blob file.");
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Emit_malformed_spec_throws_and_writes_no_output()
    {
        string dir = NewTempDir();
        string bad = Path.Combine(dir, "malformed-spec.json");
        File.WriteAllText(bad, "not json");
        string stmt = Path.Combine(dir, "out-statement.json");
        string blob = Path.Combine(dir, "out-commit.blob");
        try
        {
            Assert.ThrowsAny<Exception>(() => EntryStatementEmitter.Emit(bad, stmt, blob));
            Assert.False(File.Exists(stmt), "AP-001: a malformed spec must leave NO statement file.");
            Assert.False(File.Exists(blob), "AP-001: a malformed spec must leave NO blob file.");
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Emit_spec_missing_a_precondition_throws_and_writes_no_output()
    {
        string dir = NewTempDir();
        // Valid JSON + commit_x, but the preconditions object omits P3 -> fail closed.
        string bad = Path.Combine(dir, "incomplete-spec.json");
        File.WriteAllText(bad,
            "{\"commit_x\":\"0123456789abcdef0123456789abcdef01234567\"," +
            "\"preconditions\":{\"P1\":[{\"path\":\"a\",\"content\":\"x\"}],\"P2\":[{\"path\":\"b\",\"content\":\"y\"}]}}");
        string stmt = Path.Combine(dir, "out-statement.json");
        string blob = Path.Combine(dir, "out-commit.blob");
        try
        {
            Assert.ThrowsAny<Exception>(() => EntryStatementEmitter.Emit(bad, stmt, blob));
            Assert.False(File.Exists(stmt), "AP-001: a spec missing a precondition must leave NO statement file.");
            Assert.False(File.Exists(blob), "AP-001: a spec missing a precondition must leave NO blob file.");
        }
        finally { CleanupDir(dir); }
    }

    // =====================================================================
    // F. Filter-target existence + shape (reflection GUARD — catches a future rename of the --filter).
    // =====================================================================

    [Fact]
    public void Filter_target_method_exists_is_a_fact_and_is_parameterless()
    {
        MethodInfo[] candidates = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains(FilterTargetName, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(candidates);
        MethodInfo target = Assert.Single(candidates);
        Assert.True(
            target.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute"),
            "INV-030: the EmitEntryStatement filter target must be an xUnit [Fact].");
        Assert.Empty(target.GetParameters());
        Assert.Contains(FilterTargetName, target.DeclaringType!.FullName + "." + target.Name);
    }
}
