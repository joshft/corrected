using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Non-parallel xUnit collection for test classes that spawn real OS subprocesses
/// (fork/exec): CosignRunner.Run, ProductionSurfaceScanner.Scan (real `dotnet build`),
/// `dotnet --version`, and any test that execs a committed .sh script or the gate.
///
/// Why: with no parallelism config, xUnit runs test classes concurrently by default.
/// Under concurrent fork/exec load a spawn can transiently fail (EAGAIN/ENOMEM); the
/// hardened runners correctly report LaunchFailed, but tests asserting a different
/// outcome (e.g. OversizeOutput) then flake -> intermittent CI red. A collection marked
/// DisableParallelization = true does NOT run in parallel with other collections, and
/// its member classes run sequentially, so all subprocess spawning is serialized while
/// the pure in-memory unit tests stay parallel.
///
/// This is a marker only: no fixture, no shared state, no test logic.
/// </summary>
[CollectionDefinition("Subprocess", DisableParallelization = true)]
public sealed class SubprocessCollection
{
}
