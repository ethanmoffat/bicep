// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Analyzers.Linter.Rules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Core.UnitTests.Diagnostics.LinterRuleTests;

[TestClass]
public class NoImpossibleComparisonsRuleTests : LinterRuleTestsBase
{
    private static void AssertDiagnostics(string text, params string[] expectedMessages)
        => AssertLinterRuleDiagnostics(NoImpossibleComparisonsRule.Code, text, expectedMessages);

    [TestMethod]
    public void A_misspelled_alternative_is_reported_with_the_alternative_it_resembles()
    {
        AssertDiagnostics("""
            param scope ('resourceGroup' | 'subscription')
            output isSubscription bool = scope == 'subscripton'
            """,
            "[2] This comparison is always false, because no value of type 'resourceGroup' | 'subscription' equals a value of type 'subscripton'. Did you mean 'subscription'?");
    }

    [TestMethod]
    public void A_negated_comparison_is_reported_as_always_true()
    {
        AssertDiagnostics("""
            param scope ('resourceGroup' | 'subscription')
            output notTenant bool = scope != 'tenant'
            """,
            "[2] This comparison is always true, because no value of type 'resourceGroup' | 'subscription' equals a value of type 'tenant'.");
    }

    [TestMethod]
    public void The_literal_may_be_on_either_side()
    {
        AssertDiagnostics("""
            param scope ('resourceGroup' | 'subscription')
            output isSubscription bool = 'subscripton' == scope
            """,
            "[2] This comparison is always false, because no value of type 'subscripton' equals a value of type 'resourceGroup' | 'subscription'. Did you mean 'subscription'?");
    }

    [TestMethod]
    public void Allowed_values_are_a_closed_set_too()
    {
        AssertDiagnostics("""
            @allowed(['dev', 'prod'])
            param env string
            output isTest bool = env == 'test'
            """,
            "[3] This comparison is always false, because no value of type 'dev' | 'prod' equals a value of type 'test'.");
    }

    [TestMethod]
    public void Equality_is_case_sensitive_but_insensitive_equality_is_not()
    {
        AssertDiagnostics("""
            param scope ('resourceGroup' | 'subscription')
            output sensitive bool = scope == 'Subscription'
            output insensitive bool = scope =~ 'Subscription'
            output insensitiveMiss bool = scope =~ 'Tenant'
            """,
            "[2] This comparison is always false, because no value of type 'resourceGroup' | 'subscription' equals a value of type 'Subscription'. Did you mean 'subscription'?",
            "[4] This comparison is always false, because no value of type 'resourceGroup' | 'subscription' equals a value of type 'Tenant'.");
    }

    [TestMethod]
    public void Integer_and_computed_alternatives_are_judged_the_same_way()
    {
        AssertDiagnostics("""
            param count (1 | 2)
            param useA bool
            var letter = useA ? 'a' : 'b'
            output three bool = count == 3
            output c bool = letter == 'c'
            output a bool = letter == 'a'
            """,
            "[4] This comparison is always false, because no value of type 1 | 2 equals a value of type 3.",
            "[5] This comparison is always false, because no value of type 'a' | 'b' equals a value of type 'c'.");
    }

    [TestMethod]
    public void Two_closed_sets_with_nothing_in_common_are_reported()
    {
        AssertDiagnostics("""
            param left ('x' | 'y')
            param right ('p' | 'q')
            param overlapping ('y' | 'z')
            output never bool = left == right
            output sometimes bool = left == overlapping
            """,
            "[4] This comparison is always false, because no value of type 'x' | 'y' equals a value of type 'p' | 'q'.");
    }

    [TestMethod]
    public void Null_is_one_of_the_alternatives_of_a_nullable_set()
    {
        AssertDiagnostics("""
            param scope ('resourceGroup' | 'subscription')?
            output unset bool = scope == null
            output misspelled bool = scope == 'tenant'
            """,
            "[3] This comparison is always false, because no value of type 'resourceGroup' | 'subscription' | null equals a value of type 'tenant'.");
    }

    [TestMethod]
    public void A_constant_compared_with_a_constant_is_left_alone()
    {
        AssertDiagnostics("""
            var env = 'dev'
            output isProd bool = env == 'prod'
            output literals bool = 'a' != 'b'
            """);
    }

    [TestMethod]
    public void An_open_type_is_left_alone()
    {
        AssertDiagnostics("""
            param name string
            param anything object
            output a bool = name == 'x'
            output b bool = anything.value == 'x'
            """);
    }

    [TestMethod]
    public void A_resource_type_enumeration_is_left_alone_because_it_may_be_incomplete()
    {
        AssertDiagnostics("""
            resource account 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
              name: 'account'
            }
            output tier bool = account.properties.accessTier == 'Frozen'
            """);
    }

    [TestMethod]
    public void The_fix_replaces_the_literal_with_the_alternative_it_resembles()
    {
        AssertCodeFix(
            NoImpossibleComparisonsRule.Code,
            "Change to 'subscription'",
            """
            @allowed(['resourceGroup', 'subscription'])
            param scope string
            output isSubscription bool = scope == 'subs|cripton'
            """,
            """
            @allowed(['resourceGroup', 'subscription'])
            param scope string
            output isSubscription bool = scope == 'subscription'
            """);
    }
}
