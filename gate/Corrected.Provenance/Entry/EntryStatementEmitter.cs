// INV-030 (P3 phase-entry, Group G / MA-C part b). The RUNNABLE entry-statement emitter host — the
// entry analog of DeterminismStatementEmitter. It turns a self-contained entry-receipt spec (the
// commit-X + the three per-precondition evidence closures, inline) into entry-statement.json + the
// commit-X blob, through the SINGLE canonical byte-source EntryStatementCodec.SerializeEntryStatementJson
// — so the statement the signer signs (cosign attest-blob --statement) and the gate-side EntryVerifier
// reconstructs are byte-identical. The emission LOGIC lives here (BCL-only, Corrected.Provenance); the
// CI/mint producer drives it via a [Fact] host (dotnet test --filter EmitEntryStatement).
//
// FAIL-CLOSED (AP-001): a missing / unreadable / unparseable spec, or a spec missing commit_x or any
// of the three preconditions, THROWS and writes NO output file (never a partial write). Every throwing
// step runs BEFORE any write.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Corrected.Provenance.InToto;

namespace Corrected.Provenance.Entry;

/// <summary>
/// The runnable entry-statement emitter host (INV-030). BCL-only. It reads a self-contained entry-
/// receipt spec, builds the entry in-toto Statement through <see cref="EntryAttestation.BuildEntryStatement"/>
/// + the ONE canonical serializer <see cref="EntryStatementCodec.SerializeEntryStatementJson"/>, and
/// writes those EXACT bytes out (statement) plus the commit-X representation (the cosign check-claims
/// blob). The statement bytes are the only byte-source shared with the verifier's parse.
/// </summary>
public static class EntryStatementEmitter
{
    /// <summary>The env key naming the input entry-receipt spec (CI/mint producer mode).</summary>
    public const string EnvKeySpec = "EMIT_ENTRY_SPEC";

    /// <summary>The env key naming the output entry-statement.json path.</summary>
    public const string EnvKeyStatementOut = "EMIT_ENTRY_STATEMENT_OUT";

    /// <summary>The env key naming the output commit-X blob path.</summary>
    public const string EnvKeyBlobOut = "EMIT_ENTRY_BLOB_OUT";

    /// <summary>
    /// Read the entry-receipt spec at <paramref name="specPath"/>, build the entry Statement through
    /// the ONE canonical serializer, and write that EXACT string to <paramref name="statementOutPath"/>
    /// (UTF-8, NO BOM, no extra trailing newline) plus the commit-X UTF-8 bytes to
    /// <paramref name="commitBlobOutPath"/> (the cosign --check-claims blob whose sha256 == the signed
    /// commit subject). FAIL-CLOSED: any spec read / parse / shape error THROWS and writes NO output.
    /// </summary>
    public static void Emit(string specPath, string statementOutPath, string commitBlobOutPath)
    {
        // ORDERING IS LOAD-BEARING (fail-closed / AP-001): every throwing step (read, parse, build,
        // serialize) runs BEFORE any write, so a bad spec leaves NO output behind. Never pre-create
        // an output path.

        // 1. Read + parse the spec (missing/unreadable -> IOException; malformed -> JsonException).
        byte[] specBytes = File.ReadAllBytes(specPath);
        EntryReceiptSpec spec = ParseSpec(specBytes);

        // 2. Build the entry Statement over the parsed closures (the fully-tested builder + codec).
        InTotoStatement statement = EntryAttestation.BuildEntryStatement(
            spec.CommitX, spec.P1, spec.P2, spec.P3);
        string statementJson = EntryStatementCodec.SerializeEntryStatementJson(statement);

        // 3. Write the EXACT statement bytes: UTF-8, NO BOM, no extra trailing newline. Only now —
        //    after every throwing step succeeded — does any file touch the disk.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(statementOutPath, statementJson, utf8NoBom);

        // 4. Write the commit-X blob: the EXACT commit-X UTF-8 bytes (sha256 == the signed subjects[0]).
        File.WriteAllBytes(commitBlobOutPath, Encoding.UTF8.GetBytes(spec.CommitX));
    }

    /// <summary>
    /// Environment-driven dispatch for the CI/mint producer host. Reads
    /// <see cref="EnvKeySpec"/> / <see cref="EnvKeyStatementOut"/> / <see cref="EnvKeyBlobOut"/> via
    /// <paramref name="getenv"/>; if ALL THREE are non-empty it runs <see cref="Emit"/> over those
    /// injected paths (producer mode); otherwise it runs <see cref="Emit"/> over
    /// <paramref name="fixtureSpecPath"/> / <paramref name="suiteStatementOut"/> /
    /// <paramref name="suiteBlobOut"/> (suite self-test mode, so the host [Fact] passes in the normal
    /// gate with no env set).
    /// </summary>
    public static void EmitFromEnvironment(
        Func<string, string?> getenv,
        string fixtureSpecPath,
        string suiteStatementOut,
        string suiteBlobOut)
    {
        ArgumentNullException.ThrowIfNull(getenv);

        string? spec = getenv(EnvKeySpec);
        string? stmtOut = getenv(EnvKeyStatementOut);
        string? blobOut = getenv(EnvKeyBlobOut);

        // Producer mode: ALL THREE keys must be present AND non-empty. An EMPTY value is NOT "set".
        if (!string.IsNullOrEmpty(spec) && !string.IsNullOrEmpty(stmtOut) && !string.IsNullOrEmpty(blobOut))
        {
            Emit(spec, stmtOut, blobOut);
        }
        else
        {
            Emit(fixtureSpecPath, suiteStatementOut, suiteBlobOut);
        }
    }

    /// <summary>The parsed entry-receipt spec: the commit-X + the three per-precondition closures.</summary>
    private sealed class EntryReceiptSpec
    {
        public required string CommitX { get; init; }

        public required IReadOnlyDictionary<string, byte[]> P1 { get; init; }

        public required IReadOnlyDictionary<string, byte[]> P2 { get; init; }

        public required IReadOnlyDictionary<string, byte[]> P3 { get; init; }
    }

    /// <summary>
    /// Parse the entry-receipt spec JSON. Shape: <c>{ "commit_x": "&lt;id&gt;", "preconditions": {
    /// "P1": [ {"path": "&lt;repo-rel&gt;", "content": "&lt;utf8 bytes&gt;"}, ... ], "P2": [...],
    /// "P3": [...] } }</c>. FAIL-CLOSED: a non-object root, a missing/empty commit_x, or a missing
    /// precondition THROWS (so the emitter writes nothing).
    /// </summary>
    private static EntryReceiptSpec ParseSpec(byte[] specBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(specBytes);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("entry-receipt spec root must be a JSON object");
        }

        string commitX = root.TryGetProperty("commit_x", out JsonElement cx) && cx.ValueKind == JsonValueKind.String
            ? cx.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrEmpty(commitX))
        {
            throw new InvalidOperationException("entry-receipt spec is missing a non-empty commit_x");
        }

        if (!root.TryGetProperty("preconditions", out JsonElement pre) || pre.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("entry-receipt spec is missing the preconditions object");
        }

        return new EntryReceiptSpec
        {
            CommitX = commitX,
            P1 = ParseClosure(pre, "P1"),
            P2 = ParseClosure(pre, "P2"),
            P3 = ParseClosure(pre, "P3"),
        };
    }

    /// <summary>Parse one precondition's closure array into a path-&gt;UTF8(content) map. Throws if the precondition is absent.</summary>
    private static IReadOnlyDictionary<string, byte[]> ParseClosure(JsonElement preconditions, string key)
    {
        if (!preconditions.TryGetProperty(key, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"entry-receipt spec is missing the {key} precondition closure");
        }

        var closure = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (JsonElement e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"entry-receipt spec {key} closure entry is not an object");
            }

            string path = e.TryGetProperty("path", out JsonElement p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? string.Empty
                : string.Empty;
            string content = e.TryGetProperty("content", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException($"entry-receipt spec {key} closure entry has an empty path");
            }

            closure[path] = Encoding.UTF8.GetBytes(content);
        }

        return closure;
    }
}
