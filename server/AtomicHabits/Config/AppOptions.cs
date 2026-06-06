namespace AtomicHabits.Config
{
    public class AppOptions
    {
        public const string SectionName = "App";

        public string WebBaseUrl { get; set; } = "http://localhost:5173";
        public string ResetPasswordPath { get; set; } = "/auth/reset-password";
    }

    public class CorsOptions
    {
        public const string SectionName = "Cors";

        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    }
}
