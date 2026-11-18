namespace XTerminal.Tests;

/// <summary>
/// Serializes live private-server tests so concurrent SSH sessions do not contend on the
/// same authorized endpoint (timeouts/cancels under load). Does not disable product parallelism.
/// </summary>
[CollectionDefinition("LiveIntegration", DisableParallelization = true)]
public sealed class LiveIntegrationCollectionDefinition;
