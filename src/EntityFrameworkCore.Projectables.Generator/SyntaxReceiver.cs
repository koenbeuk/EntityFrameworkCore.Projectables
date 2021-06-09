using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Generator
{
    public class SyntaxReceiver : ISyntaxReceiver
    {
        public List<MemberDeclarationSyntax>? Candidates { get; private set; } 

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is MemberDeclarationSyntax memberDeclarationSyntax && memberDeclarationSyntax.AttributeLists.Count > 0)
            {
                var hasProjectableAttribute = memberDeclarationSyntax.AttributeLists
                    .SelectMany(x => x.Attributes)
                    .Any(x => x.Name.ToString().Contains("Projectable"));

                if (hasProjectableAttribute)
                {
                    if (Candidates == null)
                    {
                        Candidates = new();
                    }

                    Candidates.Add(memberDeclarationSyntax);
                }
            }
        }
    }
}
