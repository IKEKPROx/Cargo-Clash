using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
// This deliberately omits Unity references. It checks syntax, scopes and duplicate declarations only.
var refs = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
var fixture = CSharpCompilation.Create("Regression", new[] { CSharpSyntaxTree.ParseText(
    "class C { void M(bool b) { if(b) { string words = \"\"; } string words = \"\"; } }") }, refs);
if (!fixture.GetDiagnostics().Any(d => d.Id == "CS0136"))
    throw new Exception("Scope checker did not detect the known HUD variable-shadowing regression.");
var paths = Directory.GetFiles(Path.Combine(args[0], "Assets"), "*.cs", SearchOption.AllDirectories);
var options = CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR", "UNITY_STANDALONE_OSX", "ENABLE_INPUT_SYSTEM");
var trees = paths.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), options, p)).ToArray();
var compilation = CSharpCompilation.Create("ScopeCheck", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var diagnostics = compilation.GetDiagnostics();
var ids = new[] { "CS0136", "CS0128", "CS0102", "CS0111", "CS0841" };
var scope = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && ids.Contains(d.Id)).ToArray();
var syntax = trees.SelectMany(t => t.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
Console.WriteLine($"Roslyn regression self-check passed; parsed {trees.Length} files. Syntax errors: {syntax.Length}; duplicate/declaration/scope errors: {scope.Length}");
foreach (var d in syntax) Console.WriteLine(d);
foreach (var d in scope) Console.WriteLine(d);
Console.WriteLine("Scope analysis only: Unity assemblies absent; Unity compilation and API binding NOT validated.");
Environment.ExitCode = scope.Length == 0 && syntax.Length == 0 ? 0 : 1;
