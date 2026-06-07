namespace AtomicHabits.Config
{
    public class EmailOptions
    {
        public const string SectionName = "Smtp";

        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;

        // The SMTP username (full email address for Gmail).
        public string Username { get; set; } = string.Empty;

        // SMTP password or app-specific password. Should be supplied via env var SMTP_PASSWORD.
        public string Password { get; set; } = string.Empty;

        // From-address shown to recipients. For Gmail this must equal Username (or an alias on it),
        // otherwise the SMTP server rejects the message with 5.7.0 "From address must match
        // authenticated user".
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "Momentum";
    }
}
