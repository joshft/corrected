// fixtures/stdlib-anchor.dfy — spike-only Route B anchor fixture
// (OQ-004 / INV-002, resolved during RED). Verified with
// --standard-libraries=true: Compilation adds
// dllresource://DafnyPipeline/DafnyStandardLibraries.doo to the file set and
// DafnyCore loads it from the DafnyPipeline assembly's embedded resources via
// Assembly.Load("DafnyPipeline") + GetManifestResourceStream (dafny v4.11.0:
// Source/DafnyCore/DafnyMain.cs:21-28,
// Source/DafnyCore/Pipeline/Compilation.cs:181-203,
// Source/DafnyCore/DafnyFile.cs:214-222). The removal/differential test proves
// the consumption matters: with --standard-libraries=false this fixture fails
// resolution; with DafnyPipeline.dll removed, the standard-libraries run fails
// with the missing-assembly/missing-resource typed error.
import opened Std.Wrappers

method UsesStdWrappers() returns (r: Option<int>)
  ensures r.Some? && r.value == 42
{
  r := Some(42);
}
