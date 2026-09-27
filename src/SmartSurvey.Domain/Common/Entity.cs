namespace SmartSurvey.Domain.Common;

/// <summary>
/// Base class for every persisted entity. Identifiers are client-generated GUIDs so that
/// complete object graphs (e.g. a survey with sections, questions, options and logic rules
/// that reference each other) can be built in memory — in the Blazor builder or by an API
/// client — before anything is saved.
/// </summary>
public abstract class Entity
{
    /// <summary>Primary key (client generated, never database generated).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Entity with creation / modification metadata. The values are stamped automatically by the
/// <c>AuditableEntityInterceptor</c> in the Infrastructure layer — do not set them by hand.
/// </summary>
public abstract class AuditableEntity : Entity
{
    /// <summary>UTC timestamp of creation.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>User that created the entity (null for system/seed operations).</summary>
    public Guid? CreatedById { get; set; }

    /// <summary>UTC timestamp of the last modification.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>User that last modified the entity.</summary>
    public Guid? UpdatedById { get; set; }
}
