// These tests load assemblies into this process and then ask the process what it
// has loaded. Running two of them at once makes that question racy: whichever class
// loads the mod first decides where the game assembly is resolved from, and a suite
// whose answer depends on scheduling is a suite that will fail on somebody else's
// machine for a reason nobody can reproduce.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
