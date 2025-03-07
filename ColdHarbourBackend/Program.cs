var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure middleware in the correct order
app.UseRouting();
app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseWebSockets();

// Configure endpoints
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

app.Run();