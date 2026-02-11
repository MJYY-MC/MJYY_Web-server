using Microsoft.AspNetCore.Mvc;

namespace MJYY_Web_server.Controllers {
	[Route("[controller]")]
	[ApiController]
	public class GatewayController : ControllerBase {

		private readonly IConfiguration _config;

		public GatewayController(IConfiguration config) {
			_config = config;

			//将配置文件中的网关配置载入内存字典
			Gateways= ((Func<Dictionary<string, GatewayInfo>>)(() => {
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
			public required string Name { get; set; }
			public required string Url { get; set; }
			public enum ResultTypeEnum {
				json,
				iframe
			}
			public ResultTypeEnum ResultType { get; set; } = ResultTypeEnum.json;
		}

		private readonly Dictionary<string, GatewayInfo> Gateways;

		[Route("{gatewayName}/{**path}")]
		[HttpGet]
		public IActionResult Gateway(string gatewayName, string? path) {
			if (!Gateways.TryGetValue(gatewayName, out var gwInfo))
				return NotFound();
			switch (gwInfo.ResultType) {
				case GatewayInfo.ResultTypeEnum.json:
					return Ok(new {
						url=gwInfo.Url
					});
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
						return iframeContent(gatewayName, gwInfo.Url);
					}
				default:
					return NotFound();
			}
			
		}
	}
}
