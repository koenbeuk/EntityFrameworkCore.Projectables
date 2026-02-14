# Block-Bodied Methods Support

As of this version, EntityFrameworkCore.Projectables now supports "classic" block-bodied methods decorated with `[Projectable]`, in addition to expression-bodied methods.

## What's Supported

Block-bodied methods can now be transformed into expression trees when they contain:

### 1. Simple Return Statements
```csharp
[Projectable]
public int GetConstant()
{
    return 42;
}
```

### 2. If-Else Statements (converted to ternary expressions)
```csharp
[Projectable]
public string GetCategory()
{
    if (Value > 100)
    {
        return "High";
    }
    else
    {
        return "Low";
    }
}
```

### 3. Nested If-Else Statements
```csharp
[Projectable]
public string GetLevel()
{
    if (Value > 100)
    {
        return "High";
    }
    else if (Value > 50)
    {
        return "Medium";
    }
    else
    {
        return "Low";
    }
}
```

### 4. Local Variable Declarations (inlined into the expression)
```csharp
[Projectable]
public int CalculateDouble()
{
    var doubled = Value * 2;
    return doubled + 5;
}
```

### 5. Switch Statements (converted to nested ternary expressions)
```csharp
[Projectable]
public string GetValueLabel()
{
    switch (Value)
    {
        case 1:
            return "One";
        case 2:
            return "Two";
        case 3:
            return "Three";
        default:
            return "Many";
    }
}
```

### 6. If Statements Without Else (uses default value)
```csharp
[Projectable]
public int? GetPremiumIfActive()
{
    if (IsActive)
    {
        return Value * 2;
    }
    // Implicitly returns null (default for int?)
}

// Or with explicit fallback:
[Projectable]
public string GetStatus()
{
    if (IsActive)
    {
        return "Active";
    }
    return "Inactive";  // Explicit fallback
}
```

## Limitations and Warnings

The source generator will produce **warning EFP0003** when it encounters unsupported statements in block-bodied methods:

### Unsupported Statements:
- While, for, foreach loops  
- Try-catch-finally blocks
- Throw statements
- New object instantiation in statement position

### Example of Unsupported Pattern:
```csharp
[Projectable]
public int GetValue()
{
    for (int i = 0; i < 10; i++)  // ❌ Loops not supported
    {
        // ...
    }
    return 0;
}
```

Supported patterns:
```csharp
[Projectable]
public int GetValue()
{
    if (IsActive)  // ✅ If without else is now supported!
    {
        return Value;
    }
    else
    {
        return 0;
    }
}
```

Additional supported patterns:
```csharp
// If without else using fallback return:
[Projectable]
public int GetValue()
{
    if (IsActive)
    {
        return Value;
    }
    return 0;  // ✅ Fallback return
}

// Switch statement:
[Projectable]
public string GetLabel()
{
    switch (Value)  // ✅ Switch statements now supported!
    {
        case 1:
            return "One";
        case 2:
            return "Two";
        default:
            return "Other";
    }
}
```

Or as expression-bodied:
```csharp
[Projectable]
public int GetValue() => IsActive ? Value : 0;  // ✅ Expression-bodied
```

## How It Works

The source generator:
1. Parses block-bodied methods
2. Converts if-else statements to conditional (ternary) expressions
3. Converts switch statements to nested conditional expressions
4. Inlines local variables into the return expression
5. Rewrites the resulting expression using the existing expression transformation pipeline
6. Generates the same output as expression-bodied methods

## Benefits

- **More readable code**: Complex logic with nested conditions and switch statements is often easier to read than nested ternary operators
- **Gradual migration**: Existing code with block bodies can now be marked as `[Projectable]` without rewriting
- **Intermediate variables**: Local variables can make complex calculations more understandable
- **Switch support**: Traditional switch statements now work alongside switch expressions

## SQL Output Examples

### Switch Statement with Multiple Cases
Given this code:
```csharp
switch (Value)
{
    case 1:
    case 2:
        return "Low";
    case 3:
    case 4:
    case 5:
        return "Medium";
    default:
        return "High";
}
```

Generates optimized SQL:
```sql
SELECT CASE
    WHEN [e].[Value] IN (1, 2) THEN N'Low'
    WHEN [e].[Value] IN (3, 4, 5) THEN N'Medium'
    ELSE N'High'
END
FROM [Entity] AS [e]
```

### If-Else Example Output

Given this code:
```csharp
public record Entity
{
    public int Value { get; set; }
    public bool IsActive { get; set; }

    [Projectable]
    public int GetAdjustedValue()
    {
        if (IsActive && Value > 0)
        {
            return Value * 2;
        }
        else
        {
            return 0;
        }
    }
}
```

The generated SQL will be:
```sql
SELECT CASE
    WHEN [e].[IsActive] = CAST(1 AS bit) AND [e].[Value] > 0 
    THEN [e].[Value] * 2
    ELSE 0
END
FROM [Entity] AS [e]
```
