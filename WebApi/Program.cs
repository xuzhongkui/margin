using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using WebApi.Data;
using WebApi.Services.Auth;
using WebApi.Services.ComAllocations;
using WebApi.Services.Infrastructure;
using WebApi.Services.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "请输入 JWT Token，格式：Bearer {token}，例如：Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IComAllocationService, ComAllocationService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddSignalR();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
if (jwtOptions is null || string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException("Jwt configuration is missing or invalid.");
}

var redisOptions = builder.Configuration.GetSection("Redis").Get<RedisOptions>();
if (redisOptions is null || string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
{
    throw new InvalidOperationException("Redis configuration is missing or invalid.");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisOptions.ConnectionString);

    if (!string.IsNullOrWhiteSpace(redisOptions.Password))
    {
        options.Password = redisOptions.Password;
    }

    // Keep Database config effective even if ConnectionString doesn't specify it.
    options.DefaultDatabase = redisOptions.Database;

    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = jwtKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("User", policy => policy.RequireRole("User", "Admin"));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'Default' not found.");
}

builder.Services.AddDbContext<SmsManageDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SmsManageDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    // 确保数据库结构与当前模型一致（包含 MessageReadReceipts 等新表）。
    await dbContext.Database.MigrateAsync();

    var hasAdmin = await dbContext.Users.AnyAsync(x => x.Role == WebApi.Models.UserRole.Admin);
    if (!hasAdmin)
    {
        var salt = passwordHasher.GenerateSalt();
        var adminUser = new WebApi.Models.User
        {
            UserName = "admin",
            PasswordSalt = salt,
            PasswordHash = passwordHasher.Hash("admin", salt),
            Role = WebApi.Models.UserRole.Admin,
            Remark = "Seeded admin"
        };

        dbContext.Users.Add(adminUser);
        await dbContext.SaveChangesAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI();


//app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<WebApi.Hubs.DeviceHub>("/hubs/device");

app.Run();
