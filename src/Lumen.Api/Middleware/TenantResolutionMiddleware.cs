namespace Lumen.Api.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentTenant currentTenant)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claim = context.User.FindFirst(JwtClaimConstants.TenantIdClaim)?.Value;
            if (Guid.TryParse(claim, out var tenantId))
            {
                currentTenant.TenantId = tenantId;
            }
        }

        await _next(context);
    }
}
