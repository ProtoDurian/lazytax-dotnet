using LazyTax.Testing;

[assembly: AssemblyFixture(typeof(KernelApp))]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1051", Justification = "Test calls do not need the per-test cancellation token.")]
