using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MJYY_Web_server.Middleware;

namespace MJYY_Web_server.Controllers {
	[Route("[controller]")]
	[ApiController]
	public class GatewayController : ControllerBase {

		private readonly IConfiguration _config;
		private readonly ILogger<ApiKeyMiddleware> _logger;
		//用于将数据存储在内存中，程序关闭后数据即丢失
		private readonly IMemoryCache _cache;

		public GatewayController(
			IConfiguration config,
			ILogger<ApiKeyMiddleware> logger,
			IMemoryCache cache
			) {
			_config = config;
			_logger = logger;
			_cache = cache;

			//将配置文件中的网关配置载入内存字典
			gateways = ((Func<Dictionary<string, GatewayInfo>>)(() => {
				Dictionary<string, GatewayInfo> dict = new(StringComparer.OrdinalIgnoreCase);
				GatewayInfo[]? read = _config.GetSection("Config:Gateway:Target").Get<GatewayInfo[]>();
				if (read != null) {
					foreach (GatewayInfo gwi in read) {
						dict.Add(gwi.Name, gwi);
					}
				}
				return dict;
			}))();
			gatewayPasswd = _config.GetSection($"Config:Gateway:Password").Get<GatewayPassword>();
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
		public class GatewayPassword {
			public class LockClass {
				/// <summary>
				/// 是否启用密码锁定机制。当连续多次验证失败时锁定一段时间。
				/// </summary>
				public bool Enable { get; set; } = false;
				/// <summary>
				/// 锁定的基础时间长度，单位秒。
				/// </summary>
				public uint LockBaseTime { get; set; }
				/// <summary>
				/// 根据锁定次数增加而翻倍锁定基础时间的倍数
				/// </summary>
				public uint LockTimeMultiple { get; set; }
				/// <summary>
				/// 触发锁定的连续验证失败次数
				/// </summary>
				public ushort LockCountOfFail { get; set; }
				/// <summary>
				/// 触发过锁定后再次连续验证失败触发锁定的次数
				/// </summary>
				public ushort LockCountOfFailAgain { get; set; }
			}
			/// <summary>
			/// 连续验证失败锁定机制配置
			/// </summary>
			public LockClass Lock { get; set; } = new();
		}
		private class LockData {
			internal required string IP { get; set; }
			internal required uint FailCount { get; set; }
			internal required DateTime? UnLockTime { get; set; }
		}
		

		public class GatewayPostBody {
			public string? Password { get; set; } = null;
		}

		private readonly Dictionary<string, GatewayInfo> gateways;
		private readonly GatewayPassword? gatewayPasswd;

		
		private IActionResult Gateway(string gatewayName, string? path, GatewayPostBody? postBody=null) {
			if (!gateways.TryGetValue(gatewayName, out var gwInfo))
				return NotFound(new {
					code = 404,
				});

			if (gwInfo.Password.Enable) {
				if (gatewayPasswd != null) {
					//密码多次验证失败锁定机制
					if (gatewayPasswd.Lock.Enable) {
						string cachePath = $"Gateway.Password.Lock.{GetRemoteIpAddress(HttpContext)}";
						_cache.TryGetValue(
							cachePath,
							out LockData? lockData
							);
						if (lockData != null) {
							if (lockData.UnLockTime != null && lockData.UnLockTime > DateTime.Now) {
								_logger.LogWarning(
									"[Gateway Name: {gtwName}, IP: {ip}] 目标IP因多次验证失败导致其被锁定，因此拒绝对其进行密码验证。解锁时间：{unlockTime}"
									, gwInfo.Name
									, GetRemoteIpAddress(HttpContext)
									, lockData.UnLockTime
									);
								return Unauthorized(new {
									code = 401,
									error = "locked",
									errorId = 2,
									unlockTime = lockData.UnLockTime,
								});
							}
						}
					}
				}

				string passwd;
				if (gwInfo.Password.UseReuseValue) {
					string? read = _config.GetSection($"Config:Gateway:ReuseValue:{gwInfo.Password.Value}").Get<string>();
					if (read != null) 
						passwd = read!;
					else {
						_logger.LogError(
							"[Gateway Name: {gtwName}, IP: {ip}] 未找到指定复用值。复用值占位符：{reuseValueName}；Password/Value"
							, gwInfo.Name
							, GetRemoteIpAddress(HttpContext)
							, gwInfo.Password.Value
							);
						return StatusCode(500, new {
							code = 500,
						});
					}
				}
				else
					passwd = gwInfo.Password.Value;
				if (!(postBody!=null && postBody.Password != null && postBody.Password == passwd)) {
					_logger.LogWarning(
						"[Gateway Name: {gtwName}, IP: {ip}] 密码验证失败，输入：{inpPw}"
						, gwInfo.Name
						, GetRemoteIpAddress(HttpContext)
						, postBody?.Password
						);
					
					if (gatewayPasswd != null) {
						//密码多次验证失败锁定机制
						if (gatewayPasswd.Lock.Enable) {
							string targetIp = GetRemoteIpAddress(HttpContext)!;
							string cachePath = $"Gateway.Password.Lock.{targetIp}";
							_cache.TryGetValue(
								cachePath,
								out LockData? lockData
								);
							if (lockData != null) {
								lockData.FailCount++;
								if (lockData.FailCount >= gatewayPasswd.Lock.LockCountOfFail) {
									double lockTime = -1;
									if ((lockData.FailCount - gatewayPasswd.Lock.LockCountOfFail) % gatewayPasswd.Lock.LockCountOfFailAgain == 0) {
										uint lockCount = (uint)Math.Ceiling(
												(double)(lockData.FailCount - gatewayPasswd.Lock.LockCountOfFail) / (double)gatewayPasswd.Lock.LockCountOfFailAgain
											);
										lockTime = gatewayPasswd.Lock.LockBaseTime * Math.Pow(gatewayPasswd.Lock.LockTimeMultiple, lockCount);
									}

									if (lockTime != -1) {
										lockData.UnLockTime = DateTime.Now.AddSeconds(lockTime);

										_logger.LogWarning(
											"[Gateway Name: {gtwName}, IP: {ip}] 密码验证失败累计达到{failCount}次，执行锁定{lockTime}秒。解锁时间：{unlockTime}"
											, gwInfo.Name
											, GetRemoteIpAddress(HttpContext)
											, lockData.FailCount
											, lockTime
											, lockData.UnLockTime
										);
										_cache.Set(
											cachePath,
											lockData,
											TimeSpan.FromSeconds(lockTime * 5)
											);
										return Unauthorized(new {
											code = 401,
											error = "locked",
											errorId = 1,
											unlockTime = lockData.UnLockTime,
										});
									}
								}
							}
							else {
								lockData = new() {
									IP = targetIp,
									FailCount = 1,
									UnLockTime = null
								};
								_cache.Set(
											cachePath,
											lockData,
											TimeSpan.FromSeconds(gatewayPasswd.Lock.LockBaseTime * 10)
											);
							}
						}
					}
					return Unauthorized(new {
						code = 401,
						error = "wrong password",
						errorId = 0,
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
										"[Gateway Name: {gtwName}, IP: {ip}] 未找到指定复用值。复用值占位符：{reuseValueName}；Parameters/Value；参数名：{paramName}"
										, gwInfo.Name
										, GetRemoteIpAddress(HttpContext)
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

		[HttpPost]
		[Route("post/{gatewayName}/{**path}")]
		[Consumes("application/json")]
		public IActionResult GatewayPost(string gatewayName, string? path, [FromBody] GatewayPostBody postBody) 
			=> Gateway(gatewayName, path, postBody);
		[HttpGet]
		[Route("get/{gatewayName}/{**path}")]
		[Consumes("application/json")]
		public IActionResult GatewayGet(string gatewayName, string? path) 
			=> Gateway(gatewayName, path, null);



		private string? GetRemoteIpAddress(HttpContext context) =>
			(Convert.ToBoolean(_config["Config:Server:UseXFFRequestHeader"]) == true)
				? context.Request.Headers["X-Forwarded-For"].ToString()
				: context.Connection.RemoteIpAddress?.ToString();
	}
}
