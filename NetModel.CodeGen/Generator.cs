using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetModel.CodeGen;

[Generator]
public sealed class Generator : IIncrementalGenerator
{
	internal sealed record class ProtocolInfo(
		INamedTypeSymbol Symbol,
		ImmutableArray<Diagnostic> Diagnostics,
		ImmutableArray<KeyValuePair<ushort, INamedTypeSymbol>>? Schemas = null);

	internal sealed record class SchemaIntermediate(
		INamedTypeSymbol Symbol,
		ClassDeclarationSyntax Syntax,
		ushort? Key);

	internal sealed record class SchemaInfo(
		INamedTypeSymbol Symbol,
		ImmutableArray<Diagnostic> Diagnostics,
		INamedTypeSymbol? Protocol,
		ushort? Key);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var protocols = context.SyntaxProvider.ForAttributeWithMetadataName<ProtocolInfo?>(
			"NetModel.ProtocolAttribute",
			predicate: static (node, _) => node is InterfaceDeclarationSyntax,
			transform: static (ctx, _) =>
			{
				if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;
				if (ctx.TargetNode is not InterfaceDeclarationSyntax syntax) return null;

				var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

				if (!HasPartialModifier(syntax))
					diagnostics.AddTypeDiagnostic(Diagnostics.MustBePartial, syntax);

				return new ProtocolInfo(symbol, diagnostics.ToImmutable());
			})
			.Where(static p => p is not null)!
			.Select(static (p, _) => p!);

		var schemas = context.SyntaxProvider.ForAttributeWithMetadataName<SchemaIntermediate?>(
			"NetModel.SchemaAttribute",
			predicate: static (node, _) => node is ClassDeclarationSyntax,
			transform: static (ctx, _) =>
			{
				if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;
				if (ctx.TargetNode is not ClassDeclarationSyntax syntax) return null;

				return new SchemaIntermediate(symbol, syntax, TryGetSchemaKey(ctx.Attributes));
			})
			.Where(static s => s is not null)
			.Select(static (s, _) => s!)
			.Collect()
			.Combine(protocols.Select(static (info, _) => info.Symbol).Collect())
			.SelectMany(static (ctx, _) =>
			{
				var (schemas, protocols) = ctx;

				var protocolSet = protocols
					.Select(static protocol => protocol.OriginalDefinition)
					.ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

				return schemas.Select(schema =>
				{
					var (symbol, syntax, key) = schema;
					var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

					if (!HasPartialModifier(syntax))
						diagnostics.AddTypeDiagnostic(Diagnostics.MustBePartial, syntax);

					if (key is null)
					{
						diagnostics.AddSymbolDiagnostic(Diagnostics.MissingSchemaKey, symbol);
						return new SchemaInfo(symbol, diagnostics.ToImmutable(), null, null);
					}

					var candidateProtocols = symbol.AllInterfaces
						.Select(static i => i.OriginalDefinition)
						.Where(protocolSet.Contains)
						.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
						.ToList();

					INamedTypeSymbol? protocol = null;

					if (candidateProtocols.Count == 0)
						diagnostics.AddSymbolDiagnostic(Diagnostics.MustImplementProtocol, symbol);
					else if (candidateProtocols.Count > 1)
						diagnostics.AddSymbolDiagnostic(Diagnostics.MustImplementOneProtocol, symbol);
					else
						protocol = candidateProtocols[0];

					if (symbol.IsGenericType)
						diagnostics.AddSymbolDiagnostic(Diagnostics.CannotBeGeneric, symbol);

					return new SchemaInfo(symbol, diagnostics.ToImmutable(), protocol, key);
				});
			});

		protocols = protocols.Collect()
			.Combine(schemas.Collect())
			.SelectMany(static (ctx, _) =>
			{
				var (protocols, schemas) = ctx;

				var schemaLookup = schemas
					.Where(static info => info.Protocol is not null && info.Key is not null)
					.ToLookup(static info => info.Protocol!, SymbolEqualityComparer.Default);

				return protocols.Select(protocol =>
				{
					var builder = ImmutableArray.CreateBuilder<Diagnostic>();
					builder.AddRange(protocol.Diagnostics);

					var groupedSchemas = schemaLookup[protocol.Symbol].ToLookup(static schema => schema.Key!.Value);

					foreach (var schema in groupedSchemas.Where(static group => group.Skip(1).Any()).SelectMany(static group => group))
						builder.Add(Diagnostic.Create(Diagnostics.NoDuplicateKeys, schema.Symbol.Locations.FirstOrDefault(), schema.Symbol.Name));

					var uniqueSchemas = groupedSchemas
						.Where(static group => group.Count() == 1)
						.Select(static group => group.First());

					return protocol with
					{
						Schemas = uniqueSchemas
							.OrderBy(static schema => schema.Key)
							.Select(static schema => new KeyValuePair<ushort, INamedTypeSymbol>(schema.Key!.Value, schema.Symbol))
							.ToImmutableArray(),
						Diagnostics = builder.ToImmutable()
					};
				});
			});

		context.RegisterSourceOutput(protocols, static (spc, info) =>
		{
			var (protocol, diagnostics, schemas) = info;

			foreach (var diagnostic in diagnostics)
				spc.ReportDiagnostic(diagnostic);

			if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
				return;

			string code = GenerateProtocol(protocol, schemas!);
			spc.AddSource($"{protocol.Name}.Protocol.g.cs", SourceText.From(code, Encoding.UTF8));
		});

		var attributeProvider = context.CompilationProvider
										.Select(static (compilation, _) => new {
											IgnoreAttribute = compilation.GetTypeByMetadataName("NetModel.IgnoreAttribute")!,
											IncludeAttribute = compilation.GetTypeByMetadataName("NetModel.IncludeAttribute")!,
											FixedLengthAttribute = compilation.GetTypeByMetadataName("NetModel.FixedLengthAttribute")!,
										});

		context.RegisterSourceOutput(schemas, static (spc, info) =>
		{
			var (schema, upstreamDiagnostics, protocol, key) = info;

			var diagnostics = upstreamDiagnostics.ToBuilder();

			var fields = schema.GetMembers().OfType<IFieldSymbol>();
			var backings = fields.Where(field => field.IsImplicitlyDeclared &&
												 field.AssociatedSymbol is IPropertySymbol property &&
												 
												 property.GetAttributes().Contains();

			foreach (var diagnostic in diagnostics)
				spc.ReportDiagnostic(diagnostic);

			if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
				return;

			if (protocol is null || key is null)
				return;

			string code = GenerateSchema(schema, protocol, key.Value);
			spc.AddSource($"{schema.Name}.Schema.g.cs", SourceText.From(code, Encoding.UTF8));
		});
	}

	private static bool HasPartialModifier(TypeDeclarationSyntax syntax)
		=> syntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));

	private static ushort? TryGetSchemaKey(ImmutableArray<AttributeData> attributes)
	{
		foreach (var attribute in attributes)
		{
			if (attribute.ConstructorArguments.Length > 0 &&
				attribute.ConstructorArguments[0].Value is ushort constructorKey)
			{
				return constructorKey;
			}

			foreach (var argument in attribute.NamedArguments)
			{
				if (argument.Key == "Key" && argument.Value.Value is ushort namedKey)
					return namedKey;
			}
		}

		return null;
	}

	private static string GenerateSchema(INamedTypeSymbol schema, INamedTypeSymbol protocol, ushort key)
	{
		

		StringBuilder sb = new();
		sb.AppendLine($$"""
		// <auto-generated>
		#nullable enable
		using NetModel;

		using System;

		namespace {{schema.ContainingNamespace.ToDisplayString()}};

		// Member of {{protocol.Name}}
		partial class {{schema.Name}} {
			public static ushort Key => {{key}};
		""");
		sb.AppendLine($$"""
			public void Serialize(BinaryWriter writer) => throw new NotImplementedException();
			public static DeserializationResult Deserialize(BinaryReader reader, out {{schema.Name}}? schema) => throw new NotImplementedException();
		}
		""");
		return sb.ToString();
	}

	private static string GenerateProtocol(
		INamedTypeSymbol protocol,
		IEnumerable<KeyValuePair<ushort, INamedTypeSymbol>> schemas)
	{
		var externalNamespaces = schemas.Select(kvp => kvp.Value.ContainingNamespace)
								  .Distinct<INamespaceSymbol>(SymbolEqualityComparer.Default)
								  .Where<INamespaceSymbol>(space => !SymbolEqualityComparer.Default.Equals(space, protocol.ContainingNamespace))
								  .Select(space => space.ToDisplayString());
		StringBuilder sb = new();
		sb.AppendLine($$"""
		// <auto-generated>
		#nullable enable
		using NetModel;
		""");
		foreach (var space in externalNamespaces)
		{
			sb.AppendFormat("using {0};\n", space);
		}
		sb.AppendLine($$"""
		namespace {{protocol.ContainingNamespace.ToDisplayString()}};

		partial interface {{protocol.Name}} {
			public static abstract ushort Key {get;}
			
			public void Serialize(BinaryWriter reader);
			public void BeforeSerialize() { }

			public static virtual DeserializationResult Deserialize(
				BinaryReader reader,
				out {{protocol.Name}}? schema
			) {
				switch(reader.ReadUInt16()) {
		""");

		foreach ((ushort key, INamedTypeSymbol schema) in schemas.Select(static kvp => (kvp.Key, kvp.Value)))
		{
			sb.AppendLine($$"""
					case {{key}}:
					{
						var result = {{schema.Name}}.Deserialize(reader, out var concrete);
						schema = concrete as {{protocol.Name}};
						return result;
					}
			""");
		}

		sb.Append("""
					default:
						schema = null;
						return DeserializationResult.Failure | DeserializationResult.UnknownKey;
				}
			}
		}
		""");

		return sb.ToString();
	}
}