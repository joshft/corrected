// GREEN — Route A harness executable (DD-001/DD-003): a thin child-process
// entry point. All orchestration (startup gate, sentinel machinery, probe
// execution, atomic evidence emission, exit-code contract, terminal verdict
// summary) lives in the Dafny-free HarnessCore; route-specific Dafny access
// lives behind the seam (INV-008).
return Corrected.Spike.Contracts.HarnessCore.Run("A", new Corrected.Spike.RouteA.RouteAAdapter(), args);
