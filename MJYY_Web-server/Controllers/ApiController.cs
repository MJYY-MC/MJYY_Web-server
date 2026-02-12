using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MJYY_Web_server.Controllers {
	[Route("[controller]")]
	[ApiController]
	public class ApiController : ControllerBase {
		[Route("check")]
		[HttpGet]
		public IActionResult Check() => Ok();
	}
}
