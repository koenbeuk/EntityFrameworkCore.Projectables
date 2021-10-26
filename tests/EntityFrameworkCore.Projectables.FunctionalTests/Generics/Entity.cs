namespace EntityFrameworkCore.Projectables.FunctionalTests.Generics
{
    public record Entity : IEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
