using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    public class ReturnTypeSyntaxRewriter : CSharpSyntaxRewriter
    {
        readonly SemanticModel _semanticModel;

        public ReturnTypeSyntaxRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitNullableType(NullableTypeSyntax node)
        {
            var typeInfo = _semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is not null)
            {
                if (typeInfo.Type.TypeKind is not TypeKind.Struct)
                {
                    return Visit(node.ElementType)
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia()); 
                }
            }

            return base.VisitNullableType(node);
        }
    }
}
