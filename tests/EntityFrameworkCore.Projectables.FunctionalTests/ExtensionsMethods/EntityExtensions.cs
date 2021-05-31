namespace EntityFrameworkCore.Projectables.FunctionalTests.ExtensionMethods
{
    public static class EntityExtensions
    {
        [Projectable]
        public static int Squared(this int i) => i * i;

        [Projectable]
        public static int Foo(this Entity entity) => entity.Id + 1;
    }
}
