using System.Collections.Generic;

namespace EntityFrameworkCore.Projectables.FunctionalTests.NullConditionals
{
    public record Entity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<Entity>? RelatedEntities { get; set; }
    }
}
