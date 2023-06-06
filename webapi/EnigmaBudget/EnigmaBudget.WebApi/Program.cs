using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Infrastructure.SendInBlue.Model;
using EnigmaBudget.WebApi.Configuration;
using EnigmaBudget.WebApi.Middlewares;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

builder.ConfigureCORS();
builder.ConfigDataBases();

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


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

EncodeDecodeHelper.Init(builder.Configuration["Encoder:Key"]);
builder.Services.AddSingleton(_ => builder.Configuration.GetSection("Jwt").Get<AuthServiceOptions>());
builder.Services.AddSingleton(_ => builder.Configuration.GetSection("SendInBlue").Get<SendInBlueOptions>());


builder.Services.RegisterAutoMappers();
builder.Services.RegisterRepositories();
builder.Services.RegisterApplicationServices();

var app = builder.Build();
app.UseCors("enigmaapp");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ResponseResultMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

