#nullable disable

namespace EntityFrameworkCore.Projectables.FunctionalTests.NullConditionals
{
    public static class EntityExtensions
    {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static string GetNameIgnoreNulls(this Entity entity)
            => entity?.Name;

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static int? GetNameLengthIgnoreNulls(this Entity entity)
            => entity.GetNameIgnoreNulls()?.Length;

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static Entity GetFirstRelatedIgnoreNulls(this Entity entity)
            => entity?.RelatedEntities?[0];

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static string GetNameRewriteNulls(this Entity entity)
            => entity?.Name;

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static int? GetNameLengthRewriteNulls(this Entity entity)
            => entity.GetNameIgnoreNulls()?.Length;

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static Entity GetFirstRelatedRewriteNulls(this Entity entity)
            => entity?.RelatedEntities?[0];
    }
}
