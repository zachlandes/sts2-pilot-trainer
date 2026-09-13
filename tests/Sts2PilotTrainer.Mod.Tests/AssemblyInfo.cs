// These tests load assemblies into this process and then ask the process what it
// has loaded. Running two of them at once makes that question racy: whichever class
// loads the mod first decides where the game assembly is resolved from, and a suite
// whose answer depends on scheduling is a suite that will fail on somebody else's
// machine for a reason nobody can reproduce.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

// A second thing about loading, learned the hard way: a generic method in this
// assembly whose type constraint is one of the game's types - `where TCard :
// CardModel` - breaks xUnit's discovery of the whole assembly ("could not find
// dependent assembly 'sts2'"), because the game is resolvable only once a test has
// started the host and discovery runs before any test does. Game types in a
// method's parameters or return type are fine; a constraint is not.
