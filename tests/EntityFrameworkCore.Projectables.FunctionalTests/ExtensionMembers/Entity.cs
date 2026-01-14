#if NET10_0_OR_GREATER
namespace EntityFrameworkCore.Projectables.FunctionalTests.ExtensionMembers
{
    public class Entity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }    
}
#endif
