using LazyTax.Testing;

[assembly: AssemblyFixture(typeof(KernelApp))]

// Test classes share one app and one outbox: a parallel class's DispatchOnce can claim this
// class's events, so the inbox assert races. Run them one at a time (as penalty does).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
