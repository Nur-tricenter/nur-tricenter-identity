using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Identity.App.Utils;

/// <summary>
/// Serialises a <see cref="HealthReport"/> as JSON for <c>/health</c>.
/// </summary>
/// <remarks>
/// The framework default emits bare text (<c>Healthy</c> / <c>Unhealthy</c>), which tells anything
/// deciding WHY an instance is unhealthy nothing. Consul only looks at the status code, so plain
/// text would technically satisfy the probe — but this same endpoint is what a human runs during
/// an incident, and it is what the health UI is built around.
/// <para>
/// The payload keeps the same <c>{ success, message, data }</c> envelope every other endpoint in
/// this API uses, so tooling that already reads identity responses needs no second parser.
/// </para>
/// </remarks>
public static class HealthReportWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            success = report.Status == HealthStatus.Healthy,
            message = report.Status.ToString(),
            data = new
            {
                status = report.Status.ToString(),
                totalDurationMilliseconds = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMilliseconds = entry.Value.Duration.TotalMilliseconds,
                    error = entry.Value.Exception?.Message
                })
            }
        };

        await context.Response.WriteAsJsonAsync(payload);
    }
}