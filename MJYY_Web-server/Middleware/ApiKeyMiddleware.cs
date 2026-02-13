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

		const string configHead = "Config:ApiKey:";

		public class ApikeyTarget {
			/// <summary>
			/// 是否启用api key验证
			/// </summary>
			public bool Enable { get; set; } = true;
			/// <summary>
			/// url前缀，用于匹配
			/// </summary>
			public required string UrlStart { get; set; }
			/// <summary>
			/// 使用的key名称，在配置文件中配置每个key的名称和对应值
			/// </summary>
			public string? KeyName { get; set; } = null;
		}

		public async Task InvokeAsync(HttpContext context) {
			if (Convert.ToBoolean(_config[$"{configHead}Enable"])) {
				async Task code500(string text) {
					_logger.LogError(
						"[IP: {ip}, Port: {port}, Path: {path}][500] {text}"
						, GetRemoteIpAddress(context)
						, context.Connection.RemotePort
						, context.Request.Path
						, text
						);
					context.Response.StatusCode = 500;
					await context.Response.WriteAsJsonAsync(new { code = 500 });
				}

				ApikeyTarget target=null!;
				{//根据请求路径匹配目标配置
					string path = context.Request.Path.Value?.ToLower() ?? "";
					ApikeyTarget[]? akts = _config.GetSection($"{configHead}Targets").Get<ApikeyTarget[]>();
					if (akts != null) {
						bool pass = false;
						foreach(ApikeyTarget akt in akts) {
							if (path.StartsWith(akt.UrlStart.ToLower())) {
								pass = true;
								target = akt;
								break;
							}
						}
						if (!pass) {
							await code500("未在ApiKey/Targets配置中匹配到目标");
							return;
						}
					}
					else {
						await code500("ApiKey/Targets配置为空");
						return;
					}
				}
				if (!target.Enable) {//未启用验证则放行
					goto defEnd;
				}

				//配置文件中预期的api key
				string? expectedKey = _config[$"{configHead}Keys:{target.KeyName}"];
				//读取api key，优先从header读取，其次是query
				string? providedKey =
					context.Request.Headers["X-API-Key"].FirstOrDefault() ??
					context.Request.Query["apiKey"].FirstOrDefault();

				if (string.IsNullOrEmpty(expectedKey)) {//检测是否配置了api key
					await code500("指定的ApiKey未在ApiKey/Keys中配置");
					return;
				}

				if (providedKey != expectedKey) {//验证失败处理
					_logger.LogWarning(
						"[IP: {ip}, Port: {port}, Path: {path}][401] API Key 验证失败"
						, GetRemoteIpAddress(context)
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
				, GetRemoteIpAddress(context)
				, context.Connection.RemotePort
				, context.Request.Path
				, context.Response.StatusCode
				, context.Request.Protocol
				);
			await _next(context);//通过则继续处理
		}

		private string? GetRemoteIpAddress(HttpContext context) =>
			(Convert.ToBoolean(_config["Config:Server:UseXFFRequestHeader"]) == true)
				? context.Request.Headers["X-Forwarded-For"].ToString()
				: context.Connection.RemoteIpAddress?.ToString();
	}
}
