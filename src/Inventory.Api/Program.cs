using Inventory.Application.Categories;
using Inventory.Application.Products;
using Inventory.Application.Stock;
using Inventory.Api;
using Inventory.Api.Authentication;
using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<InventoryExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TokenIssuer>();

builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<InventoryDbContext>()
    .AddSignInManager();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
var bootstrapAdmin = args.Contains("--bootstrap-admin");
var migrateDatabase = args.Contains("--migrate");
if (bootstrapAdmin && migrateDatabase)
{
    throw new InvalidOperationException("Run --migrate and --bootstrap-admin as separate commands.");
}
if (!bootstrapAdmin && !migrateDatabase)
{
    jwt.Validate();
}

builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwt.ValidationParameters();
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
                var id = context.Principal?.FindFirst("sub")?.Value;
                var stamp = context.Principal?.FindFirst("security_stamp")?.Value;
                var user = id is null ? null : await users.FindByIdAsync(id);
                if (user is null || string.IsNullOrEmpty(stamp) || user.SecurityStamp != stamp)
                {
                    context.Fail("Invalid user session.");
                }
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Computer Store Inventory API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("InventoryDatabase")
        ?? throw new InvalidOperationException("Connection string 'InventoryDatabase' is required.");

    options.UseSqlServer(connection, sql => sql.UseCompatibilityLevel(150));
});

var app = builder.Build();

if (migrateDatabase)
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await database.Database.MigrateAsync();
    Console.WriteLine("Database migrations completed.");
    return;
}

if (bootstrapAdmin)
{
    await AdminBootstrap.RunAsync(app.Services, app.Configuration);
    Console.WriteLine("Admin bootstrap completed.");
    return;
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
