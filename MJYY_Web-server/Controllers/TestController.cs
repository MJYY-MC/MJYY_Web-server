using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MJYY_Web_server.Controllers {
	[Route("api/[controller]")]
	[ApiController]
	public class TestController : ControllerBase {
		[HttpGet]
		public IActionResult Get() {
			return Ok(new {
				message = "test text",
				time = DateTime.Now
			});
		}
	}
}
