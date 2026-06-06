namespace AtomicHabits.Models.DTO
{
    public class TagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
    }

    public class TagUpsertDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
    }
}
