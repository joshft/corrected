// INV-006/007/010 (P3-specific). The RUNNABLE test-host emitter seam: it turns the
// committed determinism RunReceipt bytes into handoff/determinism-statement.json through
// the SINGLE canonical byte-source DeterminismAttestation.SerializeStatementJson — so the
// emitted Statement the signer signs and the (future T3, INV-010) verifier reconstructs are
// byte-identical. The emission LOGIC lives here (BCL-only, in Corrected.Provenance); the CI
// producer drives it via a [Fact] host (dotnet test --filter EmitDeterminismStatement),
// replacing the current no-op "(DEFERRED real-run wiring)" echo in the producer job.
//
// GREEN implements both methods below. RED: both STUBs throw NotImplementedException so every
// host test fails as an EXCEPTION (never a compile error) — and the fail-closed tests fail
// because a NotImplementedException is NOT the real missing/parse fail-closed throw GREEN owes.
using System;
using System.IO;

namespace Corrected.Provenance.Determinism;

/// <summary>
/// The runnable determinism-statement emitter host (INV-007). BCL-only. It reads the receipt
/// bytes, builds the in-toto Statement through the ONE canonical serializer
/// (<see cref="DeterminismAttestation.SerializeStatementJson"/>), and writes those EXACT bytes
/// out — the only byte-source shared with the T3 verifier (INV-006/010). Fail-closed: a
/// missing / unreadable / unparseable receipt THROWS and writes NO output file (AP-001).
/// </summary>
public static class DeterminismStatementEmitter
{
    /// <summary>
    /// Read the receipt bytes from <paramref name="receiptPath"/>, parse them with
    /// <see cref="RunReceipt.FromJson"/>, serialize through
    /// <see cref="DeterminismAttestation.SerializeStatementJson"/>, and write that EXACT string
    /// to <paramref name="statementOutPath"/> (UTF-8, NO BOM, no extra trailing newline — the
    /// bytes must equal SerializeStatementJson's bytes exactly). FAIL-CLOSED: if the receipt is
    /// missing / unreadable / parse-fails, THROW and write NO output file (never a partial write).
    /// GREEN implements; the RED stub throws (STUB:TDD).
    /// </summary>
    public static void Emit(string receiptPath, string statementOutPath)
    {
        // ORDERING IS LOAD-BEARING (fail-closed / AP-001): the three steps that can throw
        // (read bytes, parse, serialize) ALL run BEFORE any write, so a missing / unreadable /
        // unparseable receipt leaves NO output file behind. Never pre-create the output path.

        // 1. Read the EXACT receipt bytes (missing/unreadable -> FileNotFoundException etc., fail-closed).
        byte[] receiptBytes = File.ReadAllBytes(receiptPath);

        // 2. Parse into the typed DTO (malformed JSON -> JsonException, fail-closed).
        RunReceipt receipt = RunReceipt.FromJson(receiptBytes);

        // 3. Serialize through the ONE canonical byte-source (INV-006/010).
        string json = DeterminismAttestation.SerializeStatementJson(receiptBytes, receipt);

        // 4. Write those EXACT bytes: UTF-8, NO BOM, no extra trailing newline. Only now — after
        //    every throwing step has succeeded — does any file touch the disk.
        File.WriteAllText(
            statementOutPath,
            json,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Environment-driven dispatch for the CI producer host. Reads the env keys
    /// <c>EMIT_STATEMENT_RECEIPT</c> and <c>EMIT_STATEMENT_OUT</c> via <paramref name="getenv"/>;
    /// if BOTH are non-empty it runs <see cref="Emit"/> over those injected paths (CI producer
    /// mode); otherwise it runs <see cref="Emit"/> over
    /// <paramref name="fixtureReceiptPath"/> / <paramref name="suiteTempOutPath"/> (suite
    /// self-test mode, so the host [Fact] passes in the normal gate with no env set).
    /// GREEN implements; the RED stub throws (STUB:TDD).
    /// </summary>
    public static void EmitFromEnvironment(
        System.Func<string, string?> getenv,
        string fixtureReceiptPath,
        string suiteTempOutPath)
    {
        ArgumentNullException.ThrowIfNull(getenv);

        string? r = getenv("EMIT_STATEMENT_RECEIPT");
        string? o = getenv("EMIT_STATEMENT_OUT");

        // CI producer mode: BOTH keys must be present AND non-empty. An EMPTY env value is NOT
        // "set" — string.IsNullOrEmpty makes an empty EMIT_STATEMENT_RECEIPT fall back to suite mode.
        if (!string.IsNullOrEmpty(r) && !string.IsNullOrEmpty(o))
        {
            Emit(r, o);
        }
        else
        {
            // Suite self-test mode: the host [Fact] passes in the normal gate with no env set.
            Emit(fixtureReceiptPath, suiteTempOutPath);
        }
    }
}
