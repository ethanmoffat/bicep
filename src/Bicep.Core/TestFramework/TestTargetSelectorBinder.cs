// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Extensions;
using Bicep.Core.Syntax;

namespace Bicep.Core.TestFramework;

/// <summary>
/// Reads a statically declared <c>match</c> selector out of a test body.
///
/// The selector type marks every property as compile-time constant, so this reader only ever needs to
/// understand literals. Anything it cannot read has already been reported as a type error, and is
/// skipped here rather than guessed at.
/// </summary>
public static class TestTargetSelectorBinder
{
    public static TestTargetSelector? TryBind(TestDeclarationSyntax test)
        => test.TryGetMatchSelectorSyntax() is { } selector ? TryBind(selector) : null;

    public static TestTargetSelector? TryBind(ObjectSyntax selector)
    {
        var root = TryReadString(selector, TestTargetSelector.RootPropertyName);
        var include = ReadStringArray(selector, TestTargetSelector.IncludePropertyName);
        var exclude = ReadStringArray(selector, TestTargetSelector.ExcludePropertyName);
        var allowEmpty = TryReadBool(selector, TestTargetSelector.AllowEmptyPropertyName) ?? false;

        return TestTargetSelector.Create(root, include, exclude, allowEmpty);
    }

    private static string? TryReadString(ObjectSyntax selector, string propertyName)
        => (selector.TryGetPropertyByName(propertyName)?.Value as StringSyntax)?.TryGetLiteralValue();

    private static bool? TryReadBool(ObjectSyntax selector, string propertyName)
        => (selector.TryGetPropertyByName(propertyName)?.Value as BooleanLiteralSyntax)?.Value;

    private static IEnumerable<string> ReadStringArray(ObjectSyntax selector, string propertyName)
    {
        if (selector.TryGetPropertyByName(propertyName)?.Value is not ArraySyntax array)
        {
            return [];
        }

        return array.Items
            .Select(item => (item.Value as StringSyntax)?.TryGetLiteralValue())
            .WhereNotNull();
    }
}
