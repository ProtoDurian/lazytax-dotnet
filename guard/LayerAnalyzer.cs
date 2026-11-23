using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LazyTax.Guard;

/// <summary>
/// LYR001: code under a context's <c>domain/</c> folder may not use EF, Npgsql, ASP.NET, HTTP or file IO.
/// The domain holds the model, services and ports; data/ and api/ hold the IO.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerAnalyzer : DiagnosticAnalyzer
{
  public const string Id = "LYR001";

  private static readonly string[] Banned =
  [
    "Microsoft.EntityFrameworkCore",
    "Npgsql",
    "Microsoft.AspNetCore",
    "System.Net.Http",
    "System.IO",
  ];

  private static readonly DiagnosticDescriptor Rule = new(
    Id,
    "domain/ must not use IO or framework code",
    "domain/ uses {0} (IO or framework); move this code to data/ or api/ and reach it through a port",
    "Layers",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

  public override void Initialize(AnalysisContext context)
  {
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSyntaxNodeAction(CheckUsing, SyntaxKind.UsingDirective);
    context.RegisterSyntaxNodeAction(CheckName, SyntaxKind.IdentifierName);
  }

  private static bool InDomain(SyntaxNode node)
  {
    var path = node.SyntaxTree.FilePath.Replace('\\', '/');
    return path.Contains("/src/domain/");
  }

  private static string? BannedPrefix(string? ns) =>
    ns is null ? null : Banned.FirstOrDefault(b => ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal));

  private static void CheckUsing(SyntaxNodeAnalysisContext ctx)
  {
    if (!InDomain(ctx.Node)) return;
    var name = ((UsingDirectiveSyntax)ctx.Node).Name?.ToString();
    if (BannedPrefix(name) is { } b) ctx.ReportDiagnostic(Diagnostic.Create(Rule, ctx.Node.GetLocation(), b));
  }

  private static void CheckName(SyntaxNodeAnalysisContext ctx)
  {
    if (!InDomain(ctx.Node) || ctx.Node.Parent is UsingDirectiveSyntax || ctx.Node.Ancestors().OfType<UsingDirectiveSyntax>().Any()) return;
    var symbol = ctx.SemanticModel.GetSymbolInfo(ctx.Node, ctx.CancellationToken).Symbol;
    if (symbol is null || symbol is INamespaceSymbol) return;
    var ns = (symbol as ITypeSymbol ?? symbol.ContainingType)?.ContainingNamespace?.ToDisplayString()
             ?? symbol.ContainingNamespace?.ToDisplayString();
    if (BannedPrefix(ns) is { } b) ctx.ReportDiagnostic(Diagnostic.Create(Rule, ctx.Node.GetLocation(), b));
  }
}
