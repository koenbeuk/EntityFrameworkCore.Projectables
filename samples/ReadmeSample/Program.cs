using ReadmeSample;
using ReadmeSample.Entities;
using ReadmeSample.Extensions;

using var dbContext = new ApplicationDbContext();

// recreate database
dbContext.Database.EnsureDeleted();
dbContext.Database.EnsureCreated();

// Populate with seed data
var sampleUser = new User { UserName = "Jon", EmailAddress = "jon@doe.com" };
var sampleProduct = new Product { Name = "Blue Pen", ListPrice = 1.5m };
var sampleOrder = new Order {
    User = sampleUser,
    TaxRate = .19m,
    CreatedDate = DateTime.UtcNow.AddDays(-1),
    FulfilledDate = DateTime.UtcNow,
    Items = new List<OrderItem> {
        new OrderItem { Product = sampleProduct, Quantity = 5  }
    }
};

dbContext.AddRange(sampleUser, sampleProduct, sampleOrder);
dbContext.SaveChanges();

var query = dbContext.Users
    .Where(x => x.UserName == sampleUser.UserName)
    .Select(x => new {
        GrandTotal = x.GetMostRecentOrderForUser(/* includeUnfulfilled: */ false).GrandTotal
    });

var result = query.First();

Console.WriteLine($"Jons latest order had a grant total of {result.GrandTotal}");
