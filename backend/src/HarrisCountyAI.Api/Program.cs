using System.Text.Json.Serialization;
using HarrisCountyAI.Api.Extensions;
using HarrisCountyAI.Application;
using HarrisCountyAI.Infrastructure;
using HarrisCountyAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiAuthorization();
// After AddControllers: replaces the ProblemDetailsFactory MVC registers.
builder.Services.AddApiErrorHandling();

var app = builder.Build();
app.UseObservability();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Harris County AI API v1");
    });

    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsAtStartup"))
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.Migrate();
    }
}

// Wraps everything below it, so an unhandled failure anywhere in routing,
// authentication, or a controller becomes a problem document rather than a
// bare 500 with a stack trace.
app.UseApiErrorHandling();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program;
