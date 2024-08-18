using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    public class EnumerableProjectableTests
    {
        public class Product
        {
            public int Id { get; set; }

            public List<ProductPrice> Prices { get; } = [];

            [Projectable]
            public IEnumerable<ProductPrice> CheapPrices => Prices.Where(x => x.Price < 10D);
        }

        public class ProductPrice
        {
            public int Id { get; set; }

            public double Price { get; set; }
        }

        [Fact]
        public void ProjectableProperty_IsIgnoredFromMapping()
        {
            var dbContext = new SampleDbContext<Product>();
            var productPriceType = dbContext.Model.GetEntityTypes().Single(x => x.ClrType == typeof(ProductPrice));

            // Assert 3 properties: Id, Price, ProductId (synthetic)
            Assert.Equal(3, productPriceType.GetProperties().Count());  
        }
    }
}
