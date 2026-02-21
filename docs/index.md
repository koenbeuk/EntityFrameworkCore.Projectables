---
layout: home

hero:
  name: "EF Core Projectables"
  text: "Flexible projection magic for EF Core"
  tagline: Write properties and methods once — use them anywhere in your LINQ queries, translated to efficient SQL automatically.
  actions:
    - theme: brand
      text: Get Started
      link: /guide/introduction
    - theme: alt
      text: Quick Start
      link: /guide/quickstart
    - theme: alt
      text: View on GitHub
      link: https://github.com/koenbeuk/EntityFrameworkCore.Projectables

features:
  - icon: 🏷️
    title: Just Add [Projectable]
    details: Decorate any property or method with [Projectable] and the source generator does the rest — no boilerplate, no manual expression trees.

  - icon: 🔌
    title: Works with Any EF Core Provider
    details: Provider-agnostic. SQL Server, PostgreSQL, SQLite, Cosmos DB — Projectables hooks into the EF Core query pipeline regardless of your database.

  - icon: ⚡
    title: Performance-First Design
    details: Limited compatibility mode expands and caches queries after their first execution. Subsequent calls skip the expansion step entirely, often outperforming native EF Core.

  - icon: 🔗
    title: Composable by Design
    details: Projectable members can call other projectable members. Build a library of reusable query fragments and compose them freely in any query.

  - icon: 🛡️
    title: Null-Conditional Rewriting
    details: Working with nullable navigation properties? Configure NullConditionalRewriteSupport to automatically handle the ?. operator in generated expressions.

  - icon: 🔢
    title: Enum Method Expansion
    details: Use ExpandEnumMethods to translate enum extension methods (like display names from [Display] attributes) into SQL CASE expressions automatically.
---

## At a Glance

```csharp
class Order {
    public decimal TaxRate { get; set; }
    public ICollection<OrderItem> Items { get; set; }

    [Projectable] public decimal Subtotal => Items.Sum(item => item.Product.ListPrice * item.Quantity);
    [Projectable] public decimal Tax => Subtotal * TaxRate;
    [Projectable] public decimal GrandTotal => Subtotal + Tax;
}

// Use it anywhere in your queries — translated to SQL automatically
var result = dbContext.Users
    .Where(u => u.UserName == "Jon")
    .Select(u => new { u.GetMostRecentOrder().GrandTotal })
    .FirstOrDefault();
```

The properties are **inlined into the SQL** — no client-side evaluation, no N+1.

## NuGet Packages

| Package | Description |
|---|---|
| [`EntityFrameworkCore.Projectables.Abstractions`](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/) | The `[Projectable]` attribute and source generator |
| [`EntityFrameworkCore.Projectables`](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/) | The EF Core runtime extension |

