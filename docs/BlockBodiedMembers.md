# Block-Bodied Members Support

EntityFrameworkCore.Projectables now supports "classic" block-bodied members (methods and properties) decorated with `[Projectable]`, in addition to expression-bodied members.

## ⚠️ Experimental Feature

Block-bodied members support is currently **experimental**. By default, using a block-bodied member with `[Projectable]` will emit a warning:

```
EFP0001: Block-bodied member 'MethodName' is using an experimental feature. Set AllowBlockBody = true on the Projectable attribute to suppress this warning.
```

To acknowledge that you're using an experimental feature and suppress the warning, set `AllowBlockBody = true`:

```csharp
[Projectable(AllowBlockBody = true)]
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

This requirement will be removed in a future version once the feature is considered stable.

## What's Supported

Block-bodied members can now be transformed into expression trees when they contain:

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

// Transitive inlining is also supported:
[Projectable]
public int CalculateComplex()
{
    var a = Value * 2;
    var b = a + 5;
    return b + 10;  // Fully expanded to: Value * 2 + 5 + 10
}
```

**⚠️ Important Notes:**
- Local variables are inlined at each usage point, which duplicates the initializer expression
- If a local variable is used multiple times, its initializer expression is duplicated at each usage, which can change semantics if the initializer has side effects
- Local variables can only be declared at the method body level, not inside nested blocks (if/switch/etc.)
- Variables are fully expanded transitively (variables that reference other variables are fully inlined)

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
// Pattern 1: Explicit null return
[Projectable]
public int? GetPremiumIfActive()
{
    if (IsActive)
    {
        return Value * 2;
    }
    return null;  // Explicit return for all code paths
}

// Pattern 2: Explicit fallback return
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

### 7. Multiple Early Returns (converted to nested ternary expressions)
```csharp
[Projectable]
public string GetValueCategory()
{
    if (Value > 100)
    {
        return "Very High";
    }

    if (Value > 50)
    {
        return "High";
    }

    if (Value > 10)
    {
        return "Medium";
    }

    return "Low";
}

// Converted to: Value > 100 ? "Very High" : (Value > 50 ? "High" : (Value > 10 ? "Medium" : "Low"))
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

## Side Effect Detection

The generator provides specific error reporting for side effects in block-bodied methods, helping you identify and fix issues quickly.

### Detected Side Effects

#### 1. Property Assignments (EFP0004 - Error)

Property assignments modify state and are not allowed:

```csharp
[Projectable]
public int Foo()
{
    Bar = 10;  // ❌ Error: Assignment operation has side effects
    return Bar;
}
```

#### 2. Compound Assignments (EFP0004 - Error)

Compound assignment operators like `+=`, `-=`, `*=`, etc. are not allowed:

```csharp
[Projectable]
public int Foo()
{
    Bar += 10;  // ❌ Error: Compound assignment operator '+=' has side effects
    return Bar;
}
```

#### 3. Increment/Decrement Operators (EFP0004 - Error)

Pre and post increment/decrement operators are not allowed:

```csharp
[Projectable]
public int Foo()
{
    var x = 5;
    x++;  // ❌ Error: Increment/decrement operator '++' has side effects
    return x;
}
```

#### 4. Non-Projectable Method Calls (EFP0005 - Warning)

Calls to methods not marked with `[Projectable]` may have side effects:

```csharp
[Projectable]
public int Foo()
{
    Console.WriteLine("test");  // ⚠️ Warning: Method call 'WriteLine' may have side effects
    return Bar;
}
```

### Diagnostic Codes

- **EFP0003**: Unsupported statement in block-bodied method (Warning)
- **EFP0004**: Statement with side effects in block-bodied method (Error)
- **EFP0005**: Potential side effect in block-bodied method (Warning)

### Error Message Improvements

Instead of generic error messages, you now get precise, actionable feedback:

**Before:**
```
warning EFP0003: Method 'Foo' contains an unsupported statement: Expression statements are not supported
```

**After:**
```
error EFP0004: Property assignment 'Bar' has side effects and cannot be used in projectable methods
```

The error message points to the exact line with the problematic code, making it much easier to identify and fix issues.

