using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    public static class ProjectableInterpreter
    {
        static IEnumerable<string> GetNestedInClassPath(ITypeSymbol namedTypeSymbol)
        {
            if (namedTypeSymbol.ContainingType is not null)
            {
                foreach (var nestedInClassName in GetNestedInClassPath(namedTypeSymbol.ContainingType))
                {
                    yield return nestedInClassName;
                }
            }

            yield return namedTypeSymbol.Name;
        }

        /// <summary>
        /// Gets the nested class path for extension members, skipping the extension block itself
        /// and using the outer class as the containing type.
        /// </summary>
        static IEnumerable<string> GetNestedInClassPathForExtensionMember(ITypeSymbol extensionType)
        {
            // For extension members, the ContainingType is the extension block,
            // and its ContainingType is the outer class (e.g., EntityExtensions)
            var outerType = extensionType.ContainingType;
            
            if (outerType is not null)
            {
                return GetNestedInClassPath(outerType);
            }
            
            return [];
        }

        public static ProjectableDescriptor? GetDescriptor(Compilation compilation, MemberDeclarationSyntax member, SourceProductionContext context)
        {
            var semanticModel = compilation.GetSemanticModel(member.SyntaxTree);
            var memberSymbol = semanticModel.GetDeclaredSymbol(member);

            if (memberSymbol is null)
            {
                return null;
            }

            var projectableAttributeTypeSymbol = compilation.GetTypeByMetadataName("EntityFrameworkCore.Projectables.ProjectableAttribute");

            var projectableAttributeClass = memberSymbol.GetAttributes()
                .Where(x => x.AttributeClass?.Name == "ProjectableAttribute")
                .FirstOrDefault();

            if (projectableAttributeClass is null || !SymbolEqualityComparer.Default.Equals(projectableAttributeClass.AttributeClass, projectableAttributeTypeSymbol))
            {
                return null;
            }

            var nullConditionalRewriteSupport = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "NullConditionalRewriteSupport")
                .Where(x => x.Value.Kind == TypedConstantKind.Enum)
                .Select(x => x.Value.Value)
                .Where(x => Enum.IsDefined(typeof(NullConditionalRewriteSupport), x))
                .Cast<NullConditionalRewriteSupport>()
                .FirstOrDefault();

            var useMemberBody = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "UseMemberBody")
                .Select(x => x.Value.Value)
                .OfType<string?>()
                .FirstOrDefault();

            var expandEnumMethods = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "ExpandEnumMethods")
                .Select(x => x.Value.Value is bool b && b)
                .FirstOrDefault();

            var allowBlockBody = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "AllowBlockBody")
                .Select(x => x.Value.Value is bool b && b)
                .FirstOrDefault();

            var memberBody = member;

            if (useMemberBody is not null)
            {
                var comparer = SymbolEqualityComparer.Default;

                memberBody = memberSymbol.ContainingType.GetMembers(useMemberBody)
                    .Where(x =>
                    {
                        if (memberSymbol is IMethodSymbol symbolMethod &&
                            x is IMethodSymbol xMethod &&
                            comparer.Equals(symbolMethod.ReturnType, xMethod.ReturnType) &&
                            symbolMethod.TypeArguments.Length == xMethod.TypeArguments.Length &&
                            !symbolMethod.TypeArguments.Zip(xMethod.TypeArguments, (a, b) => !comparer.Equals(a, b)).Any())
                        {
                            return true;
                        }
                        else if (memberSymbol is IPropertySymbol symbolProperty &&
                            x is IPropertySymbol xProperty &&
                            comparer.Equals(symbolProperty.Type, xProperty.Type))
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    })
                    .SelectMany(x => x.DeclaringSyntaxReferences)
                    .Select(x => x.GetSyntax())
                    .OfType<MemberDeclarationSyntax>()
                    .FirstOrDefault(x =>
                    {
                        if (x == null ||
                            x.SyntaxTree != member.SyntaxTree ||
                            x.Modifiers.Any(SyntaxKind.StaticKeyword) != member.Modifiers.Any(SyntaxKind.StaticKeyword))
                        {
                            return false;
                        }
                        else if (x is MethodDeclarationSyntax xMethod &&
                            (xMethod.ExpressionBody is not null || xMethod.Body is not null))
                        {
                            return true;
                        }
                        else if (x is PropertyDeclarationSyntax xProperty)
                        {
                            // Support expression-bodied properties: int Prop => value;
                            if (xProperty.ExpressionBody is not null)
                            {
                                return true;
                            }

                            // Support properties with explicit getters: int Prop { get => value; } or { get { return value; } }
                            if (xProperty.AccessorList is not null)
                            {
                                var getter = xProperty.AccessorList.Accessors
                                    .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                                if (getter?.ExpressionBody is not null || getter?.Body is not null)
                                {
                                    return true;
                                }
                            }
                        }
                        
                        return false;
                    });

                if (memberBody is null)
                {
                    return null;
                }
            }

            // Check if this member is inside a C# 14 extension block
            var isExtensionMember = memberSymbol.ContainingType is { IsExtension: true };
            IParameterSymbol? extensionParameter = null;
            ITypeSymbol? extensionReceiverType = null;
            
            if (isExtensionMember && memberSymbol.ContainingType is { } extensionType)
            {
                extensionParameter = extensionType.ExtensionParameter;
                extensionReceiverType = extensionParameter?.Type;
            }

            // For extension members, use the extension receiver type for rewriting
            var targetTypeForRewriting = isExtensionMember && extensionReceiverType is INamedTypeSymbol receiverNamedType
                ? receiverNamedType
                : memberSymbol.ContainingType;

            var expressionSyntaxRewriter = new ExpressionSyntaxRewriter(
                targetTypeForRewriting, 
                nullConditionalRewriteSupport, 
                expandEnumMethods,
                semanticModel, 
                context,
                extensionParameter?.Name);
            var declarationSyntaxRewriter = new DeclarationSyntaxRewriter(semanticModel);

            // For extension members, use the outer class for class naming
            var classForNaming = isExtensionMember && memberSymbol.ContainingType.ContainingType is not null
                ? memberSymbol.ContainingType.ContainingType
                : memberSymbol.ContainingType;

            var descriptor = new ProjectableDescriptor
            {
                UsingDirectives = member.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>(),                    
                ClassName = classForNaming.Name,
                ClassNamespace = classForNaming.ContainingNamespace.IsGlobalNamespace ? null : classForNaming.ContainingNamespace.ToDisplayString(),
                MemberName = memberSymbol.Name,
                NestedInClassNames = isExtensionMember 
                    ? GetNestedInClassPathForExtensionMember(memberSymbol.ContainingType)
                    : GetNestedInClassPath(memberSymbol.ContainingType),
                ParametersList = SyntaxFactory.ParameterList()
            };
            
            var methodSymbol = memberSymbol as IMethodSymbol;

            // Collect parameter type names for method overload disambiguation
            if (methodSymbol is not null)
            {
                var parameterTypeNames = methodSymbol.Parameters
                    .Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .ToList();
                
                // For extension members, prepend the extension receiver type to match how the runtime sees the method.
                // At runtime, extension member methods have the receiver as the first parameter, but Roslyn's
                // methodSymbol.Parameters doesn't include it.
                if (isExtensionMember && extensionReceiverType is not null)
                {
                    parameterTypeNames.Insert(0, extensionReceiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
                
                descriptor.ParameterTypeNames = parameterTypeNames;
            }

            if (classForNaming is { IsGenericType: true })
            {
                descriptor.ClassTypeParameterList = SyntaxFactory.TypeParameterList();

                foreach (var additionalClassTypeParameter in classForNaming.TypeParameters)
                {
                    descriptor.ClassTypeParameterList = descriptor.ClassTypeParameterList.AddParameters(
                        SyntaxFactory.TypeParameter(additionalClassTypeParameter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    );
                    
                    // See https://github.com/dotnet/roslyn/blob/d7e010bbe5b1d37837417fc5e79ecb2fd9b7b487/src/VisualStudio/CSharp/Impl/ObjectBrowser/DescriptionBuilder.cs#L340
                    if (!additionalClassTypeParameter.ConstraintTypes.IsDefaultOrEmpty)
                    {
                        descriptor.ClassConstraintClauses ??= SyntaxFactory.List<TypeParameterConstraintClauseSyntax>();

                        var parameters = new List<TypeConstraintSyntax>();

                        if (additionalClassTypeParameter.HasReferenceTypeConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("class"));
                        }

                        if (additionalClassTypeParameter.HasValueTypeConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("struct"));
                        }

                        if (additionalClassTypeParameter.HasNotNullConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("notnull"));
                        }
                        
                        parameters.AddRange(additionalClassTypeParameter
                            .ConstraintTypes
                            .Select(c => 
                                MakeTypeConstraint(c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            )
                        );

                        if (additionalClassTypeParameter.HasConstructorConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("new()"));
                        }

                        descriptor.ClassConstraintClauses = descriptor.ClassConstraintClauses.Value.Add(
                            SyntaxFactory.TypeParameterConstraintClause(
                                SyntaxFactory.IdentifierName(additionalClassTypeParameter.Name),
                                SyntaxFactory.SeparatedList<TypeParameterConstraintSyntax>(parameters)
                            )
                        );
                    }
                }
            }

            // Handle extension members - add @this parameter with the extension receiver type
            if (isExtensionMember && extensionReceiverType is not null)
            {
                descriptor.ParametersList = descriptor.ParametersList.AddParameters(
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier("@this")
                    )
                    .WithType(
                        SyntaxFactory.ParseTypeName(
                            extensionReceiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        )
                    )
                );
            }
            else if (!member.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                descriptor.ParametersList = descriptor.ParametersList.AddParameters(
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier("@this")
                    )
                    .WithType(
                        SyntaxFactory.ParseTypeName(
                            memberSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        )
                    )
                );
            }

            // Handle target type for extension members
            if (isExtensionMember && extensionReceiverType is not null)
            {
                descriptor.TargetClassNamespace = extensionReceiverType.ContainingNamespace.IsGlobalNamespace ? null : extensionReceiverType.ContainingNamespace.ToDisplayString();
                descriptor.TargetNestedInClassNames = GetNestedInClassPath(extensionReceiverType);
            }
            else if (methodSymbol is { IsExtensionMethod: true })
            {
                var targetTypeSymbol = methodSymbol.Parameters.First().Type;
                descriptor.TargetClassNamespace = targetTypeSymbol.ContainingNamespace.IsGlobalNamespace ? null : targetTypeSymbol.ContainingNamespace.ToDisplayString();
                descriptor.TargetNestedInClassNames = GetNestedInClassPath(targetTypeSymbol);
            }
            else
            {
                descriptor.TargetClassNamespace = descriptor.ClassNamespace;
                descriptor.TargetNestedInClassNames = descriptor.NestedInClassNames;
            }

            // Projectable methods
            if (memberBody is MethodDeclarationSyntax methodDeclarationSyntax)
            {
                ExpressionSyntax? bodyExpression = null;

                if (methodDeclarationSyntax.ExpressionBody is not null)
                {
                    // Expression-bodied method (e.g., int Foo() => 1;)
                    bodyExpression = methodDeclarationSyntax.ExpressionBody.Expression;
                }
                else if (methodDeclarationSyntax.Body is not null)
                {
                    // Block-bodied method (e.g., int Foo() { return 1; })
                    
                    // Emit warning if AllowBlockBody is not set to true
                    if (!allowBlockBody)
                    {
                        var diagnostic = Diagnostic.Create(Diagnostics.BlockBodyExperimental, methodDeclarationSyntax.GetLocation(), memberSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                    
                    var blockConverter = new BlockStatementConverter(context, expressionSyntaxRewriter);
                    bodyExpression = blockConverter.TryConvertBlock(methodDeclarationSyntax.Body, memberSymbol.Name);

                    if (bodyExpression is null)
                    {
                        // Diagnostics already reported by BlockStatementConverter
                        return null;
                    }
                    
                    // The expression has already been rewritten by BlockStatementConverter, so we don't rewrite it again
                }
                else
                {
                    var diagnostic = Diagnostic.Create(Diagnostics.RequiresBodyDefinition, methodDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                    return null;
                }

                var returnType = declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ReturnType);

                descriptor.ReturnTypeName = returnType.ToString();
                // Only rewrite expression-bodied methods, block-bodied methods are already rewritten
                descriptor.ExpressionBody = methodDeclarationSyntax.ExpressionBody is not null 
                    ? (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression)
                    : bodyExpression;
                foreach (var additionalParameter in ((ParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ParameterList)).Parameters)
                {
                    descriptor.ParametersList = descriptor.ParametersList.AddParameters(additionalParameter);
                }

                if (methodDeclarationSyntax.TypeParameterList is not null)
                {
                    descriptor.TypeParameterList = SyntaxFactory.TypeParameterList();
                    foreach (var additionalTypeParameter in ((TypeParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.TypeParameterList)).Parameters)
                    {
                        descriptor.TypeParameterList = descriptor.TypeParameterList.AddParameters(additionalTypeParameter);
                    }
                }

                if (methodDeclarationSyntax.ConstraintClauses.Any())
                {
                    descriptor.ConstraintClauses = SyntaxFactory.List(
                        methodDeclarationSyntax
                            .ConstraintClauses
                            .Select(x => (TypeParameterConstraintClauseSyntax)declarationSyntaxRewriter.Visit(x))
                        );
                }
            }
            
            // Projectable properties
            else if (memberBody is PropertyDeclarationSyntax propertyDeclarationSyntax)
            {
                ExpressionSyntax? bodyExpression = null;
    
                // Expression-bodied property: int Prop => value;
                if (propertyDeclarationSyntax.ExpressionBody is not null)
                {
                    
                    bodyExpression = propertyDeclarationSyntax.ExpressionBody.Expression;
                }
                else if (propertyDeclarationSyntax.AccessorList is not null)
                {
                    // Property with explicit getter
                    var getter = propertyDeclarationSyntax.AccessorList.Accessors
                        .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            
                    if (getter?.ExpressionBody is not null)
                    {
                        // get => expression;
                        bodyExpression = getter.ExpressionBody.Expression;
                    }
                    else if (getter?.Body is not null)
                    {
                        // get { return expression; }
                        // Emit warning if AllowBlockBody is not set to true
                        if (!allowBlockBody)
                        {
                            var diagnostic = Diagnostic.Create(Diagnostics.BlockBodyExperimental, propertyDeclarationSyntax.GetLocation(), memberSymbol.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                        
                        var blockConverter = new BlockStatementConverter(context, expressionSyntaxRewriter);
                        bodyExpression = blockConverter.TryConvertBlock(getter.Body, memberSymbol.Name);
                    }
                }
    
                if (bodyExpression is null)
                {
                    var diagnostic = Diagnostic.Create(Diagnostics.RequiresBodyDefinition, propertyDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                    return null;
                }

                var returnType = declarationSyntaxRewriter.Visit(propertyDeclarationSyntax.Type);

                descriptor.ReturnTypeName = returnType.ToString();
                descriptor.ExpressionBody = (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression);
            }
            else
            {
                return null;
            }

            return descriptor;
        }

        private static TypeConstraintSyntax MakeTypeConstraint(string constraint) => SyntaxFactory.TypeConstraint(SyntaxFactory.IdentifierName(constraint));
    }
}
