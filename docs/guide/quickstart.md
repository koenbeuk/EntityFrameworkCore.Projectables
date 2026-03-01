# Quick Start

This guide walks you through a complete end-to-end example — from defining entities with projectable members to seeing the generated SQL.

## Step 1 — Define Your Entities

```csharp
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
    public ICollection<Order> Orders { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedDate { get; set; }
    public decimal TaxRate { get; set; }

    public User User { get; set; }
    public ICollection<OrderItem> Items { get; set; }

    // Mark computed properties with [Projectable]
    [Projectable] public decimal Subtotal => Items.Sum(item => item.Product.ListPrice * item.Quantity);
    [Projectable] public decimal Tax => Subtotal * TaxRate;
    [Projectable] public decimal GrandTotal => Subtotal + Tax;
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int Quantity { get; set; }
    public Product Product { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public decimal ListPrice { get; set; }
}
```

## Step 2 — Enable Projectables on Your DbContext

```csharp
public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Order> Orders { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseSqlServer("your-connection-string")
            .UseProjectables();  // Enable the runtime interceptor
    }
}
```

## Step 3 — Use Projectable Members in Queries

Now you can use `GrandTotal`, `Subtotal`, and `Tax` **directly in any LINQ query**:

```csharp
// In a Select projection
var orderSummaries = dbContext.Orders
    .Select(o => new {
        o.Id,
        o.Subtotal,
        o.Tax,
        o.GrandTotal
    })
    .ToList();

// In a Where clause
var highValueOrders = dbContext.Orders
    .Where(o => o.GrandTotal > 1000)
    .ToList();

// In an OrderBy
var sortedOrders = dbContext.Orders
    .OrderByDescending(o => o.GrandTotal)
    .ToList();
```

## Step 4 — Check the Generated SQL

Use `ToQueryString()` to inspect the SQL EF Core generates:

```csharp
var query = dbContext.Orders
    .Where(o => o.GrandTotal > 1000)
    .OrderByDescending(o => o.GrandTotal);

Console.WriteLine(query.ToQueryString());
```

The `GrandTotal` property — which itself uses `Subtotal` (which is also `[Projectable]`) — is fully inlined:

```sql
SELECT [o].[Id], [o].[UserId], [o].[CreatedDate], [o].[TaxRate]
FROM [Orders] AS [o]
WHERE (
    COALESCE(SUM([p].[ListPrice] * CAST([oi].[Quantity] AS decimal(18,2))), 0.0) +
    COALESCE(SUM([p].[ListPrice] * CAST([oi].[Quantity] AS decimal(18,2))), 0.0) * [o].[TaxRate]
) > 1000.0
ORDER BY (
    COALESCE(SUM([p].[ListPrice] * CAST([oi].[Quantity] AS decimal(18,2))), 0.0) +
    COALESCE(SUM([p].[ListPrice] * CAST([oi].[Quantity] AS decimal(18,2))), 0.0) * [o].[TaxRate]
) DESC
```

All computation happens in the database — no data is loaded into memory for filtering or sorting.

## Adding Extension Methods

You can also define projectable extension methods — useful for logic that doesn't belong on the entity itself:

```csharp
public static class UserExtensions
{
    [Projectable]
    public static Order GetMostRecentOrder(this User user, DateTime? cutoffDate = null) =>
        user.Orders
            .Where(x => cutoffDate == null || x.CreatedDate >= cutoffDate)
            .OrderByDescending(x => x.CreatedDate)
            .FirstOrDefault();
}
```

Use it in a query just like any regular method:

```csharp
var result = dbContext.Users
    .Where(u => u.UserName == "Jon")
    .Select(u => new {
        GrandTotal = u.GetMostRecentOrder(DateTime.UtcNow.AddDays(-30)).GrandTotal
    })
    .FirstOrDefault();
```

## Next Steps

- [Projectable Properties in depth →](/guide/projectable-properties)
- [Projectable Methods →](/guide/projectable-methods)
- [Extension Methods →](/guide/extension-methods)
- [Full [Projectable] attribute reference →](/reference/projectable-attribute)

