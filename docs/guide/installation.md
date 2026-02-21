# Installation

Projectables is split into two NuGet packages. You will typically need both.

## Packages

### `EntityFrameworkCore.Projectables.Abstractions`

Contains the `[Projectable]` attribute and the Roslyn **source generator**. This package must be referenced by the project that **defines** your entities and projectable members.

### `EntityFrameworkCore.Projectables`

Contains the EF Core **runtime extension** that intercepts queries and expands projectable members into SQL. This package must be referenced by the project that configures your `DbContext`.

In most single-project setups, you reference both packages in the same project.

## Install via .NET CLI

```bash
dotnet add package EntityFrameworkCore.Projectables.Abstractions
dotnet add package EntityFrameworkCore.Projectables
```

## Install via Package Manager Console

```powershell
Install-Package EntityFrameworkCore.Projectables.Abstractions
Install-Package EntityFrameworkCore.Projectables
```

## Install via PackageReference (csproj)

```xml
<ItemGroup>
  <PackageReference Include="EntityFrameworkCore.Projectables.Abstractions" Version="*" />
  <PackageReference Include="EntityFrameworkCore.Projectables" Version="*" />
</ItemGroup>
```

> **Tip:** Replace `*` with the latest stable version from [NuGet](https://www.nuget.org/packages/EntityFrameworkCore.Projectables/).

## Enable in Your DbContext

After installing the packages, call `UseProjectables()` when configuring your `DbContextOptions`:

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)
           .UseProjectables());  // 👈 Add this
```

Or in `OnConfiguring`:

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder
        .UseSqlServer(connectionString)
        .UseProjectables();
}
```

That's it — you're ready to start using `[Projectable]`!

## Optional Configuration

`UseProjectables()` accepts an optional callback to configure advanced options:

```csharp
options.UseProjectables(projectables =>
    projectables.CompatibilityMode(CompatibilityMode.Limited));
```

See [Compatibility Mode](/reference/compatibility-mode) for details.

## Verifying the Installation

The source generator runs at compile time. You can verify it is working by:

1. Adding `[Projectable]` to a property in your entity class.
2. Building the project — no errors should appear.
3. Using the property in a LINQ query and checking that the generated SQL reflects the inlined logic (e.g., via `ToQueryString()` or EF Core logging).

## Multi-Project Solutions

In solutions where entities are in a separate class library:

```
MyApp.Domain     → references Abstractions (has [Projectable] attributes)
MyApp.Data       → references Projectables runtime + Domain
MyApp.Web        → references Data
```

```xml
<!-- MyApp.Domain.csproj -->
<PackageReference Include="EntityFrameworkCore.Projectables.Abstractions" Version="*" />

<!-- MyApp.Data.csproj -->
<PackageReference Include="EntityFrameworkCore.Projectables" Version="*" />
<ProjectReference Include="..\MyApp.Domain\MyApp.Domain.csproj" />
```

