namespace AtomicHabits.Models.DTO
{
    public class InsightDto
    {
        public string Key { get; set; } = string.Empty;   // stable id, e.g. "top-skip-reason"
        public string Text { get; set; } = string.Empty;   // plain-language sentence
    }
}
