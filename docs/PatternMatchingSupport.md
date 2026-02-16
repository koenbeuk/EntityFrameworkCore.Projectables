# Pattern Matching Support in Block-Bodied Methods

This document describes how pattern matching is handled in block-bodied projectable methods.

## Overview

C# pattern matching (the `is` operator with patterns) is not supported in expression trees and will cause a CS8122 compilation error. The source generator automatically converts pattern matching syntax into equivalent boolean expressions that work in expression trees.

## Supported Pattern Types

### 1. Recursive Patterns (Property Patterns)

**Syntax:**
```csharp
[Projectable]
public static string GetCategory(this Entity entity)
{
    if (entity is { IsActive: true, Value: > 100 })
    {
        return "Active High";
    }
    return "Other";
}
```

**Converted To:**
```csharp
entity != null && entity.IsActive == true && entity.Value > 100 ? "Active High" : "Other"
```

The pattern is converted to:
1. Null check: `entity != null`
2. Property checks: `entity.IsActive == true && entity.Value > 100`
3. Combined with logical AND

### 2. Relational Patterns

**Syntax:**
```csharp
[Projectable]
public static string GetCategory(this Entity entity)
{
    if (entity.Value is > 100)
    {
        return "High";
    }
    return "Low";
}
```

**Converted To:**
```csharp
entity.Value > 100 ? "High" : "Low"
```

Supported relational operators:
- `>` (greater than)
- `>=` (greater than or equal)
- `<` (less than)
- `<=` (less than or equal)

### 3. Constant Patterns

**Syntax:**
```csharp
[Projectable]
public static bool IsNull(this Entity entity)
{
    if (entity is null)
    {
        return true;
    }
    return false;
}
```

**Converted To:**
```csharp
entity == null ? true : false
```

### 4. Unary Patterns (Not Patterns)

**Syntax:**
```csharp
[Projectable]
public static bool IsNotNull(this Entity entity)
{
    if (entity is not null)
    {
        return true;
    }
    return false;
}
```

**Converted To:**
```csharp
!(entity == null) ? true : false
```

### 5. Binary Patterns (And/Or)

**Syntax:**
```csharp
[Projectable]
public static bool IsInRange(this Entity entity)
{
    if (entity.Value is > 10 and < 100)
    {
        return true;
    }
    return false;
}
```

**Converted To:**
```csharp
entity.Value > 10 && entity.Value < 100 ? true : false
```

## Benefits

1. **Modern C# Syntax**: Use pattern matching in block-bodied methods just like regular C# code
2. **Automatic Conversion**: No manual rewriting needed - the generator handles it
3. **Expression Tree Compatibility**: Generated code compiles without CS8122 errors
4. **Semantic Equivalence**: Converted expressions maintain the same behavior as patterns

## Limitations

Not all pattern types are currently supported:
- Type patterns with variable declarations may have limited support
- List patterns are not yet supported
- Some complex nested patterns may not be supported

If you encounter an unsupported pattern, you'll receive an error message indicating which pattern type is not supported.

## Examples

### Complex Property Pattern
```csharp
[Projectable]
public static string GetStatus(this Order order)
{
    if (order is { Status: "Completed", Amount: > 1000, Customer.IsVip: true })
    {
        return "VIP High Value Completed";
    }
    return "Other";
}
```

**Generates:**
```csharp
order != null && 
order.Status == "Completed" && 
order.Amount > 1000 && 
order.Customer.IsVip == true 
    ? "VIP High Value Completed" 
    : "Other"
```

### Range Check with Relational Patterns
```csharp
[Projectable]
public static string GetRange(this Entity entity)
{
    if (entity.Value is >= 0 and < 50)
    {
        return "Low";
    }
    else if (entity.Value is >= 50 and < 100)
    {
        return "Medium";
    }
    return "High";
}
```

**Generates:**
```csharp
entity.Value >= 0 && entity.Value < 50 ? "Low" :
entity.Value >= 50 && entity.Value < 100 ? "Medium" :
"High"
```

## Technical Details

The conversion is implemented in `ExpressionSyntaxRewriter.VisitIsPatternExpression` which:
1. Visits the expression being tested
2. Converts the pattern to an equivalent expression using `ConvertPatternToExpression`
3. Handles nested patterns recursively
4. Combines multiple property checks with logical AND operators

This ensures that all pattern matching is transformed into expression tree-compatible code before code generation.
