using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// The tests share process-wide state (Engine.Items, Engine.Player, Options.CurrentOptions,
// Engine.StartupPath, the static packet handler tables), so they run one at a time - as they did under
// MSTest, which only parallelises when asked to. xunit.v3 4.0 removed
// CollectionBehavior.DisableTestParallelization; ParallelMode.None is the equivalent.
[assembly: Parallelization( Mode = ParallelMode.None )]
