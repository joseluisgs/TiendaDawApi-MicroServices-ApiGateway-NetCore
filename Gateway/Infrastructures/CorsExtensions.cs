using Microsoft.AspNetCore.Builder;
using Serilog;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Extension methods para aplicar la política CORS del gateway.
/// </summary>
public static class CorsExtensions {
    /// <summary>
    /// Aplica la política CORS configurada según el entorno
    /// ("AllowAll" en Development, "ProductionPolicy" en el resto).
    /// </summary>
    /// <param name="app">Pipeline de la aplicación.</param>
    /// <returns>IApplicationBuilder.</returns>
    public static IApplicationBuilder UseCorsPolicy(this IApplicationBuilder app) {
        var env = ((WebApplication)app).Environment;
        var policyName = env.IsDevelopment() ? "AllowAll" : "ProductionPolicy";

        Log.Information("Aplicando politica CORS: {PolicyName}", policyName);
        return app.UseCors(policyName);
    }
}
