using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace UserService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        public AuthController(IConfiguration cfg) => _cfg = cfg;

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginDto dto)
        {
            // Demo: accept any username/password; in production validate credentials
            var jwtKey = _cfg["JWT_SECRET"] ?? "supersecret_jwt_key_for_demo";
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(jwtKey);

            var token = tokenHandler.CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, dto.Username) }),
                Expires = DateTime.UtcNow.AddHours(8),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            });

            return Ok(new { token = tokenHandler.WriteToken(token) });
        }

        public class LoginDto { public string Username { get; set; } = default!; public string Password { get; set; } = default!; }
    }
}
