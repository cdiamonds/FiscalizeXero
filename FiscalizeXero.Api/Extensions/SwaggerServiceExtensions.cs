using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Api.Extensions
{
    public static class SwaggerServiceExtensions
    {
        public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                // REST group
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "FiscalizeXero.Api",
                    Version = "v1",
                    Description = "Fiscalize Xero REST API Documentation",
                    TermsOfService = new Uri("https://www.onadiamonds.com"),
                    Contact = new OpenApiContact
                    {
                        Name = "onadiamonds",
                        Email = "info@onadiamonds.com",
                        Url = new Uri("http://www.onadiamonds.com")
                    }
                });

                // OData group
                c.SwaggerDoc("odata", new OpenApiInfo
                {
                    Title = "FiscalizeXero.OData",
                    Version = "v1",
                    Description = "Fiscalize Xero OData API Documentation",
                    TermsOfService = new Uri("https://www.onadiamonds.com"),
                    Contact = new OpenApiContact
                    {
                        Name = "onadiamonds",
                        Email = "info@onadiamonds.com",
                        Url = new Uri("http://www.onadiamonds.com")
                    }
                });

                // Security setup - Use Http scheme with Bearer format for proper JWT handling
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Description = "JWT Authorization header using the Bearer scheme. Enter your token (without 'Bearer ' prefix).",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header
                });

                c.AddSecurityRequirement(_ =>
                {
                    var requirement = new OpenApiSecurityRequirement();
                    var schemeRef = new OpenApiSecuritySchemeReference("Bearer");
                    requirement.Add(schemeRef, new List<string>());
                    return requirement;
                });

                // XML comments
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);

                var xmlPath2 = Path.Combine(AppContext.BaseDirectory, "FiscalizeXero.Shared.xml");
                c.IncludeXmlComments(xmlPath2);
            });

            services.AddControllersWithViews()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            return services;
        }

        public static IApplicationBuilder UseSwaggerDocumentation(this IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("../swagger/v1/swagger.json", "Fiscalize.Api v1 (REST)");
                c.SwaggerEndpoint("../swagger/odata/swagger.json", "Fiscalize.Api v1 (OData)");

                c.DocumentTitle = "Fiscalize API Explorer";
                c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
            });

            return app;
        }
    }
}