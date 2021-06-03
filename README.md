> DISCLAIMER: This project and this readme in particular are still a WIP. Expect a first version coming coon.

# EntitiyFrameworkCore.Projectables
Flexible projection magic for EFCore

[![NuGet version (EntityFrameworkCore.Projectables)](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
[![.NET](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml/badge.svg)](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml)

## NuGet packages
- EntityFrameworkCore.Projectables.Abstractions [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
- EntityFrameworkCore.Projectables [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/)

## Getting started
1. Install the package from [NuGet](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/)
2. Enable Projectables in your DbContext by calling: `dbContextOptions.UseProjectables()`
3. Implement projectable properties and methods and mark them with the [Projectable] attribute.
4. View our [samples](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/tree/master/samples) and checkout our [Blog Post](https://onthedrift.com/posts/efcore-projectables/)

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
DECLARE @__sampleUser_UserName_0 nvarchar(4000) = N'Jon';

SELECT (
    SELECT COALESCE(SUM([p].[ListPrice] * CAST([o].[Quantity] AS decimal(18,2))), 0.0)
    FROM [OrderItem] AS [o]
    INNER JOIN [Products] AS [p] ON [o].[ProductId] = [p].[Id]
    WHERE (
        SELECT TOP(1) [o0].[Id]
        FROM [Orders] AS [o0]
        WHERE ([u].[Id] = [o0].[UserId]) AND [o0].[FulfilledDate] IS NOT NULL
        ORDER BY [o0].[CreatedDate] DESC) IS NOT NULL AND ((
        SELECT TOP(1) [o1].[Id]
        FROM [Orders] AS [o1]
        WHERE ([u].[Id] = [o1].[UserId]) AND [o1].[FulfilledDate] IS NOT NULL
        ORDER BY [o1].[CreatedDate] DESC) = [o].[OrderId])) * (
    SELECT TOP(1) [o2].[TaxRate]
    FROM [Orders] AS [o2]
    WHERE ([u].[Id] = [o2].[UserId]) AND [o2].[FulfilledDate] IS NOT NULL
    ORDER BY [o2].[CreatedDate] DESC) AS [GrandTotal]
FROM [Users] AS [u]
WHERE [u].[UserName] = @__sampleUser_UserName_0
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
There are 2 compatibility modes: Limited and Full. Most of the time, limited compatibility mode is sufficient however if you are running into issues with failed query compilation, then you may want to try Full compatibility mode. With Full compatibility mode, Each Query will first be expandend (Any calls to Projectable properties and methods will be replaced by their respsective Expresions) before being handed off to EFCore. (This is similar to how LinqKit/LinqExpander/Expressionify works). Because of this additional step, there is a small performance impact. Limited compatibility mode is smart about things and only expands the Query after it has been accepted by EF. The expanded query will then be stored in the Query Cache. With Limited compatibility you will likely see increased performance over EFCore without projectables. I have a planned post coming up talking about why that is but for now you can see it for yourself by running the included Benchmark.

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
There are a few projects like [LinqKit](https://github.com/scottksmith95/LINQKit) that were created before we had code generators in dotnet. These are great options if you're stuck with classical EF or don't want to rely on code generation. Otherwise I would suggest that EntityFrameworkCore.Projectables and Expresssionify are superior approaches as they are able to rely on SourceGenerators to do most of the hard work.

#### Is the available for EFCore 3.1, 5 and 6?
Yes it is! there is no difference between any of these versions and you can upgrade/downgrade whenever you want.

#### What is next for this project?
TBD... However one thing I'd like to improve is our Expression generation logic as its currently making a few assumptions (have yet to experience it breaking). Community contributions are very welcome!
