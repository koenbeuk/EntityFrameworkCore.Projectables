# EntityFrameworkCore.Projectables
Flexible projection magic for EF Core

[![NuGet version (EntityFrameworkCore.Projectables)](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
[![.NET](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml/badge.svg)](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml)

Write properties and methods once — use them anywhere in your LINQ queries, translated to efficient SQL automatically.

📖 **[Full documentation → projectables.github.io](https://projectables.github.io)**

## NuGet packages

| Package | |
|---|---|
| `EntityFrameworkCore.Projectables.Abstractions` | [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/) [![Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/) |
| `EntityFrameworkCore.Projectables` | [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/) [![Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/) |

> **EF Core compatibility:** v1.x → EF Core 3.1 / 5 · v2.x+ → EF Core 6+

## Quick start

```bash
dotnet add package EntityFrameworkCore.Projectables.Abstractions
dotnet add package EntityFrameworkCore.Projectables
```

Enable Projectables on your `DbContext`:

```csharp
options.UseSqlServer(connectionString)
       .UseProjectables();
```

Mark properties, methods or constructors with `[Projectable]`:

```csharp
class Order
{
    public decimal TaxRate { get; set; }
    public ICollection<OrderItem> Items { get; set; }

    [Projectable] public decimal Subtotal => Items.Sum(item => item.Product.ListPrice * item.Quantity);
    [Projectable] public decimal Tax => Subtotal * TaxRate;
    [Projectable] public decimal GrandTotal => Subtotal + Tax;
}

public static class UserExtensions
{
    [Projectable]
    public static Order GetMostRecentOrder(this User user, DateTime? cutoffDate) =>
        user.Orders
            .Where(x => cutoffDate == null || x.CreatedDate >= cutoffDate)
            .OrderByDescending(x => x.CreatedDate)
            .FirstOrDefault();
}
```

Use them anywhere in your queries — they are **inlined into SQL automatically**:

```csharp
var result = dbContext.Users
    .Where(x => x.UserName == "Jon")
    .Select(x => new {
        x.GetMostRecentOrder(DateTime.UtcNow.AddDays(-30)).GrandTotal
    })
    .FirstOrDefault();
```

No client-side evaluation. No duplicated expressions. Just clean, efficient SQL.

## Documentation

The full documentation is hosted at **[projectables.github.io](https://projectables.github.io)** and covers:

- [Getting Started](https://projectables.github.io/guide/introduction) — installation, quick start, core concepts
- [Reference](https://projectables.github.io/reference/projectable-attribute) — `[Projectable]` attribute options, compatibility mode, diagnostics
- [Advanced](https://projectables.github.io/advanced/how-it-works) — internals, query compiler pipeline, block-bodied members
- [Recipes](https://projectables.github.io/recipes/computed-properties) — computed properties, enum display names, reusable query filters

## License

MIT
