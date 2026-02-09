namespace MJYY_Web_server.Middleware {
	public class ApiKeyMiddleware {
		private readonly RequestDelegate _next;
		private readonly IConfiguration _config;
		private readonly ILogger<ApiKeyMiddleware> _logger;

		public ApiKeyMiddleware(
			RequestDelegate next,
			IConfiguration config,
			ILogger<ApiKeyMiddleware> logger) {
			_next = next;
			_config = config;
			_logger = logger;
		}

		public async Task InvokeAsync(HttpContext context) {
			string path = context.Request.Path.Value?.ToLower() ?? "";
			if (path.StartsWith("/swagger")) {//跳过指定的路径检查
				await _next(context);
				return;
			}

			//配置文件中预期的api key
			string? expectedKey = _config["ENV:ApiKey"];
			//读取api key，优先从header读取，其次是query
			string? providedKey =
				context.Request.Headers["X-API-Key"].FirstOrDefault() ??
				context.Request.Query["apiKey"].FirstOrDefault();

			if (string.IsNullOrEmpty(expectedKey)) {//检测是否配置了api key
				_logger.LogError("ApiKey未在appsettings.json中配置");
				context.Response.StatusCode = 500;
				await context.Response.WriteAsJsonAsync(new { error = "server error" });
				return;
			}

			if (providedKey != expectedKey) {//验证失败处理
				_logger.LogWarning("API Key验证失败，IP: {IP}, Path: {Path}",
					context.Connection.RemoteIpAddress,
					context.Request.Path);

				context.Response.StatusCode = 401;
				await context.Response.WriteAsJsonAsync(new {
					error = "Unauthorized",
					message = "缺少或无效的API Key"
				});
				return;
			}

			await _next(context);//通过则继续处理
		}
	}
}
