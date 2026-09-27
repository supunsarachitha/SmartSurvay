namespace SmartSurvey.Infrastructure.Persistence.Seed;

// STUB - replaced in Phase 4D (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Seeds roles, the initial admin and (optionally) demo data.</summary>
public sealed class DbSeeder
{
    /// <summary>Runs all seeding steps (idempotent).</summary>
    public Task SeedAsync(CancellationToken ct = default) => Task.CompletedTask;
}
