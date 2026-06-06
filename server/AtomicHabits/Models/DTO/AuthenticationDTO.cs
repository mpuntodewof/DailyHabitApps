namespace AtomicHabits.Models.DTO
{
    public class AuthenticationDTO
    {
    }

    public class RegisterDto
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
    }

    public class LoginDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class ForgotPasswordDto
    {
        public string Email { get; set; }
    }

    public class ResetPasswordDto
    {
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    // Additional DTOs for JWT Authentication Flow
    public class AuthResponseDto
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
    }

    public class LogoutDto
    {
        public string RefreshToken { get; set; }
    }

    public class CheckUserDto
    {
        public string Email { get; set; }
        public string Username { get; set; }
    }

    public class ConfirmResetPasswordDto
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }

    public class UserProfileDto
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class RefreshTokenDto
    {
        public string RefreshToken { get; set; }
    }

    public class VerifyTwoFactorDto
    {
        public string TwoFactorToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class TwoFactorCodeDto
    {
        public string Code { get; set; } = string.Empty;
    }

    public class UserInfoDto
    {
        public string UserId { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string Name { get; set; } = default!;
    }
}
