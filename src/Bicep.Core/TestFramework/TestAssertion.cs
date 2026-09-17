// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Core.TestFramework;

/// <summary>
/// The shape of a single named assertion inside a test's <c>assertions</c> object.
///
/// An assertion declares exactly one of <see cref="PassWhenPropertyName"/> or
/// <see cref="FailOnPropertyName"/>. They differ in how the result is judged and reported, not in how
/// the test is executed: both inspect source facts and neither requires a deployment.
/// </summary>
public static class TestAssertion
{
    /// <summary>
    /// A boolean that must be true for the assertion to pass.
    /// </summary>
    public const string PassWhenPropertyName = "passWhen";

    /// <summary>
    /// A collection of offending facts. The assertion passes when the collection is empty, and reports
    /// the source location of every element otherwise. Its meaning is emptiness, not truthiness.
    /// </summary>
    public const string FailOnPropertyName = "failOn";

    /// <summary>
    /// The message explaining what the author must do when the assertion fails.
    /// </summary>
    public const string MessagePropertyName = "message";

    /// <summary>
    /// The template variable the target facts are supplied under while an assertion is evaluated.
    /// Its name is not a valid thing for a test to declare twice, so it cannot collide with a
    /// variable the test file itself contributes.
    /// </summary>
    public const string TargetVariableName = "$testTarget";
}
