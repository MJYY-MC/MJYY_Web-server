using Microsoft.AspNetCore.Hosting;
using Microsoft.OpenApi.Models;
using MJYY_Web_server.Middleware;
using Serilog;

namespace MJYY_Web_server
{
    public class Program
    {
        public static void Main(string[] args)
        {
			var builder = WebApplication.CreateBuilder(args);

            {
                /*if(builder.Environment.IsDevelopment())
                    Serilog.Debugging.SelfLog.Enable(Console.Error);*/

                var loggerConfig = new LoggerConfiguration()
														.MinimumLevel.Information()
														.Enrich.FromLogContext()
                                                        .WriteTo.Console()//同时输出至控制台
                                                        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day/*按天输出日志文件*/);
                loggerConfig.ReadFrom.Configuration(builder.Configuration.GetSection("SerilogConfig"));//读取json中的配置

                Log.Logger = loggerConfig.CreateLogger();

                builder.Host.UseSerilog(Log.Logger);//将默认日志系统替换为serilog
            }
			Log.Information(
@"----------
谧静幽原官网服务后端
版本：动态构建版
----------"
);
            Log.Debug("服务端开始加载");
			var env = builder.Environment;
            {
                string[] allowedOrigins = ((Func<string[]>)(() => {
                    string[]? origins = builder.Configuration.GetSection("Config:Cors:Origins").Get<string[]>();
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
            {
                string? url = builder.Configuration.GetSection("Config:Server:Url").Get<string>();
                if (url != null)
                    builder.WebHost.UseUrls(url);
            }

			var app = builder.Build();
            {
				//PathBase不能只包含单独的斜杠；开头必须是斜杠，结尾不能是斜杠；可以为空或为null
				string? pathBase = builder.Configuration.GetSection("Config:Server:PathBase").Get<string>();
				if (pathBase != null)
					app.UsePathBase(pathBase);
            }
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

            Log.Debug("服务端加载完成，开始监听请求");
            app.Run();
            Log.Information("服务端已停止");
        }
    }
}
