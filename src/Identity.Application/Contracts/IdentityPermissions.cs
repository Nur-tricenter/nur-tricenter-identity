namespace Identity.Application.Contracts;

/// <summary>
/// Every scope the identity service recognises.
/// </summary>
/// <remarks>
/// Identity owns its own permission list the same way the catalog owns
/// <c>CatalogPermissions</c>. The gateway aggregates the lists at runtime through service
/// discovery rather than hardcoding them, so a new service never requires a gateway change.
///
/// Two kinds of scope live here and the difference matters:
/// <list type="bullet">
///   <item><description><b>Resource scopes</b> (<c>identity.user:read</c>) name one capability on
///   one resource. The WebApi registers one authorization policy per entry, so a controller asks
///   for <c>[Authorize(Policy = IdentityPermissions.UserRead)]</c>.</description></item>
///   <item><description><b>The wildcard</b> (<see cref="All"/>) is not a policy. It is a sentinel
///   stored on an administrator's user record and expanded into every resource scope when a token
///   is minted, so an admin carries every permission without this service having to enumerate
///   them twice.</description></item>
/// </list>
/// </remarks>
public static class IdentityPermissions
{
    /// <summary>
    /// Sentinel meaning "every scope this service recognises". Held by an administrator and
    /// expanded at login; never registered as an authorization policy of its own.
    /// </summary>
    public const string All = "identity:*";

    public const string UserRead = "identity.user:read";
    public const string UserCreate = "identity.user:create";
    public const string UserEdit = "identity.user:edit";
    public const string UserManage = "identity.user:manage";

    public const string RoleRead = "identity.role:read";
    public const string RoleCreate = "identity.role:create";
    public const string RoleEdit = "identity.role:edit";
    public const string RoleManage = "identity.role:manage";

    /// <summary>
    /// Every resource scope, in one place so a policy cannot be registered without also being
    /// listed, and a typo cannot hide a permission.
    /// </summary>
    public static IReadOnlyList<string> PermissionsList { get; } = new[]
    {
        UserRead, UserCreate, UserEdit, UserManage,
        RoleRead, RoleCreate, RoleEdit, RoleManage
    };

    /// <summary>
    /// The scopes a token actually carries for a user. An administrator holding
    /// <see cref="All"/> receives the full expanded list rather than the wildcard, because the
    /// gateway and downstream services must be able to reason about concrete permissions
    /// without knowing that this service has an administrator concept at all.
    /// </summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> heldScopes)
    {
        var scopes = heldScopes?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                     ?? Array.Empty<string>();

        return scopes.Any(s => s is All)
            ? PermissionsList
            : scopes;
    }
}