# EntitiyFrameworkCore.Projectables
Flexible projection magic for EFCore

[![NuGet version (EntityFrameworkCore.Projectables)](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
[![.NET](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml/badge.svg)](https://github.com/koenbeuk/EntityFrameworkCore.Projectables/actions/workflows/build.yml)

## NuGet packages
- EntityFrameworkCore.Projectables.Abstractions [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.Abstractions.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables.Abstractions/)
- EntityFrameworkCore.Projectables [![NuGet version](https://img.shields.io/nuget/v/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/) [![NuGet](https://img.shields.io/nuget/dt/EntityFrameworkCore.Projectables.svg?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/)

> Starting with V2 of this project (currently available as a beta release on NuGet) we're binding against **EF Core 6**. If you're targeting **EF Core 5** or **EF Core 3.1** then you can use the latest v1 release. These are functionally equivalent.


## Getting started
1. Install the package from [NuGet](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/)
2. Enable Projectables in your DbContext by calling: `dbContextOptions.UseProjectables()`
3. Implement projectable properties and methods and mark them with the `[Projectable]` attribute.
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
There is currently no support for overloaded methods. Each method name needs to be unique within a given type.

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

#### How do I deal with nullable properties
Expressions and Lamdas are different and not equal. Expressions can only express a subset of valid CSharp statements that are allowed in lambda's and arrow functions. One obvious limitation is the null-conditional operator. Consider the following example:
```csharp
[Projectable]
public static string? GetFullAddress(this User? user) => user?.Location?.AddressLine1 + " " + user?.Location.AddressLine2;
```
This is a perfectly valid arrow function but it can't be translated directly to an expression tree. This Project will generate an error by default and suggest 2 solutions: Either you rewrite the function to excplitly check for nullables or you let the generator do that for you!

Starting from the official release of V2, we can now hint the generator in how to translate this arrow function to an expression tree. We can say:
```csharp
[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
``` 
which will simply generate an expression tree that ignores the null-conditional operator. This generates: 
```csharp
user.Location.AddressLine1 + " " + user.Location.AddressLine2
```
This is perfect for a database like SQL Server where nullability is implicit and if any of the arguments were to be null, the resulting value will be null. If you are dealing with CosmosDB (which may result to client-side evaluation) or want to be explicit about things. You can configure your projectable as such: 
```csharp
[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
```
This will rewrite your expression to explicitly check for nullables. In the former example, this will be rewritten to: 
```csharp 
(user != null ? user.Location != nulll ? user.Location?.AddressLine 1 + (user != null ? user.Location != null ? user.Location.AddressLine2 : null) : null)
```
Note that using rewrite (not ignore) may increase the actual SQL query complexity being generated with some database providers such as SQLServer

#### How does this relate to [Expressionify](https://github.com/ClaveConsulting/Expressionify)?
Expressionify is a project that was launched before this project. It has some overlapping features and uses similar approaches. When I first published this project, I was not aware of its existance so shame on me. Currently Expressionify targets a more focusses scope of what this project is doing and thereby it seems to be more limiting in its capabilities. Check them out though!

#### How does this relate to LinqKit/LinqExpander/...?
There are a few projects like [LinqKit](https://github.com/scottksmith95/LINQKit) that were created before we had code generators in dotnet. These are great options if you're stuck with classical EF or don't want to rely on code generation. Otherwise I would suggest that EntityFrameworkCore.Projectables and Expresssionify are superior approaches as they are able to rely on SourceGenerators to do most of the hard work.

#### Is the available for EFCore 3.1, 5 and 6?
Yes it is! there is no difference between any of these versions and you can upgrade/downgrade whenever you want.

#### What is next for this project?
TBD... However one thing I'd like to improve is our Expression generation logic as its currently making a few assumptions (have yet to experience it breaking). Community contributions are very welcome!
