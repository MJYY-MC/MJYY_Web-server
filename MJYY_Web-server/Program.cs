
using Microsoft.OpenApi.Models;
using MJYY_Web_server.Middleware;

namespace MJYY_Web_server
{
    public class Program
    {
        public static void Main(string[] args)
        {
			var builder = WebApplication.CreateBuilder(args);
			var env = builder.Environment;
            {
                string[] allowedOrigins = ((Func<string[]>)(() => {
                    string[]? origins = builder.Configuration.GetSection("ENV:Cors:Origins").Get<string[]>();
                    //Console.WriteLine(origins?[0]);

                    if (origins != null) {
                        return origins;
					}
                    else {
                        if (env.IsDevelopment()) {
                            return [
                                "http://localhost:5173"//vite调试地址
                                ];
                        }
                        else {
                            return [
                                ];
                        }
                    }
                }))();
                //CORS策略
                builder.Services.AddCors(options => {
                    options.AddPolicy("AllowFrontend", policy => {
                        policy
                            .WithOrigins(allowedOrigins)
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                            //.AllowCredentials();
                    });
                });
            }
			builder.Services.AddHttpForwarder();
			builder.Services.AddHttpClient();
			builder.Services.AddControllers();//控制器
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            if (env.IsDevelopment()) {
                builder.Services.AddSwaggerGen(options => {
                    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme(){
                        Description = "API Key认证",
                        Name = "X-API-Key",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                    });

                    options.AddSecurityRequirement(new OpenApiSecurityRequirement(){
                        {
                            new OpenApiSecurityScheme(){
                                Reference = new OpenApiReference{
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "ApiKey"
                                }
                            },
                            Array.Empty<string>()
                        }
                    });
                });
			}

            var app = builder.Build();
            //使用CORS策略
			app.UseCors("AllowFrontend");
            //使用api key中间件
			app.UseMiddleware<ApiKeyMiddleware>();
			// Configure the HTTP request pipeline.
			if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
