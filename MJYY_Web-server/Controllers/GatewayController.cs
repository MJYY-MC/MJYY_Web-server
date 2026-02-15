using Microsoft.AspNetCore.Mvc;
using MJYY_Web_server.Middleware;

namespace MJYY_Web_server.Controllers {
	[Route("[controller]")]
	[ApiController]
	public class GatewayController : ControllerBase {

		private readonly IConfiguration _config;
		private readonly ILogger<ApiKeyMiddleware> _logger;

		public GatewayController(
			IConfiguration config,
			ILogger<ApiKeyMiddleware> logger
			) {
			_config = config;
			_logger = logger;

			//将配置文件中的网关配置载入内存字典
			Gateways = ((Func<Dictionary<string, GatewayInfo>>)(() => {
				Dictionary<string, GatewayInfo> dict = new(StringComparer.OrdinalIgnoreCase);
				GatewayInfo[]? read = _config.GetSection("Config:Gateway:Target").Get<GatewayInfo[]>();
				if (read != null) {
					foreach (GatewayInfo gwi in read) {
						dict.Add(gwi.Name, gwi);
					}
				}
				return dict;
			}))();
		}

		public class GatewayInfo {
			/// <summary>
			/// 目标网关名称
			/// </summary>
			public required string Name { get; set; }
			/// <summary>
			/// 目标地址
			/// </summary>
			public required string Url { get; set; }
			public enum ResultTypeEnum {
				json,
				iframe
			}
			/// <summary>
			/// 返回类型
			/// </summary>
			public ResultTypeEnum ResultType { get; set; } = ResultTypeEnum.json;
			public class PasswordClass {
				/// <summary>
				/// 是否启用密码验证
				/// </summary>
				public required bool Enable { get; set; }
				/// <summary>
				/// 密码
				/// </summary>
				public required string Value { get; set; }
				/// <summary>
				/// 是否在Value中使用复用值。如果为true，则Value的值将被视为一个占位符
				/// </summary>
				public bool UseReuseValue { get; set; } = false;
			}
			/// <summary>
			/// 密码验证配置
			/// </summary>
			public PasswordClass Password { get; set; } = new() {
				Enable = false,
				Value = ""
			};
			public class Parameter {
				/// <summary>
				/// 参数名
				/// </summary>
				public required string Name { get; set; }
				/// <summary>
				/// 参数值
				/// </summary>
				public required object Value { get; set; }
				/// <summary>
				/// 是否在Value中使用复用值。如果为true，则Value的值将被视为一个占位符
				/// </summary>
				public bool UseReuseValue { get; set; } = false;
			}
			/// <summary>
			/// 返回json时附加的参数，仅json模式时生效
			/// </summary>
			public Parameter[] Parameters { get; set; } = [];
		}
		public class GatewayPostBody {
			public string? Password { get; set; } = null;
		}

		private readonly Dictionary<string, GatewayInfo> Gateways;

		[Route("{gatewayName}/{**path}")]
		[HttpGet]
		[HttpPost]
		[Consumes("application/json")]
		public IActionResult Gateway(string gatewayName, string? path, [FromBody] GatewayPostBody postBody) {
			if (!Gateways.TryGetValue(gatewayName, out var gwInfo))
				return NotFound(new {
					code = 404,
				});

			if (gwInfo.Password.Enable) {
				string passwd;
				if (gwInfo.Password.UseReuseValue) {
					string? read = _config.GetSection($"Config:Gateway:ReuseValue:{gwInfo.Password.Value}").Get<string>();
					if (read != null) 
						passwd = read!;
					else {
						_logger.LogError(
							"[Gateway Name: {gtwName}] 未找到指定复用值。复用值占位符：{reuseValueName}；Password/Value"
							, gwInfo.Name
							, gwInfo.Password.Value
							);
						return StatusCode(500, new {
							code = 500,
						});
					}
				}
				else
					passwd = gwInfo.Password.Value;
				if (!(postBody.Password != null && postBody.Password == passwd)) {
					_logger.LogWarning(
						"[Gateway Name: {gtwName}] 密码验证失败，输入：{inpPw}"
						, gwInfo.Name
						, postBody.Password
						);
					return Unauthorized(new {
						code = 401,
						error = "wrong password"
					});
				}
			}

			switch (gwInfo.ResultType) {
				case GatewayInfo.ResultTypeEnum.json: {
						Dictionary<string, object> resParas = [];
						foreach(var p in gwInfo.Parameters) {
							object val;
							if (p.UseReuseValue) {
								object? read = _config.GetSection($"Config:Gateway:ReuseValue:{p.Value}").Get<object>();
								if (read != null)
									val = read!;
								else {
									_logger.LogError(
										"[Gateway Name: {gtwName}] 未找到指定复用值。复用值占位符：{reuseValueName}；Parameters/Value；参数名：{paramName}"
										, gwInfo.Name
										, p.Value
										, p.Name
										);
									return StatusCode(500, new {
										code = 500,
									});
								}
							}
							else
								val = p.Value;
							resParas.Add(p.Name, val);
						}

						return Ok(new {
							url = gwInfo.Url,
							paras = resParas
						});
					}
				case GatewayInfo.ResultTypeEnum.iframe: {
						IActionResult iframeContent(string name, string url) => 
							Content($@"
<!DOCTYPE html>
<html style='width:100%;height:100%;'>
<head>
	<title>{name}</title>
</head>
<body style='margin:0;width:100%;height:100%;'>
    <iframe src='{url}' 
			style='width:100%;height:100%;border:none' 
            sandbox='allow-scripts allow-same-origin allow-forms'
	></iframe>
</body>
</html>"
							, "text/html");

						if (gwInfo.Parameters.Length > 0) 
							_logger.LogWarning("参数无法通过iframe模式返回，需更改为json模式或删除沉冗参数");
						return iframeContent(gatewayName, gwInfo.Url);
					}
				default:
					return NotFound();
			}
			
		}
	}
}
