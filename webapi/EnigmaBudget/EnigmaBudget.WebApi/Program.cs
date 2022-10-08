using EnigmaBudget.WebApi.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using System.Reflection;
using System.Text;
using Serilog;
using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.SendInBlue.Model;
using EnigmaBudget.Infrastructure.Helpers;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

var logFile = builder.Configuration.GetValue<string>("Logs:FileRoute");

builder.Services.AddTransient(_ => new MySqlConnection(builder.Configuration["MariaDB:ConnectionString"]));

builder.Services.AddCors(p => p.AddPolicy("enigmaapp", corsBuilder =>
{
    corsBuilder.WithOrigins(builder.Configuration["Cors:Origins"]).AllowAnyMethod().AllowAnyHeader();
}));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };

});


builder.Services.AddSwaggerGen(option =>
{
    option.SwaggerDoc("v1", new OpenApiInfo { Title = "Enigma API", Version = "v1" });

    option.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Por favor, ingrese un token válido",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });

    option.AddSecurityRequirement(new OpenApiSecurityRequirement()
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
        new string[] { }
    }
});
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<AuthServiceOptions>(_ => new AuthServiceOptions(
    builder.Configuration["Jwt:Issuer"],
    builder.Configuration["Jwt:Audience"],
    builder.Configuration["Jwt:Subject"],
    builder.Configuration["Jwt:Key"],
    builder.Configuration["ValidacionCorreoTemplate:AppUrl"]
    ));

builder.Services.AddSingleton<SendInBlueOptions>(_ => new SendInBlueOptions(
    builder.Configuration["SendInMail:ApiKey"],
    builder.Configuration["SendInMail:Uri"],
    builder.Configuration["ValidacionCorreoTemplate:Id"]
    ));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

EncodeDecodeHelper.Initialize(builder.Configuration);
DependencyInjectionExtensions.RegisterAutoMappers(builder.Services);
DependencyInjectionExtensions.RegisterRepositories(builder.Services);
DependencyInjectionExtensions.RegisterApplicationServices(builder.Services);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseCors("enigmaapp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

