// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.TestFramework;

/// <summary>
/// The declared type of a test file's <c>mocks</c> declaration.
///
/// Each entry describes one exact runtime read and the synthetic answer to give for it. The matching
/// metadata is closed and required, because a mock that cannot be tied to a specific request is a
/// wildcard, and a wildcard would let a test pass by answering a call nobody meant to make. The
/// response is deliberately unconstrained: a partial response is normal, and the fields a test never
/// configures simply stay unset.
/// </summary>
public static class TestMockType
{
    public const string OperationPropertyName = "operation";
    public const string ResourceIdPropertyName = "resourceId";
    public const string ApiVersionPropertyName = "apiVersion";
    public const string RequestBodyPropertyName = "requestBody";
    public const string ResponsePropertyName = "response";
    public const string ResponsePropertiesName = "properties";

    public const string ReferenceOperation = "reference";
    public const string ListKeysOperation = "listKeys";

    private static readonly TypeSymbol Operation = TypeHelper.CreateTypeUnion(
        TypeFactory.CreateStringLiteralType(ReferenceOperation),
        TypeFactory.CreateStringLiteralType(ListKeysOperation));

    private static readonly ObjectType Entry = new(
        "mock",
        TypeSymbolValidationFlags.Default,
        [
            new NamedTypeProperty(OperationPropertyName, Operation, TypePropertyFlags.Required, "The runtime operation this mock answers."),
            new NamedTypeProperty(ResourceIdPropertyName, LanguageConstants.String, TypePropertyFlags.Required, "The exact resource ID the operation is issued against. This is an identity, not a pattern."),
            new NamedTypeProperty(ApiVersionPropertyName, LanguageConstants.String, TypePropertyFlags.Required, "The API version the operation is issued with."),
            new NamedTypeProperty(RequestBodyPropertyName, LanguageConstants.Any, TypePropertyFlags.None, "The exact request body the operation is issued with. An absent body matches only a request that has none."),
            new NamedTypeProperty(ResponsePropertyName, LanguageConstants.Any, TypePropertyFlags.None, "The synthetic answer. Fields it does not set stay unset rather than becoming null, zero or empty."),
        ],
        null);

    /// <summary>
    /// Mock names are chosen by the test author, so the outer object is open. The entries themselves are
    /// closed, which is what makes a misspelled field an error instead of a silently ignored setting.
    /// </summary>
    public static ObjectType Create()
        => new(
            "mocks",
            TypeSymbolValidationFlags.Default,
            [],
            new TypeProperty(Entry));
}
