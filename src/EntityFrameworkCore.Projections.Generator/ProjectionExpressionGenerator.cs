using EntityFrameworkCore.Projections.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Generator
{
    [Generator]
    public class ProjectionExpressionGenerator : ISourceGenerator
    {
        public const string ProjectionTargetParameterName = "projectionTarget";

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SyntaxReceiver receiver)
            {
                return;
            }

            if (receiver.Candidates.Count > 0)
            {
                var projectables = receiver.Candidates
                    .Select(x => ProjectableInterpreter.GetDescriptor(x, context))
                    .Where(x => x is not null);

                var resultBuilder = new StringBuilder();

                foreach (var projectable in projectables)
                {
                    resultBuilder.Clear();

                    foreach (var usingDirective in projectable.UsingDirectives)
                    {
                        resultBuilder.AppendLine(usingDirective);
                    }

                    if (projectable.TargetClassNamespace is not null && !projectable.UsingDirectives.Contains(projectable.TargetClassNamespace))
                    {
                        resultBuilder.AppendLine($"using {projectable.TargetClassNamespace};");
                    }

                    if (projectable.ClassNamespace is not null && projectable.ClassNamespace != projectable.TargetClassNamespace && !projectable.UsingDirectives.Contains(projectable.ClassNamespace))
                    {
                        resultBuilder.AppendLine($"using {projectable.ClassNamespace};");
                    }

                    var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(projectable.ClassNamespace, projectable.NestedInClassNames, projectable.MemberName);

                    resultBuilder.Append($@"
namespace EntityFrameworkCore.Projections.Generated
#nullable disable
{{
    public static class {generatedClassName}
    {{
        public static System.Linq.Expressions.Expression<System.Func<{projectable.TargetClassNamespace}.{string.Join(".", projectable.TargetNestedInClassNames)}, {projectable.ReturnTypeName}>> Expression{projectable.ParametersListString} => 
            {ProjectionTargetParameterName} => {projectable.Body};
    }}
}}");

                    context.AddSource($"{generatedClassName}_Generated", SourceText.From(resultBuilder.ToString(), Encoding.UTF8));
                }
            }
        }

        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }
}
