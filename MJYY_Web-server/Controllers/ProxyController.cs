using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Net.Http;
using Yarp.ReverseProxy.Forwarder;

namespace MJYY_Web_server.Controllers {
	[ApiController]
	public class ProxyController : ControllerBase {
		private readonly IHttpForwarder _forwarder;
		private readonly HttpMessageInvoker _httpClient;

		public ProxyController(IHttpForwarder forwarder) {
			_forwarder = forwarder;
			_httpClient = new HttpMessageInvoker(new SocketsHttpHandler());
		}

		public class ServiceInfo {
			public string Url { get; set; } = null!;
			public bool IsProxy { get; set; }
		}

		private static readonly Dictionary<string, ServiceInfo> Services = new(StringComparer.OrdinalIgnoreCase) {
			["mjyy"] = new ServiceInfo { Url = "https://mjyy.top", IsProxy = false },
			["pmjyy"] = new ServiceInfo { Url = "https://mjyy.top", IsProxy = true },
			["test"] = new ServiceInfo { Url = "https://bing.com", IsProxy = true },
		};

		[Route("web/auto/{serviceName}/{**path}")]
		[HttpGet]
		[HttpPost]
		[HttpPut]
		[HttpDelete]
		[HttpPatch]
		public IActionResult Auto(string serviceName, string? path) {
			if (!Services.TryGetValue(serviceName, out var service))
				return NotFound();

			string inputPath = path != null ? $"/{path}" : "";
			if (!service.IsProxy) {
				return Redirect($"/web/view/{serviceName}{inputPath}");
			}
			else {
				return Redirect($"/web/proxy/{serviceName}{inputPath}");
			}
		}

		[Route("web/view/{serviceName}/{**path}")]
		[HttpGet]
		public IActionResult View(string serviceName, string? path) {
			if (!Services.TryGetValue(serviceName, out var service))
				return NotFound();

			IActionResult IframeView(string name, string url) =>
			Content($@"
<!DOCTYPE html>
<html style='height:100%'>
<head><title>{name}</title><style>body{{margin:0;height:100vh}}</style></head>
<body>
    <iframe src='{url}' style='width:100%;height:100%;border:none' 
            sandbox='allow-scripts allow-same-origin allow-forms'></iframe>
</body>
</html>", "text/html");
			return IframeView(serviceName, service.Url);
		}



		//该方案计划放弃。已尝试过多种方法（未提交至git）解决head资源以及js动态资源获取的问题，均无法解决。
		[Route("web/proxy/{serviceName}/{**path}")]
		[HttpGet]
		[HttpPost]
		public async Task<IActionResult> Proxy(string serviceName, string? path) {
			if (!Services.TryGetValue(serviceName, out var service) || !service.IsProxy)
				return NotFound();

			HttpContext.Request.Path = "/" + (path ?? "");
			await _forwarder.SendAsync(HttpContext, service.Url, _httpClient);
			return new EmptyResult();
		}
	}
}
