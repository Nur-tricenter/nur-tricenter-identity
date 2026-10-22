using Nur.Tricenter.Access;
using Nur.Tricenter.Access.Identity;

namespace Identity.Application.Contracts;

/// <summary>
/// The scopes this service recognises, sourced from the platform kernel.
/// </summary>
/// <remarks>
/// The NAMES live in <c>Nur.Tricenter.Access.Identity</c>, not here. That is what lets identity
/// grant catalog permissions to a platform administrator without depending on the catalog: both
/// read the same vocabulary from the kernel, and neither references the other.
/// </remarks>
public static class IdentityPermissions
{
    /// <summary>
    /// Sentinel meaning "every scope this service recognises". Held by an administrator and
    /// expanded at login into <see cref="PermissionsList"/>, so a token never carries it literally.
    /// </summary>
    public const string All = IdentityPermission.All;

    public const string Login = IdentityPermission.Login;
    public const string UserRead = IdentityPermission.Read;
    public const string UserCreate = IdentityPermission.Create;
    public const string UserEdit = IdentityPermission.Edit;
    public const string UserManage = IdentityPermission.Manage;

    public const string RoleRead = IdentityPermission.RoleRead;
    public const string RoleCreate = IdentityPermission.RoleCreate;
    public const string RoleEdit = IdentityPermission.RoleEdit;
    public const string RoleManage = IdentityPermission.RoleManage;

    /// <summary>Every scope this service defines.</summary>
    public static IReadOnlyList<string> PermissionsList { get; } = IdentityPermission.PermissionsList;

    /// <summary>
    /// The scopes a token actually carries for a user.
    /// </summary>
    /// <remarks>
    /// An account holding <see cref="PlatformPermissions.All"/> — the platform-wide sentinel —
    /// receives EVERY scope on the platform, including the catalog's. Without that, a platform
    /// administrator could administer identity and nothing else, because identity has no way to
    /// name a catalog permission without depending on the catalog.
    /// <para>
    /// An account holding only <see cref="All"/> (the identity wildcard) stays scoped to identity.
    /// The two sentinels are deliberately distinct: one means "I administer this service", the
    /// other means "I administer the platform".
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Expand(IEnumerable<string> heldScopes) =>
        PlatformPermissions.Expand(heldScopes);
}