namespace Identity.Infrastructure.Seed;

/// <summary>
/// Creates the built-in administrator when it is absent.
/// </summary>
public interface IDefaultUserInitializer
{
    Task Initialize(CancellationToken cancellationToken = default);
}