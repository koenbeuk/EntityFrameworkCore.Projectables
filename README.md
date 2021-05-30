> DISCLAIMER: This project and this readme in particular are still a WIP. Expect a first version coming coon.

> DISCLAIMER: The final name of this project has not yet been settled upon. EntityFrameworkCore.Projections is quite non descriptive as projections are a core concept of EFCore already. Feel free to open up an issue suggesting a better name

# EntitiyFrameworkCore.Projections
Flexible projection magic for EFCore

[![NuGet version (EntityFrameworkCore.Projections)](https://img.shields.io/nuget/v/EntityFrameworkCore.Projections.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projections.Abstractions/)

## NuGet packages
- EntityFrameworkCore.Projections.Abstractions [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projections.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projections.Abstractions/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projections.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projections.Abstractions/)
- EntityFrameworkCore.Projections [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projections.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projections/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projections.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projections/)

## Getting started
TODO

### Example
Assuming this sample:

```csharp
class Order {
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedDate { get; set; }

    public decimal TaxRate { get; set; }
    
    public User User { get; set; } 
    public ICollection<OrderItem> Items { get; set; }

    [Projectable] public decimal Subtotal => Items.Sum(item => item.Product.ListPrice * item.Quantity);
    [Projectable] public decimal Tax => Subtotal * TaxRate;
    [Projectable] public decimal GrandTotal => Subtotal + Tax;
}

public static class UserExtensions {
    [Projectable]
    public static Order GetMostRecentOrderForUser(this User user, DateTime? cutoffDate) => 
        user.Orders
            .Where(x => cutoffDate == null || x.CreatedDate >= cutoffDate)
            .OrderByDescending(x => x.CreatedDate)
            .FirstOrDefault();
}

var result = _dbContext.Users
    .Where(x => x.UserName == "Jon")
    .Select(x => new {
        x.GetMostRecentOrderForUser(DateTime.UtcNow.AddDays(-30)).GrandTotal
    });
    .FirstOrDefault();
```

The following query gets generated (assuming SQL Server as a database provider)
```sql
SELECT (
    SELECT COALESCE(SUM([p].[ListPrice] * CAST([o].[Quantity] AS decimal(18,2))), 0.0)
    FROM [OrderItem] AS [o]
    INNER JOIN [Products] AS [p] ON [o].[ProductId] = [p].[Id]
    WHERE (
        SELECT TOP(1) [o0].[Id]
        FROM [Orders] AS [o0]
        WHERE ([u].[Id] = [o0].[UserId]) AND ([o0].[CreatedDate] >= DATEADD(day, CAST(-30.0E0 AS int), GETUTCDATE()))
        ORDER BY [o0].[CreatedDate] DESC) IS NOT NULL AND ((
        SELECT TOP(1) [o1].[Id]
        FROM [Orders] AS [o1]
        WHERE ([u].[Id] = [o1].[UserId]) AND ([o1].[CreatedDate] >= DATEADD(day, CAST(-30.0E0 AS int), GETUTCDATE()))
        ORDER BY [o1].[CreatedDate] DESC) = [o].[OrderId])) + ((
    SELECT COALESCE(SUM([p0].[ListPrice] * CAST([o2].[Quantity] AS decimal(18,2))), 0.0)
    FROM [OrderItem] AS [o2]
    INNER JOIN [Products] AS [p0] ON [o2].[ProductId] = [p0].[Id]
    WHERE (
        SELECT TOP(1) [o3].[Id]
        FROM [Orders] AS [o3]
        WHERE ([u].[Id] = [o3].[UserId]) AND ([o3].[CreatedDate] >= DATEADD(day, CAST(-30.0E0 AS int), GETUTCDATE()))
        ORDER BY [o3].[CreatedDate] DESC) IS NOT NULL AND ((
        SELECT TOP(1) [o4].[Id]
        FROM [Orders] AS [o4]
        WHERE ([u].[Id] = [o4].[UserId]) AND ([o4].[CreatedDate] >= DATEADD(day, CAST(-30.0E0 AS int), GETUTCDATE()))
        ORDER BY [o4].[CreatedDate] DESC) = [o2].[OrderId])) * (
    SELECT TOP(1) [o5].[TaxRate]
    FROM [Orders] AS [o5]
    WHERE ([u].[Id] = [o5].[UserId]) AND ([o5].[CreatedDate] >= DATEADD(day, CAST(-30.0E0 AS int), GETUTCDATE()))
    ORDER BY [o5].[CreatedDate] DESC)) AS [GrandTotal]
FROM [Users] AS [u]
WHERE [u].[UserName] = N'Jon'
```

Projectable properties and methods have been inlined! the generated SQL could be improved but this is what EFCore (v5) gives us.

### How it works
Essentially there are 2 components: We have a source generator that is able to write companion Expression for properties and methods marked with the `Projectable` attribute. We then have a runtime component that intercepts any query and translates any call to a property or method marked with the `Projectable` attribute and translates the query to use the generated Expression instead.

### FAQ

#### Are there currently any known limitations?
There is currently no support for overloaded methods. Each method name needs to be unique within a given type. This is something that will be fixed before a proper v1 release.

#### Is this specific to a database provider?
No; The runtime component injects itself within the EFCore query compilation pipeline and thus has no impact on the database provider used. Of course you're still limited to whatever your database provider can do.

#### Are there performance implications that I should be aware of?
Yes and no; using EntityFrameworkCore.Projections does not add any measerable overhead on top of normal use of EFCore (Expect a proper benchmark soon...) however it does make it easier to write more complex queries. As such: Always consider the generated SQL and ensure that it's performance implications are acceptable.

#### Can I call additional properties and methods from my Projectable properties and methods?
Yes you can! Any projectable property/method can call into other properties and methods as long as those properties/methods are native to EFCore or as long as they are marked with a `Projectable` attribute.

#### Can I use projectable extensions methods on non-entity types?
Yes you can. It's perfectly acceptable to have the following code:
```csharp
[Projectable]
public static int Squared(this int i) => i * i;
```
Any call to squared given any int will perfertly translate to SQL.

#### How does this relate to [Expressionify](https://github.com/ClaveConsulting/Expressionify)?
Expressionify is a project that was launched before this project. It has some overlapping features and uses similar approaches. When I first published this project, I was not aware of its existance so shame on me. Currently Expressionify targets a more focusses scope of what this project is doing and thereby it seems to be more limiting in its capabilities. Check them out though!

#### How does this relate to LinqKit/LinqExpander/...?
There are a few projects like [LinqKit](https://github.com/scottksmith95/LINQKit) that were created before we had code generators in dotnet. These are great options if you're stuck with classical EF or don't want to rely on code generation. Otherwise I would suggest that EntityFrameworkCore.Projections and Expresssionify are superior approaches as they are able to rely on SourceGenerators to do most of the hard work.

#### Is the available for EFCore 3.1, 5 and 6?
Yes it is! there is no difference between any of these versions and you can upgrade/downgrade whenever you want.

#### What is next for this project?
TBD... However one thing I'd like to improve is our Expression generation logic as its currently making a few assumptions (have yet to experience it breaking). Community contributions are very welcome!
