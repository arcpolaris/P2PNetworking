using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetModel.CodeGen;

internal static class Diagnostics
{
	public static readonly DiagnosticDescriptor MustBePartial = new(
		id: "P2P001",
		title: "Type must be partial",
		messageFormat: "Type '{0}' must be declared partial",
		category: "Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MustImplementProtocol = new(
		id: "P2P002",
		title: "Schema must implement protocol",
		messageFormat: "Schema '{0}' does not implement a protocol",
		category: "Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MustImplementOneProtocol = new(
		id: "P2P003",
		title: "Schema cannot implement multiple protocols",
		messageFormat: "Schema '{0}' implements multiple protocols",
		category: "Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor CannotBeGeneric = new(
		id: "P2P004",
		title: "Schema cannot have generic type parameters",
		messageFormat: "Schema '{0}' is a generic type",
		category: "Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NoDuplicateKeys = new(
		id: "P2P005",
		title: "Schemas part of the same protocol may not have duplicate keys",
		messageFormat: "Schema '{0}' is part of a key collision",
		category: "Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MissingSchemaKey = new(
		id: "P2P006",
		title: "Schema must declare a key",
		messageFormat: "Schema '{0}' does not declare a valid key",
		category: "Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InvalidProperty = new(
		id: "P2P007",
		title: "Properties in a schema must have a setter and be auto-backed",
		messageFormat: "Property '{0}' does not have a setter and/or is not auto-backed and will not be serialized",
		category: "Usage",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static void AddTypeDiagnostic(
		this ImmutableArray<Diagnostic>.Builder builder,
		DiagnosticDescriptor descriptor,
		TypeDeclarationSyntax syntax)
	{
		builder.Add(Diagnostic.Create(descriptor, syntax.Identifier.GetLocation(), syntax.Identifier.Text));
	}

	public static void AddSymbolDiagnostic(
		this ImmutableArray<Diagnostic>.Builder builder,
		DiagnosticDescriptor descriptor,
		INamedTypeSymbol symbol)
	{
		builder.Add(Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault(), symbol.Name));
	}
}