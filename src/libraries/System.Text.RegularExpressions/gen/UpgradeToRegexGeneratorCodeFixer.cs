﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace System.Text.RegularExpressions.Generator
{
    /// <summary>
    /// Roslyn code fixer that will listen to SysLIB1046 diagnostics and will provide a code fix which onboards a particular Regex into
    /// source generation.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class UpgradeToRegexGeneratorCodeFixer : CodeFixProvider
    {
        private const string RegexTypeName = "System.Text.RegularExpressions.Regex";
        private const string RegexGeneratorTypeName = "System.Text.RegularExpressions.RegexGeneratorAttribute";
        private const string DefaultRegexMethodName = "MyRegex";

        /// <inheritdoc />
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticDescriptors.UseRegexSourceGeneration.Id);

        public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc />
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            // Fetch the node to fix, and register the codefix by invoking the ConvertToSourceGenerator method.
            SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return;
            }

            SyntaxNode nodeToFix = root.FindNode(context.Span, getInnermostNodeForTie: true);
            if (nodeToFix is null)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    SR.UseRegexSourceGeneratorTitle,
                    cancellationToken => ConvertToSourceGenerator(context.Document, root, nodeToFix, context.Diagnostics[0], cancellationToken),
                    equivalenceKey: SR.UseRegexSourceGeneratorTitle),
                context.Diagnostics);
        }

        /// <summary>
        /// Takes a <see cref="Document"/> and a <see cref="Diagnostic"/> and returns a new <see cref="Document"/> with the replaced
        /// nodes in order to apply the code fix to the diagnostic.
        /// </summary>
        /// <param name="document">The original document.</param>
        /// <param name="root">The root of the syntax tree.</param>
        /// <param name="nodeToFix">The node to fix. This is where the diagnostic was produced.</param>
        /// <param name="diagnostic">The diagnostic to fix.</param>
        /// <param name="cancellationToken">The cancellation token for the async operation.</param>
        /// <returns>The new document with the replaced nodes after applying the code fix.</returns>
        private static async Task<Document> ConvertToSourceGenerator(Document document, SyntaxNode root, SyntaxNode nodeToFix, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            // We first get the compilation object from the document
            SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return document;
            }
            Compilation compilation = semanticModel.Compilation;

            // We then get the symbols for the Regex and RegexGeneratorAttribute types.
            INamedTypeSymbol? regexSymbol = compilation.GetTypeByMetadataName(RegexTypeName);
            INamedTypeSymbol? regexGeneratorAttributeSymbol = compilation.GetTypeByMetadataName(RegexGeneratorTypeName);
            if (regexSymbol is null || regexGeneratorAttributeSymbol is null)
            {
                return document;
            }

            // Save the operation object from the nodeToFix before it gets replaced by the new method invocation.
            // We will later use this operation to get the parameters out and pass them into the RegexGenerator attribute.
            IOperation? operation = semanticModel.GetOperation(nodeToFix, cancellationToken);
            if (operation is null)
            {
                return document;
            }

            // Get the parent type declaration so that we can inspect its methods as well as check if we need to add the partial keyword.
            SyntaxNode? typeDeclarationOrCompilationUnit = nodeToFix.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

            if (typeDeclarationOrCompilationUnit is null)
            {
                typeDeclarationOrCompilationUnit = await nodeToFix.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            }

            // Calculate what name should be used for the generated static partial method
            string methodName = DefaultRegexMethodName;

            INamedTypeSymbol? typeSymbol = typeDeclarationOrCompilationUnit is TypeDeclarationSyntax typeDeclaration ?
                semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) :
                semanticModel.GetDeclaredSymbol((CompilationUnitSyntax)typeDeclarationOrCompilationUnit, cancellationToken)?.ContainingType;

            if (typeSymbol is not null)
            {
                IEnumerable<ISymbol> members = GetAllMembers(typeSymbol);
                int memberCount = 1;
                while (members.Any(m => m.Name == methodName))
                {
                    methodName = $"{DefaultRegexMethodName}{memberCount++}";
                }
            }

            // Walk the type hirerarchy of the node to fix, and add the partial modifier to each ancestor (if it doesn't have it already)
            // We also keep a count of how many partial keywords we added so that we can later find the nodeToFix again on the new root using the text offset.
            int typesModified = 0;
            root = root.ReplaceNodes(
                nodeToFix.Ancestors().OfType<TypeDeclarationSyntax>(),
                (_, typeDeclaration) =>
                {
                    if (!typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        typesModified++;
                        return typeDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword)).WithAdditionalAnnotations(Simplifier.Annotation);
                    }

                    return typeDeclaration;
                });

            // We find nodeToFix again by calculating the offset of how many partial keywords we had to add.
            nodeToFix = root.FindNode(new TextSpan(nodeToFix.Span.Start + (typesModified * "partial".Length), nodeToFix.Span.Length), getInnermostNodeForTie: true);
            if (nodeToFix is null)
            {
                return document;
            }

            // We need to find the typeDeclaration again, but now using the new root.
            typeDeclarationOrCompilationUnit = typeDeclarationOrCompilationUnit is TypeDeclarationSyntax ?
                nodeToFix.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() :
                await nodeToFix.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            Debug.Assert(typeDeclarationOrCompilationUnit is not null);
            SyntaxNode newTypeDeclarationOrCompilationUnit = typeDeclarationOrCompilationUnit;

            // We generate a new invocation node to call our new partial method, and use it to replace the nodeToFix.
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            SyntaxGenerator generator = editor.Generator;

            // Generate the modified type declaration depending on whether the callsite was a Regex constructor call
            // or a Regex static method invocation.
            SyntaxNode replacement = generator.InvocationExpression(generator.IdentifierName(methodName));
            ImmutableArray<IArgumentOperation> operationArguments;
            if (operation is IInvocationOperation invocationOperation) // When using a Regex static method
            {
                operationArguments = invocationOperation.Arguments;
                IEnumerable<SyntaxNode> arguments = operationArguments
                    .Where(arg => arg.Parameter.Name is not (UpgradeToRegexGeneratorAnalyzer.OptionsArgumentName or UpgradeToRegexGeneratorAnalyzer.PatternArgumentName))
                    .Select(arg => arg.Syntax);

                replacement = generator.InvocationExpression(generator.MemberAccessExpression(replacement, invocationOperation.TargetMethod.Name), arguments);
            }
            else
            {
                operationArguments = ((IObjectCreationOperation)operation).Arguments;
            }

            newTypeDeclarationOrCompilationUnit = newTypeDeclarationOrCompilationUnit.ReplaceNode(nodeToFix, WithTrivia(replacement, nodeToFix));

            // Initialize the inputs for the RegexGenerator attribute.
            SyntaxNode? patternValue = GetNode(operationArguments, generator, UpgradeToRegexGeneratorAnalyzer.PatternArgumentName);
            SyntaxNode? regexOptionsValue = GetNode(operationArguments, generator, UpgradeToRegexGeneratorAnalyzer.OptionsArgumentName);

            // Generate the new static partial method
            MethodDeclarationSyntax newMethod = (MethodDeclarationSyntax)generator.MethodDeclaration(
                name: methodName,
                returnType: generator.TypeExpression(regexSymbol),
                modifiers: DeclarationModifiers.Static | DeclarationModifiers.Partial,
                accessibility: Accessibility.Private);

            // Allow user to pick a different name for the method.
            newMethod = newMethod.ReplaceToken(newMethod.Identifier, SyntaxFactory.Identifier(methodName).WithAdditionalAnnotations(RenameAnnotation.Create()));

            // Generate the RegexGenerator attribute syntax node with the specified parameters.
            SyntaxNode attributes = generator.Attribute(generator.TypeExpression(regexGeneratorAttributeSymbol), attributeArguments: (patternValue, regexOptionsValue) switch
            {
                ({ }, null) => new[] { patternValue },
                ({ }, { }) => new[] { patternValue, regexOptionsValue },
                _ => Array.Empty<SyntaxNode>(),
            });

            // Add the attribute to the generated method.
            newMethod = (MethodDeclarationSyntax)generator.AddAttributes(newMethod, attributes);

            // Add the method to the type.
            newTypeDeclarationOrCompilationUnit = newTypeDeclarationOrCompilationUnit is TypeDeclarationSyntax newTypeDeclaration ?
                newTypeDeclaration.AddMembers(newMethod) :
                ((CompilationUnitSyntax)newTypeDeclarationOrCompilationUnit).AddMembers((ClassDeclarationSyntax)generator.ClassDeclaration("Program", modifiers: DeclarationModifiers.Partial, members: new[] { newMethod }));

            // Replace the old type declaration with the new modified one, and return the document.
            return document.WithSyntaxRoot(root.ReplaceNode(typeDeclarationOrCompilationUnit, newTypeDeclarationOrCompilationUnit));

            static IEnumerable<ISymbol> GetAllMembers(ITypeSymbol? symbol)
            {
                while (symbol != null)
                {
                    foreach (ISymbol member in symbol.GetMembers())
                    {
                        yield return member;
                    }

                    symbol = symbol.BaseType;
                }
            }

            // Helper method that looks generates the node for pattern argument or options argument.
            static SyntaxNode? GetNode(ImmutableArray<IArgumentOperation> arguments, SyntaxGenerator generator, string parameterName)
            {
                var argument = arguments.SingleOrDefault(arg => arg.Parameter.Name == parameterName);
                if (argument is null)
                {
                    return null;
                }

                Debug.Assert(parameterName is UpgradeToRegexGeneratorAnalyzer.OptionsArgumentName or UpgradeToRegexGeneratorAnalyzer.PatternArgumentName);
                if (parameterName == UpgradeToRegexGeneratorAnalyzer.OptionsArgumentName)
                {
                    string optionsLiteral = Literal(((RegexOptions)(int)argument.Value.ConstantValue.Value).ToString());
                    return SyntaxFactory.ParseExpression(optionsLiteral);
                }
                else
                {
                    return generator.LiteralExpression(argument.Value.ConstantValue.Value);
                }
            }

            static string Literal(string stringifiedRegexOptions)
            {
                if (int.TryParse(stringifiedRegexOptions, NumberStyles.Integer, CultureInfo.InvariantCulture, out int options))
                {
                    // The options were formatted as an int, which means the runtime couldn't
                    // produce a textual representation.  So just output casting the value as an int.
                    return $"(RegexOptions)({options})";
                }

                // Parse the runtime-generated "Option1, Option2" into each piece and then concat
                // them back together.
                string[] parts = stringifiedRegexOptions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i] = "RegexOptions." + parts[i].Trim();
                }
                return string.Join(" | ", parts);
            }

            static SyntaxNode WithTrivia(SyntaxNode method, SyntaxNode nodeToFix)
                => method.WithLeadingTrivia(nodeToFix.GetLeadingTrivia()).WithTrailingTrivia(nodeToFix.GetTrailingTrivia());
        }
    }
}
