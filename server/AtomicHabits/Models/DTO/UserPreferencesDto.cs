namespace AtomicHabits.Models.DTO
{
    public class UserPreferencesDto
    {
        public bool Notifications { get; set; } = true;
        public bool DarkMode { get; set; } = false;
        public bool EmailUpdates { get; set; } = true;
        public bool DeviceSync { get; set; } = true;

        public string PrimaryColor { get; set; } = "#2196f3";
        public string FontFamily { get; set; } = "Inter";
        public int BorderRadius { get; set; } = 8;
        public int Spacing { get; set; } = 8;
    }
}
