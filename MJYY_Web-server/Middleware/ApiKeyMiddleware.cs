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
			if (Convert.ToBoolean(_config["Config:ApiKey:Enable"])) {
				string path = context.Request.Path.Value?.ToLower() ?? "";
				if (path.StartsWith("/swagger")) {//跳过指定的路径检查
					goto defEnd;
				}

				//配置文件中预期的api key
				string? expectedKey = _config["Config:ApiKey:Key"];
				//读取api key，优先从header读取，其次是query
				string? providedKey =
					context.Request.Headers["X-API-Key"].FirstOrDefault() ??
					context.Request.Query["apiKey"].FirstOrDefault();

				if (string.IsNullOrEmpty(expectedKey)) {//检测是否配置了api key
					_logger.LogError(
						"[IP: {ip}, Port: {port}, Path: {path}][500] ApiKey未在appsettings.json中配置"
						, context.Connection.RemoteIpAddress
						, context.Connection.RemotePort
						, context.Request.Path
						);
					context.Response.StatusCode = 500;
					await context.Response.WriteAsJsonAsync(new { code = 500 });
					return;
				}

				if (providedKey != expectedKey) {//验证失败处理
					_logger.LogWarning(
						"[IP: {ip}, Port: {port}, Path: {path}][401] API Key 验证失败"
						, context.Connection.RemoteIpAddress
						, context.Connection.RemotePort
						, context.Request.Path
						);

					context.Response.StatusCode = 401;
					await context.Response.WriteAsJsonAsync(new {
						error = "Unauthorized",
						code = 401,
						message = "缺少或无效的API Key"
					});
					return;
				}
			}
		defEnd:
			_logger.LogInformation(
				"[IP: {ip}, Port: {port}, Path: {path}][{sCode}] Protocol: {protocol}"
				, context.Connection.RemoteIpAddress
				, context.Connection.RemotePort
				, context.Request.Path
				, context.Response.StatusCode
				, context.Request.Protocol
				);
			await _next(context);//通过则继续处理
		}
	}
}
