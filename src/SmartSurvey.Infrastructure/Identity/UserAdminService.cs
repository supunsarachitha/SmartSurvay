using SmartSurvey.Application.Common;
using SmartSurvey.Application.Users;

namespace SmartSurvey.Infrastructure.Identity;

// STUB - replaced in Phase 4D (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>User administration via ASP.NET Core Identity.</summary>
public sealed class UserAdminService : IUserAdminService
{
    public Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserDto> GetAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserDto> SetRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserDto> LockAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UserDto> UnlockAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
}
