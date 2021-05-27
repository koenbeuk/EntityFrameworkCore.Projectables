namespace EntityFrameworkCore.Projections.FunctionalTests.ExtensionMethods
{
    public static class EntityExtensions
    {
        [Projectable]
        public static int Foo(this ExtensionMethodTests.Entity entity) => entity.Id + 1;
    }
}
