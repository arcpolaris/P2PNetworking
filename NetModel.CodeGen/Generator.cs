using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetModel.CodeGen;

[Generator]
public class Generator : IIncrementalGenerator
{
	internal sealed record class ProtocolInfo(INamedTypeSymbol Symbol, ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<KeyValuePair<ushort, INamedTypeSymbol>>? Schemas = null);
	internal sealed record class SchemaIntermediate(INamedTypeSymbol Symbol, ClassDeclarationSyntax Syntax, ushort Key);
	internal sealed record class SchemaInfo(INamedTypeSymbol Symbol, ImmutableArray<Diagnostic> Diagnostics, INamedTypeSymbol? Protocol, ushort Key);
	
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var protocols = context.SyntaxProvider.ForAttributeWithMetadataName<ProtocolInfo>(
			"NetModel.ProtocolAttribute",
			predicate: static (node, _) => node is InterfaceDeclarationSyntax,
			transform: static (ctx, _) =>
			{
				if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null!;
				var syntax = (InterfaceDeclarationSyntax)ctx.TargetNode;

				var builder = ImmutableArray.CreateBuilder<Diagnostic>();

				if (!syntax.Modifiers.All(m => m.IsKind(SyntaxKind.PartialKeyword)))
					builder.Add(Diagnostic.Create(Diagnostics.MustBePartial, syntax.GetLocation(), syntax.GetText()));


				return new ProtocolInfo(symbol, builder.ToImmutable());

			}).Where(static p => p is not null);

		var schemas = context.SyntaxProvider.ForAttributeWithMetadataName<SchemaIntermediate?>(
			"NetModel.SchemaAttribute",
			predicate: static (node, _) => node is ClassDeclarationSyntax,
			transform: static (ctx, _) =>
			{
				if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;
				var syntax = (ClassDeclarationSyntax)ctx.TargetNode;

				if (ctx.Attributes.SingleOrDefault(null!) is not AttributeData data) return null;

				if (data.NamedArguments.Single(kvp => kvp.Key == "Key").Value.Value is not ushort key) return null;

				return new SchemaIntermediate(symbol, syntax, key);
			}).Where(static s => s is not null)
			.Collect()
			.Combine(protocols.Select(static (info, _) => info.Symbol).Collect())
			.SelectMany(static (ctx, _) =>
			{
				var (schemas, protocols) = ctx;
				return schemas.Select(intermediate =>
				{
					(INamedTypeSymbol symbol, ClassDeclarationSyntax syntax, ushort key) = intermediate!;
					var builder = ImmutableArray.CreateBuilder<Diagnostic>();

					if (!syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
						builder.AddNewDiagnostic(Diagnostics.MustBePartial, syntax);

					var candidateProtocols = symbol.Interfaces.Intersect<INamedTypeSymbol>(protocols, SymbolEqualityComparer.Default).ToList();

					if (candidateProtocols.Count == 0)
						builder.AddNewDiagnostic(Diagnostics.MustImplementProtocol, syntax);
					else if (candidateProtocols.Count > 1)
						builder.AddNewDiagnostic(Diagnostics.MustImplementOneProtocol, syntax);

					var protocol = candidateProtocols.SingleOrDefault(null);

					if (symbol.IsGenericType)
						builder.AddNewDiagnostic(Diagnostics.CannotBeGeneric, syntax);

					return new SchemaInfo(symbol, builder.MoveToImmutable(), protocol, key);
				});
			});

		protocols = protocols.Collect()
			.Combine(schemas.Where(static info => info.Protocol is not null).Collect())
			.SelectMany(static (ctx, _) =>
			{
				var (protocols, schemas) = ctx;

				var schemaLookup = schemas.ToLookup(static info => info.Protocol, SymbolEqualityComparer.Default);
				return protocols.Select(protocol => (protocol, schemaLookup[protocol.Symbol])).Select(static intermediate =>
				{
					var (protocol, schemas) = intermediate;

					var schemaLookup = schemas.ToLookup(static schema => schema.Key);

					var builder = ImmutableArray.CreateBuilder<Diagnostic>();

					builder.AddRange(protocol.Diagnostics);

					foreach (var schema in schemaLookup.Where(static group => group.Skip(1).Any()).SelectMany(static item => item))
					{
						builder.Add(Diagnostic.Create(Diagnostics.NoDuplicateKeys, schema.Symbol.Locations.First(), schema.Symbol.Name));
					}

					

					return protocol with { Schemas = ImmutableArray.CreateRange(schemas.Select(static info => new KeyValuePair<ushort, INamedTypeSymbol>(info.Key, info.Symbol))), Diagnostics = builder.ToImmutable() };
				});
			});

		


		context.RegisterSourceOutput(protocols, static (spc, protocol) =>
		{
			var (symbol, diagnoses, schemas) = protocol;

			foreach (var diagnostic in diagnoses) spc.ReportDiagnostic(diagnostic);
			if (diagnoses.Any(diagnosis => diagnosis.Severity == DiagnosticSeverity.Error)) return;

			string code = GenerateProtocol(symbol, ((IEnumerable<KeyValuePair<ushort, INamedTypeSymbol>>)schemas!).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
			spc.AddSource($"{symbol.Name}.Protocol.g.cs", SourceText.From(code, Encoding.UTF8));
		});

		context.RegisterSourceOutput(schemas, static (spc, schema) =>
		{
			var (symbol, diagnoses, protocol, key) = schema;

			foreach (var diagnostic in diagnoses) spc.ReportDiagnostic(diagnostic);
			if (diagnoses.Any(diagnosis => diagnosis.Severity == DiagnosticSeverity.Error)) return;

			string code = GenerateSchema(symbol, protocol!, key);
			spc.AddSource($"{symbol.Name}.Schema.g.cs", SourceText.From(code, Encoding.UTF8));
		});
	}

	private static string GenerateSchema(INamedTypeSymbol symbol, INamedTypeSymbol protocol, ushort key)
	{
		return $$"""
		// <auto-generated>
		// Member of {{protocol.Name}}
		using NetModel;

		using System;

		namespace {{symbol.ContainingNamespace.ToDisplayString()}};

		partial class {{symbol.Name}} {
			public static ushort Key => {{key}};

			public {{protocol.Name}} Serialize(MemoryStream stream) => throw new NotImplementedException();
			public {{protocol.Name}} Deserialize(MemoryStream stream) => throw new NotImplementedException();
		}
		""";
	}

	private static string GenerateProtocol(INamedTypeSymbol symbol, Dictionary<ushort, INamedTypeSymbol> schemas)
	{
		StringBuilder sb = new();
		sb.AppendLine($$"""
		// <auto-generated>
		using NetModel;
		
		namespace {{symbol.ContainingNamespace.ToDisplayString()}};

		partial interface {{symbol.Name}} {
			public static ushort Key {get}
			
			public void Serialize(BinaryWriter reader);
			public void BeforeSerialize() { };

			public void Deserialize(BinaryReader reader);

			public static DeserializationResult Deserialize(
				BinaryReader reader,
				out {{symbol.Name}}? schema
			) {
				switch(reader.ReadUInt16()) {
		""");

		foreach (ushort key in schemas.Keys)
		{
			var schema = schemas[key];
			sb.AppendLine($$"""
					case {{key}}:
						schema = {{schema.Name}}.Deserialize(reader);
						return DeserializationResult.Success;
		""");
		}

		sb.Append("""
					default:
						schema = null;
						return DeserializationResult.Failure;
			}
		}
		""");
		return sb.ToString();
	}
}

