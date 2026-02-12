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
				return NotFound();

			if (gwInfo.Password.Enable) {
				if (!(postBody.Password != null && postBody.Password == gwInfo.Password.Value)) {
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
							resParas.Add(p.Name, p.Value);
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
