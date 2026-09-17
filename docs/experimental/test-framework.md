# Bicep Test Framework

> [!WARNING]
> The test framework is an experimental feature. There are no guarantees about its quality or stability, and its syntax and behavior may change in breaking ways. Do not rely on it for production usage.

The Bicep test framework lets you author client-side, offline tests for Bicep files. Tests compile and evaluate the file under test without deploying anything to Azure.

## Getting a build

The test framework is not in an official Bicep release. To try it you need a build that contains it.

Prebuilt artifacts are published as GitHub releases:

| Asset | Use |
| --- | --- |
| `bicep-win-x64.exe` | CLI for Windows |
| `bicep-linux-x64` | CLI for Linux |
| `vscode-bicep.vsix` | Matching editor tooling |

Download them anonymously; no token is required:

```console
$ curl -Lo bicep.exe https://github.com/ethanmoffat/bicep/releases/download/<tag>/bicep-win-x64.exe
$ ./bicep.exe --version
Bicep CLI version 0.47.97 (2ceadcb79b)
```

The version string ends with the commit the binary was built from. Pin that revision and check it, rather than assuming whichever `bicep` is on the path has the feature. A pipeline that quietly falls back to another `bicep` will otherwise appear to pass without having run any of these tests.

To install the editor tooling:

```console
$ code --install-extension vscode-bicep.vsix
```

> [!IMPORTANT]
> This extension shares its identifier with the Bicep extension published on the Marketplace, so installing it replaces that extension rather than sitting alongside it. Reinstall the Marketplace build to go back.

The extension bundles its own language server, so install the extension and the CLI from the same release. Mixing revisions can produce editor diagnostics that disagree with the CLI.

## Enabling the feature

Tests are gated behind the `testFramework` experimental feature. Assertions written inside the Bicep file under test are additionally gated behind the `assertions` feature.

`bicepconfig.json`:

```json
{
  "experimentalFeaturesEnabled": {
    "testFramework": true,
    "assertions": true
  }
}
```

## Test files

Tests may be declared either in an ordinary `.bicep` file or in a dedicated `.biceptest` file. A `.biceptest` file uses the same syntax as a `.bicep` file, but its purpose is to hold tests rather than deployable infrastructure. Keeping tests in `.biceptest` files separates them from the templates they exercise, so test declarations are never emitted as part of a deployment.

A test declaration names the file under test and supplies its parameters:

```bicep
test validPrefix 'storage.bicep' = {
  params: {
    namePrefix: 'contoso'
    location: 'eastus'
  }
}
```

The target path is a literal path relative to the test file.

Values for a test's own parameters live in a separate `.biceptestparam` file, described under
[Input cases](#input-cases).

A `.biceptest` file is not a deployable template. Referencing one from a `module` declaration, or
using one as the target of a test, is an error:

```console
$ bicep build main.bicep
main.bicep(1,10) : Error BCP461: A ".biceptest" file declares tests and is not a deployable template, so it cannot be referenced here. Reference the Bicep file under test instead.
```

## Selecting targets with `match`

A test can also select its targets from the filesystem instead of naming one. Omit the literal path and declare a `match` selector in the test body:

```bicep
test namingPolicy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
    exclude: ['_*.bicep']
  }
  params: {
    namePrefix: 'contoso'
    location: 'eastus'
  }
}
```

The test then produces one result per matched file. This is how you apply a single policy — a naming rule, a required-tag rule — to a whole folder without listing every file.

| Property | Required | Default | Meaning |
|----------|----------|---------|---------|
| `root` | No | `.` | The directory the patterns are relative to, itself relative to the directory containing the test file |
| `include` | Yes | — | Glob patterns selecting files under the root |
| `exclude` | No | `[]` | Glob patterns removing files from the included set |
| `allowEmpty` | No | `false` | Whether matching no files at all is acceptable |

Rules:

- Every value must be a compile-time constant. A selector that cannot be read from source alone is rejected, so the set of files a test covers is always visible in the test file.
- `include` and `exclude` patterns may not be rooted and may not contain `..` segments. Only `root` can widen the search, so the bounds of the walk are always a single, obvious value.
- Excludes always win over includes.
- Symbolic links and directory junctions under the root are not followed.
- Matching zero files is an error unless `allowEmpty` is `true`. A selector that has quietly stopped matching anything should not pass vacuously.
- A test uses either a literal target path or a `match` selector, never both.

### Discovery versus selection

These are two different things, and they are deliberately kept apart:

- **Discovery** is how the CLI finds test files. You either name a test file on the command line or match several with `--pattern`.
- **Selection** is how a test finds the files it applies to. That is what `match` does, and it is owned by the test file rather than by whoever invokes the CLI.

Because selection lives in the test file, running the same test from a different working directory — or from CI — always covers the same set of files, and reports the same case identities.

`match` is also evaluated against the filesystem on every run. Adding a new file that the selector already covers brings it under test on the next run, with no edit to the test file:

```console
$ bicep test main.biceptest
[✓] Evaluation policy (modules/one.bicep) Passed!
All 1 evaluations passed!

$ # add modules/two.bicep, then re-run
$ bicep test main.biceptest
[✓] Evaluation policy (modules/one.bicep) Passed!
[✓] Evaluation policy (modules/two.bicep) Passed!
All 2 evaluations passed!
```

### Each target is bound independently

Every matched target is compiled and bound on its own. The parameters in `params` are validated against that target's own parameters, not against a merged view of all targets. A target that fails to compile, or that needs a parameter the test does not supply, is reported against that target alone; the remaining targets still run, and the overall command still exits non-zero.

## Editor support

`.biceptest` files are registered as their own language (`bicep-test`) in the Bicep VS Code extension and are handled by the Bicep language server. Opening a `.biceptest` file gives syntax highlighting, diagnostics, formatting and completions.

Top-level completions in a `.biceptest` file are scoped to what a test file can declare:

| Offered | Not offered |
|---------|-------------|
| `test` (only when `testFramework` is enabled), `metadata`, `param`, `var`, `type`, `func`, `import` | `resource`, `module`, `output`, `targetScope`, `extension` |

Deployment-only declarations are omitted because a test file is never deployed. The `test` keyword is hidden unless the `testFramework` experimental feature is enabled, so the completion list matches what will actually compile.

`.biceptestparam` files are registered the same way, as the `bicep-testparams` language. Their
top-level completions are scoped to what an input file can declare:

| Offered | Not offered |
|---------|-------------|
| `using` (once), `case` and `deploymentContext` (only when `testFramework` is enabled), `var` | `param`, `test`, `extends`, `resource`, `module`, `output`, `targetScope`, `extension` |

`using` is offered only until one is declared, and `deploymentContext` only until the file declares
its ambient context, because both are file-level singletons. `param` is not offered: an input file
supplies values for the test file's parameters through its cases, and declares none of its own.

`bicep lint` and `bicep format` also accept `.biceptest` files. Both analyse or rewrite the source
only; neither evaluates the tests, and formatting preserves the `.biceptest` extension.

## Assertions

A test can assert about its target in two different ways, and they are deliberately distinct.

### Target-owned assertions

Assertions authored in the Bicep file under test use the `assert` keyword. Each `assert` is evaluated after the test's parameters are applied, so the target must compile *and* be supplied with every parameter it requires.

```bicep
param namePrefix string

var storageAccountName = toLower('${namePrefix}stg')

assert nameIsLowerCase = storageAccountName == toLower(storageAccountName)
assert nameWithinLengthLimit = length(storageAccountName) <= 24
```

### Test-owned assertions

A test may instead bring its own assertions, written in the test file. These ask questions about how
the target is *written* rather than what it evaluates to, so they need no parameter values at all:

```bicep
test moduleSourcePolicy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  assertions: {
    onlyAllowedResourceTypes: {
      failOn: filter(target.resources, r => r.type != 'Microsoft.Storage/storageAccounts')
      message: 'Modules under modules/ may only declare storage accounts.'
    }
    isALeafModule: {
      passWhen: length(target.modules) == 0
      message: 'Modules under modules/ must not compose other modules.'
    }
  }
}
```

Each named assertion supplies exactly one of:

- **`passWhen`** — a boolean condition. The assertion passes only when the condition is true.
- **`failOn`** — a collection of offending facts. The assertion passes when the collection is
  **empty**, and when it is not, every element is reported by source location. `failOn` is about
  emptiness, not truthiness: a non-empty collection is a failure even if its contents are falsy.

`message` explains what to do about a failure and is reported alongside it.

The two forms answer different questions. `passWhen` states a property the target must have;
`failOn` enumerates the specific declarations that broke a rule, which is what makes a policy
failure actionable rather than merely true.

A test declaring a non-empty `assertions` object runs **those** assertions only; the target's own
`assert` statements are not evaluated and its parameters are not required. Omitting `assertions`
preserves the target-owned behaviour above. An `assertions` object that is present but empty is an
error, because it neither asserts anything nor falls back to anything.

### The `target` symbol

Inside `assertions`, `target` is a typed, read-only symbol describing the compiler's view of the
selected file. It is not a global Bicep keyword and exists only in this scope.

| Property | Description |
|----------|-------------|
| `target.resources` | Resources the file declares, including nested ones |
| `target.modules` | Module declarations in the file |
| `target.imports` | Compile-time `import` statements in the file |
| `target.withModules` | The same three collections for the file **plus every module it transitively reaches** |
| `target.evaluated` | What the file would actually produce for this input case, computed offline — see [Evaluated values](#evaluated-values) |

Each resource carries `symbolicName`, `type` (without the API version), `existing`, `file` and `line`.
Each module carries `symbolicName`, `path` (as written), `resolvedFile`, `file` and `line`. Each
import carries `path`, `resolvedFile`, `symbols`, `wildcard`, `file` and `line`.

`symbolicName` is the name the declaration has in Bicep source — `storageAccount` in
`resource storageAccount '…' = { … }` — not the resource's ARM name, which source facts deliberately
do not claim to know.

`file` and `resolvedFile` are relative to the selector root, not the working directory, so a policy
phrased in repository-relative terms means the same thing no matter where the CLI was invoked from.

These are **source facts**. Conditions are not evaluated and loops are not expanded: a resource
declared with `if (...)` or `for (...)` contributes exactly one fact, because the question being
asked is what the file declares, not what a particular deployment would create.

`target` has no additional properties, so a misspelling such as `target.resourcez` is a compile
error rather than a silently empty result.

Assertion expressions are ordinary Bicep, so the usual functions (`filter`, `map`, `contains`,
`length`, `startsWith`, `union`, …) all apply, and they may reference variables declared in the test
file.

An assertion that cannot be evaluated — indexing past the end of a collection, for example — fails
and reports why. It never passes by default, and it does not prevent the assertions beside it from
being judged.

When comparing `file` or `resolvedFile` against a directory, include the trailing separator
(`startsWith(r.file, 'sql/')`), since paths are compared as text: `'sql'` alone would also match
`sqlbackup/`. Paths always use `/` separators and are normalized, so the same policy means the same
thing on every platform.

## Input cases

A test file can declare its own typed inputs as ordinary Bicep parameters. Values for them come from
a `.biceptestparam` file, which binds to exactly one test file and declares one or more named cases.

```bicep
// storage-cases.biceptest
@description('Prefix the target builds its storage account name from.')
param namePrefix string

@description('Location the deployment targets.')
param location string = 'eastus'

@description('Largest number of resources a single target may declare.')
param maxResources int = 1

// Inputs are mapped explicitly into the target's parameters. Nothing is forwarded implicitly.
test namingRules 'storage.bicep' = {
  params: {
    namePrefix: namePrefix
    location: location
  }
}

// Inputs can also parameterize a source policy, which needs no deployment values at all.
test sizePolicy = {
  match: {
    include: ['storage.bicep']
  }
  assertions: {
    boundedResourceCount: {
      passWhen: length(target.resources) <= maxResources
      message: 'A target may declare at most ${maxResources} resources.'
    }
  }
}
```

```bicep
// storage-cases.biceptestparam
using 'storage-cases.biceptest'

case shortPrefix = {
  namePrefix: 'contoso'
  location: 'eastus'
}

case prefixAtLengthLimit = {
  namePrefix: 'abcdefghijklmnopqrstu'
  location: 'westus2'
  maxResources: 1
}
```

The test file's parameters are a real, checked contract. A case that sets a property the test does
not declare, omits one it requires, or supplies the wrong type is a compile error in the input file,
reported against the case that caused it — not a silently ignored value.

A test file's inputs are its own. It decides which of them reach a target's production `params`, and
which only shape its own policy. There is no implicit forwarding: a value a target never receives
cannot influence what it computes.

### Running with `--inputs`

```console
$ bicep test storage-cases.biceptest --inputs storage-cases.biceptestparam
[✓] Evaluation namingRules (storage.bicep) [storage-cases.biceptestparam: shortPrefix] Passed!
[✓] Evaluation namingRules (storage.bicep) [storage-cases.biceptestparam: prefixAtLengthLimit] Passed!
[✓] Evaluation sizePolicy (storage.bicep) [storage-cases.biceptestparam: shortPrefix] Passed!
[✓] Evaluation sizePolicy (storage.bicep) [storage-cases.biceptestparam: prefixAtLengthLimit] Passed!
All 4 evaluations passed!
```

`--inputs` may be given more than once to combine several input files into one run.

### Cases and targets multiply

Every case applies to the complete set of targets a test selected: a test covering M targets, run
with N cases, produces M × N evaluations. Cases supply values; they can never change which targets a
test applies to. A different set of targets is a different test file.

Each evaluation is judged on its own, and its identity names the case it ran with, so two runs of the
same test and target that differ only in their values are never reported as the same thing:

```console
$ bicep test storage-cases.biceptest --inputs storage-cases-failing.biceptestparam
[✗] Evaluation namingRules (storage.bicep) [storage-cases-failing.biceptestparam: prefixTooLong] Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
[✓] Evaluation namingRules (storage.bicep) [storage-cases-failing.biceptestparam: withinLimits] Passed!
[✗] Evaluation sizePolicy (storage.bicep) [storage-cases-failing.biceptestparam: prefixTooLong] Failed at 1 / 1 assertions!
	[✗] Assertion boundedResourceCount failed!
		A target may declare at most 0 resources.
[✓] Evaluation sizePolicy (storage.bicep) [storage-cases-failing.biceptestparam: withinLimits] Passed!
Evaluation Summary: Failure!
Total: 4 - Success: 2 - Skipped: 0 - Failed: 2
```

An assertion's `message` is ordinary Bicep and may interpolate the values the assertion actually ran
with, so a threshold stated in the message cannot drift away from the condition that enforced it.

### Failures are attributed, not fatal

An input file that cannot contribute cases — because it fails to compile, or because it binds to a
different test file — is reported and skipped. Everything else still runs, and the aggregate exit
code is non-zero:

```console
$ bicep test storage.biceptest --inputs storage-cases.biceptestparam
storage-cases.biceptestparam: The input file supplies cases for "storage-cases.biceptest", not the test file being run.
[✓] Evaluation validPrefix (storage.bicep) Passed!
[✓] Evaluation prefixAtLengthLimit (storage.bicep) Passed!
```

The same applies within a run. An input with no value from any case and no declared default fails
only the evaluations that actually reach it:

```console
$ bicep test storage-cases.biceptest
[-] Evaluation namingRules (storage.bicep) Skipped!
Reason: The input "namePrefix" has no value. Supply it from a test case or give it a default.
[✓] Evaluation sizePolicy (storage.bicep) Passed!
Evaluation Summary: Failure!
Total: 2 - Success: 1 - Skipped: 1 - Failed: 0
```
`sizePolicy` still ran because it never reaches `namePrefix`; only the test that needed the missing
value was affected.

### Deployment context

Templates frequently read ambient deployment information rather than parameters — `resourceGroup().location`,
`subscription().subscriptionId` and so on. The input file supplies that context with a file-level
`deploymentContext` assignment:

[`context.biceptestparam`](./examples/test-framework/context.biceptestparam)

```bicep
using 'context.biceptest'

deploymentContext = {
  subscriptionId: '00000000-0000-0000-0000-000000000001'
  resourceGroup: 'contoso-prod-rg'
  resourceGroupLocation: 'eastus'
}

// Inherits the file defaults unchanged.
case primaryRegion = {}

// Replaces one property. Everything else is still inherited.
@resourceGroupLocation('westus2')
case secondaryRegion = {}

@resourceGroupLocation('northeurope')
case unapprovedRegion = {}
```

The available properties are `tenantId`, `managementGroup`, `subscriptionId`, `resourceGroup`,
`resourceGroupLocation` and `deploymentName`. Each has a matching decorator that a single case may
apply.

A decorator **replaces one property** of the file defaults for that case only. There is no deep
merge, a case cannot replace the context wholesale, and the case that overrides a property does not
change what the next case inherits. Applying the same decorator twice to one case is an error:

```
Error BCP166: Duplicate "resourceGroupLocation" decorator.
```

The target in [`context.bicep`](./examples/test-framework/context.bicep) defaults its `location`
parameter to `resourceGroup().location` and asserts that the result is an approved region, so each
case exercises a different region without the test declaring a single input:

```console
$ bicep test context.biceptest --inputs context.biceptestparam
[✓] Evaluation regionPolicy (context.bicep) [context.biceptestparam: primaryRegion] Passed!
[✓] Evaluation regionPolicy (context.bicep) [context.biceptestparam: secondaryRegion] Passed!
[✗] Evaluation regionPolicy (context.bicep) [context.biceptestparam: unapprovedRegion] Failed at 1 / 1 assertions!
	[✗] Assertion locationIsApproved failed!
Evaluation Summary: Failure!
Total: 3 - Success: 2 - Skipped: 0 - Failed: 1
```

Two properties of this are worth stating plainly:

- **Context is simulated, not deployed.** Setting a resource group location does not create anything,
  does not contact Azure and does not validate that the region exists. It only decides what the
  offline evaluator reports for the corresponding deployment function.
- **Context is not a parameter source.** A production parameter named `resourceGroupLocation` is not
  assigned by the context property of the same name. Parameters come only from the test's `params`
  mapping, so a target with an unsatisfied required parameter is still skipped:

  ```console
  Reason: Evaluating template failed: The value for the template parameter 'resourceGroupLocation' at line '16' and column '30' is not provided.
  ```

A test run with no input file at all keeps the evaluator's own placeholder context rather than one
invented by the runner.

### Deployment names

`deployment().name` is commonly used to derive module names so that concurrent deployments of the
same template do not collide. Supplying `deploymentName` makes that name deterministic for a case:

[`deployment-name.biceptestparam`](./examples/test-framework/deployment-name.biceptestparam)

```bicep
using 'deployment-name.biceptest'

deploymentContext = {
  deploymentName: 'contoso-2024-06-01'
}

case release = {}
```

Only the **root** deployment takes its name from the context. A module's name is whatever its own
declaration computed, which is what makes name-derivation chains observable:

[`deployment-name.bicep`](./examples/test-framework/deployment-name.bicep)

```bicep
module primary 'deployment-name/stamp.bicep' = {
  name: '${deployment().name}-primary'
  params: {
    role: 'primary'
  }
}
```

[`deployment-name/stamp.bicep`](./examples/test-framework/deployment-name/stamp.bicep) reads
`deployment().name` again, and reports the name of *its own* deployment:

[`deployment-name.biceptest`](./examples/test-framework/deployment-name.biceptest)

```bicep
test deploymentNames 'deployment-name.bicep' = {
  params: {}
  assertions: {
    rootUsesTheSuppliedName: {
      passWhen: target.evaluated.outputs.rootName == 'contoso-2024-06-01'
      message: 'The root deployment should use the name the case supplied.'
    }
    modulesUseTheirOwnNames: {
      passWhen: target.evaluated.outputs.primaryStamp == 'contoso-2024-06-01-primary/primary' && target.evaluated.outputs.secondaryStamp == 'contoso-2024-06-01-secondary/secondary'
      message: 'Each module should see the deployment name its own declaration computed.'
    }
  }
}
```

```console
$ bicep test deployment-name.biceptest --inputs deployment-name.biceptestparam
[✓] Evaluation deploymentNames (deployment-name.bicep) [deployment-name.biceptestparam: release] Passed!
All 1 evaluations passed!
```

There is no implicit default. A target that reads `deployment()` without a name being supplied is
not given an invented one:

```console
Reason: deployment() was evaluated but no deployment name was supplied. Set 'deploymentName' in the input file's deploymentContext, or override it for this case with @deploymentName().
```

## Evaluated values

`target.evaluated` describes what the selected file would **actually deploy** for the case being run.

`evaluated` means computed offline, for this case and this deployment context. Nothing is deployed,
nothing is queried from Azure and no provider-returned state is involved. It is the compiler and the
offline evaluator working out the consequences of the source and the values supplied to it.

| Property | Description |
|----------|-------------|
| `target.evaluated.resources` | Resource instances the selected file would deploy |
| `target.evaluated.outputs` | Evaluated values of the outputs the selected file declares, by name |
| `target.evaluated.withModules.resources` | The same instances for the selected file **plus every local module reachable from it** |

Each instance carries:

| Fact | Description |
|------|-------------|
| `name` | The resolved ARM name, including parent segments for child resources |
| `type` | The resource type without its API version |
| `symbolicName` | The symbolic name of the declaration this instance came from |
| `instanceId` | Distinguishes instances of the same declaration, including module call and loop indices |
| `file`, `line` | The declaration that produced the instance |

### Source facts and evaluated instances are different questions

`target.resources` answers *what does this file declare*. `target.evaluated.resources` answers *what
would this case deploy*. The two differ wherever the source is conditional:

- A `for` loop is **one** source declaration and **one instance per iteration**.
- A resource whose `if (...)` condition evaluates to `false` is still a source declaration, but it is
  **not** an evaluated instance.
- An `existing` reference is a source declaration but never an evaluated instance: it reads state
  something else owns.
- A module is **deduplicated in source** — the same file contributes its declarations once, however
  many times it is called — but **each call is its own evaluated instance**, evaluated with the
  arguments that call actually passed.

`fleet.bicep` exercises all four. It declares one looped storage account and one conditional one, and
calls `fleet/regionStamp.bicep` twice:

[fleet.bicep](examples/test-framework/fleet.bicep)

[fleet/regionStamp.bicep](examples/test-framework/fleet/regionStamp.bicep)

[fleet.biceptest](examples/test-framework/fleet.biceptest) asserts over both views, and
[fleet.biceptestparam](examples/test-framework/fleet.biceptestparam) supplies two cases: two regions
without the backup account, and three regions with it.

```bicep
// Source: two declarations, whatever the case supplies.
declaresTwoAccounts: {
  passWhen: length(target.resources) == 2
  message: 'The target declares the loop and the conditional account regardless of inputs.'
}

// Evaluated: the loop is expanded and the condition is applied.
deploysOneAccountPerRegion: {
  passWhen: length(target.evaluated.resources) == length(regions) + (enableBackup ? 1 : 0)
  message: 'Expected one data account per region, plus the backup account only when enabled.'
}
```

```console
$ bicep test fleet.biceptest --inputs fleet.biceptestparam
[✓] Evaluation fleetShape (fleet.bicep) [fleet.biceptestparam: twoRegionsNoBackup] Passed!
[✓] Evaluation fleetShape (fleet.bicep) [fleet.biceptestparam: threeRegionsWithBackup] Passed!
All 2 evaluations passed!
```

The same assertions describe a two-region deployment without a backup account in the first case and a
four-resource one in the second, because `target.evaluated` is recomputed for each case.

### Module outputs are computed too

A module's outputs are evaluated offline and flow back to the caller, so an output that reads
`primaryStamp.outputs.siteName` resolves:

```bicep
primarySiteIsNamedForItsRole: {
  passWhen: endsWith(target.evaluated.outputs.primarySiteName, '-primary-site')
  message: 'The primary stamp must expose the primary site name.'
}
```

`target.evaluated.outputs` accepts any output name, because one test may cover many targets and the
declared outputs are only known once a target is bound. An output the target does not declare
evaluates to null rather than failing to compile.

### Violations name the instance and the declaration

Because every instance is attributed, a `failOn` over evaluated instances reports the name the
deployment would really use alongside the declaration that produced it — including declarations
inside a module. [fleet-failing.biceptest](examples/test-framework/fleet-failing.biceptest) rejects
anything that is not storage:

```bicep
onlyStorageIsDeployed: {
  failOn: filter(target.evaluated.withModules.resources, r => !startsWith(r.type, 'Microsoft.Storage/'))
  message: 'This fleet is only allowed to deploy storage accounts.'
}
```

```console
$ bicep test fleet-failing.biceptest
[✗] Evaluation storageOnly (fleet.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion onlyStorageIsDeployed failed!
		This fleet is only allowed to deploy storage accounts.
		fleet/regionStamp.bicep(16): fleetdev-primary-plan
		fleet/regionStamp.bicep(24): fleetdev-primary-site
		fleet/regionStamp.bicep(16): fleetdev-secondary-plan
		fleet/regionStamp.bicep(24): fleetdev-secondary-site
Evaluation Summary: Failure!
Total: 1 - Success: 0 - Skipped: 0 - Failed: 1
```

Two calls to the same module produce four findings, not two: the module file declares the same two
resources once, but the deployment creates them twice under different names.

### Evaluation happens only when it is asked for

Evaluating a target needs values for its parameters; reading source facts does not. A source policy
that never mentions `target.evaluated` is therefore never held up by a target it could not evaluate,
and `target.evaluated.withModules` is only computed when an assertion actually reads it — a policy
about the selected file alone is not failed by a module it did not ask about.

An unevaluatable condition is an error rather than a silent exclusion. Reporting "not deployed" for a
condition that could not be computed would let a policy pass by describing a smaller deployment than
the case actually produces.

## Mocks

Some values a deployment uses do not come from its own source. An `existing` resource is read from
Azure, and `listKeys` is answered by a resource provider. Offline, nothing can supply them, so the
test supplies them itself.

Mocks are declared in the `.biceptest` file with a top-level `mocks` assignment. They belong to the
test, not to the target: the target stays an ordinary deployment with nothing test-specific in it.

```bicep
param identityName string

var scope = '/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/contoso-prod-rg'

mocks = {
  deployIdentity: {
    operation: 'reference'
    resourceId: '${scope}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/${identityName}'
    apiVersion: '2023-01-31'
    response: {
      properties: {
        principalId: '11111111-1111-1111-1111-111111111111'
      }
    }
  }
}
```

Each entry is named, and the name is what a failure reports. The properties are:

| Property | Required | Meaning |
|----------|----------|---------|
| `operation` | Yes | `'reference'` or `'listKeys'` |
| `resourceId` | Yes | The full resource ID the request addresses |
| `apiVersion` | Yes | The API version the request uses |
| `requestBody` | No | The body the request must carry; absent means the request must carry none |
| `response` | No | The answer to give |

A mock body is ordinary Bicep, so it can be built from the test's own parameters and read per input
case. Nothing here reaches the network: every value is stated by the test.

### Matching is exact

A request is answered only by a mock that states the same operation, resource ID, API version and
request body. Resource IDs are compared without regard to case or a trailing separator; nothing else
is relaxed.

There is no wildcard. A pattern would answer calls the author never meant to make, and a test that
passes because of an unintended answer is worse than one that fails. For the same reason two entries
that answer the same request are rejected before anything runs, rather than resolved by order:

```console
$ bicep test dup.biceptest
[-] Evaluation dup (dup.bicep) Skipped!
Reason: Mocks "first", "second" answer the same request. Remove the duplicates so the request has one answer.
Evaluation Summary: Failure!
Total: 1 - Success: 0 - Skipped: 1 - Failed: 0
```

A target compiled with symbolic names spells a runtime read as the declaration it came from rather
than as a resource ID. That is a codegen detail, so it is translated back before matching: a mock is
written against the resource ID either way.

### One response answers both views of a resource

Reading `identity.properties.principalId` asks Azure for the resource's properties. Reading
`identity.location` asks for the whole resource, because `location` sits beside `properties` rather
than inside it. Both are the same request, so both are answered by the same entry: write the response
as the resource envelope, and the properties view is taken from within it.

```bicep
// docs/experimental/examples/test-framework/mocks.biceptest
response: {
  location: 'eastus'
  properties: {
    principalId: '11111111-1111-1111-1111-111111111111'
    clientId: '22222222-2222-2222-2222-222222222222'
  }
}
```

```console
$ bicep test mocks.biceptest --inputs mocks.biceptestparam
[✓] Evaluation runtimeReads (mocks.bicep) [mocks.biceptestparam: eastus] Passed!
All 1 evaluations passed!
```

Neither view eagerly reads a field the response does not have, so an envelope only needs to carry
what the target actually reads.

### Unconfigured values are unset, not invented

A response states only what the test cares about. A field it does not mention is simply absent, and
absence is only a problem where something reads it:

```bicep
// docs/experimental/examples/test-framework/mocks-failing.biceptest
response: {
  properties: {
    principalId: '11111111-1111-1111-1111-111111111111'
  }
}
```

```console
$ bicep test mocks-failing.biceptest --inputs mocks-failing.biceptestparam
[✗] Evaluation unconfiguredField (mocks.bicep) [mocks-failing.biceptestparam: eastus] Failed at 1 / 1 assertions!
	[✗] Assertion clientIdIsResolved failed!
		The deployment reads a client ID that no mock answers.
		Could not be evaluated: The language expression property 'clientId' doesn't exist, available properties are 'principalId'.
```

A request that no mock answers at all is reported the same way, naming the request so it can be
configured:

```console
	Could not be evaluated: No mock answers reference on /subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/contoso-prod-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/contoso-deploy-identity (2023-01-31). Declare it in the test file's 'mocks' so the value the deployment reads is stated by the test.
```

Neither case falls back to a guessed value, and neither reaches Azure. A test that cannot get a value
it needs fails; it does not quietly describe a deployment that was never computed.

Because `target.evaluated.outputs` is computed as a set, a target whose outputs read an unanswered
value cannot report any of them for that case. Other tests and other cases still run, and the failure
is attributed to the one that hit it.

Absence only matters where a value is actually read, so the same three rules follow:

- A mock that nothing requests is not an error. A test can describe more of the world than a
  particular target happens to read.
- A field on a branch the deployment does not take is never read, so it is never required. An
  unevaluated branch does not turn into an empty or null result either; the branch that was taken is
  the one that produces the value.
- A resource the target only takes the ID of needs no mock at all. Resource IDs are computed from
  source, not read from Azure.

Failures name the request and the property path that could not be satisfied. They never echo the
response, the request body or the parameters, because a synthetic value can read like a real secret
and a test result travels further than the test does.

### A worked mock

```bicep
// docs/experimental/examples/test-framework/mocks.bicep
resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

resource artifacts 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: artifactStorageName
}

output deployPrincipalId string = deployIdentity.properties.principalId

#disable-next-line outputs-should-not-contain-secrets
output artifactKeyName string = artifacts.listKeys().keys[0].keyName
```

```console
$ bicep test mocks.biceptest --inputs mocks.biceptestparam
[✓] Evaluation runtimeReads (mocks.bicep) [mocks.biceptestparam: eastus] Passed!
All 1 evaluations passed!
```

A `reference` answer is a resource envelope: an ordinary `reference` reads its `properties`, and
`reference(..., 'Full')` reads the envelope itself. A `listKeys` answer is the operation's own result
and is used exactly as written.

### Composing modules with mocked values

A mocked value does not stop at the module that read it. It flows into that module's outputs, into
the arguments its caller passes on, and into whatever those arguments derive.

`rbac.bicep` creates a workload identity in one module and grants it Key Vault access in another.
Azure assigns the principal ID, so the test states it; everything else — the role assignment's name,
which is derived from the vault, the principal and the role — is computed from source.

```bicep
// docs/experimental/examples/test-framework/rbac.bicep
module identity 'rbac/workloadIdentity.bicep' = {
  name: '${workloadName}-identity'
  params: {
    identityName: '${workloadName}-id'
    location: location
  }
}

module vaultAccess 'rbac/vaultSecretsAccess.bicep' = {
  name: '${workloadName}-vault-access'
  params: {
    vaultName: vaultName
    principalId: identity.outputs.principalId
  }
}
```

```bicep
// docs/experimental/examples/test-framework/rbac.biceptest
grantIsDerivedFromWhatItGrants: {
  passWhen: target.evaluated.outputs.grantName == guid(vaultId, principalId, secretsUserRoleId)
  message: 'The role assignment name should be derived from the vault, the principal and the role.'
}
```

```console
$ bicep test rbac.biceptest --inputs rbac.biceptestparam
[✓] Evaluation vaultAccessIsGranted (rbac.bicep) [rbac.biceptestparam: contoso] Passed!
All 1 evaluations passed!
```

An argument taken from another module's output is only knowable once that module has been evaluated,
and the module it is passed to cannot be evaluated until then. Evaluation therefore repeats until the
module outputs stop changing, resolving one more link of the chain each round.

That is what makes a miswiring visible. `rbac-miswired.bicep` passes the identity's resource ID where
a principal ID belongs. Both are strings, so it compiles and deploys a role assignment either way —
but not the one the policy asked for:

```console
$ bicep test rbac-failing.biceptest --inputs rbac-failing.biceptestparam
[✗] Evaluation vaultAccessIsGranted (rbac-miswired.bicep) [rbac-failing.biceptestparam: contoso] Failed at 1 / 1 assertions!
	[✗] Assertion grantIsDerivedFromWhatItGrants failed!
		The role assignment should grant the identity this deployment created.
Evaluation Summary: Failure!
Total: 1 - Success: 0 - Skipped: 0 - Failed: 1
```

This says nothing about whether the grant would be accepted by Azure. It says what the deployment
computes, which is the part the source is responsible for.

## Running tests

```console
bicep test <path-to-test-file>
```

The command accepts either a `.bicep` or a `.biceptest` file.

### Running many test files with `--pattern`

`--pattern` runs every test file matching a glob, relative to the current directory:

```console
bicep test --pattern "**/*.biceptest"
```

Files are processed in a stable, sorted order, and a single summary covers the whole run. When more
than one file is involved each result names its test file, because test names are only unique within
a file.

A pattern that matches nothing is an error. An empty run is never reported as a run that passed.

`--pattern` and a named input file are alternatives; supply one or the other.

### Listing what would run with `--list`

`--list` reports the inventory of a test file — which tests it declares and which targets each one
resolves to — without restoring, compiling or evaluating any target:

```console
bicep test naming.biceptest --list
```

This answers "what would run". It deliberately says nothing about whether those targets compile or
pass. `--list` combines with `--pattern` to inventory a whole suite.

A test that resolves to no targets is reported on stderr and makes `--list` exit non-zero, so a
selector that has silently stopped matching cannot hide behind an empty list.

### Machine-readable output with `--output-format json`

`--output-format json` writes a versioned document to stdout and keeps every piece of progress text,
diagnostic and warning on stderr. A host process can therefore parse stdout whether the run
succeeded or failed:

```console
$ bicep test storage-failing.biceptest --output-format json
{
  "version": "1.0",
  "mode": "run",
  "cases": [
    {
      "caseId": "storage-failing.biceptest#prefixTooLong#storage.bicep",
      "testFile": "storage-failing.biceptest",
      "testName": "prefixTooLong",
      "target": "storage.bicep",
      "inputFile": null,
      "inputCase": null,
      "status": "failed",
      "error": null,
      "assertions": {
        "total": 2,
        "failed": 1,
        "failedNames": [
          "nameWithinLengthLimit"
        ],
        "failures": [
          {
            "name": "nameWithinLengthLimit",
            "message": null,
            "error": null,
            "violations": []
          }
        ]
      }
    }
  ],
  "summary": {
    "total": 1,
    "passed": 0,
    "failed": 1,
    "skipped": 0
  }
}
```

Test-owned assertions populate the same `failures` array with their message and the source locations
that violated them, so a host does not have to parse console text to act on a policy failure:

```console
$ bicep test source-policy-failing.biceptest --output-format json
{
  "version": "1.0",
  "mode": "run",
  "cases": [
    {
      "caseId": "source-policy-failing.biceptest#forbidStorageAccounts#modules/blobStorage.bicep",
      "testFile": "source-policy-failing.biceptest",
      "testName": "forbidStorageAccounts",
      "target": "modules/blobStorage.bicep",
      "inputFile": null,
      "inputCase": null,
      "status": "failed",
      "error": null,
      "assertions": {
        "total": 1,
        "failed": 1,
        "failedNames": [
          "noStorageAccounts"
        ],
        "failures": [
          {
            "name": "noStorageAccounts",
            "message": "Storage accounts must be created by the platform team, not by service modules.",
            "error": null,
            "violations": [
              "blobStorage.bicep(12): storageAccount"
            ]
          }
        ]
      }
    }
  ],
  "summary": {
    "total": 2,
    "passed": 0,
    "failed": 2,
    "skipped": 0
  }
}
```

(The second case is elided above for brevity; the real document contains one case per target.)

The same option applies to `--list`, where `mode` is `list` and each case has a status of `listed`
or `unresolved`:

```console
$ bicep test naming.biceptest --list --output-format json
{
  "version": "1.0",
  "mode": "list",
  "cases": [
    {
      "caseId": "naming.biceptest#namingPolicy#modules/blobStorage.bicep",
      "testFile": "naming.biceptest",
      "testName": "namingPolicy",
      "target": "modules/blobStorage.bicep",
      "status": "listed",
      "error": null
    },
    {
      "caseId": "naming.biceptest#namingPolicy#modules/fileStorage.bicep",
      "testFile": "naming.biceptest",
      "testName": "namingPolicy",
      "target": "modules/fileStorage.bicep",
      "status": "listed",
      "error": null
    }
  ],
  "summary": {
    "total": 2,
    "listed": 2,
    "unresolved": 0
  }
}
```

Notes on the contract:

- `version` changes only when the shape changes in a way a consumer must react to. New optional
  properties may be added without a version bump.
- `caseId` is built from test-file-relative information only, so the same case has the same identity
  regardless of the directory the CLI was invoked from.
- `target` is `null` when a test could not be resolved to any target; there is no target to name.
- `inputFile` and `inputCase` name the case a run used. Both are `null` when no input file was
  supplied, so a host that never passes `--inputs` sees exactly the document it saw before.
- `assertions` is present only for cases that were actually evaluated. A skipped case never reached
  its assertions, and reporting zero counts would be indistinguishable from a target that declares
  none.
- `failures` describes each failed assertion. `violations` holds selector-relative source locations
  drawn from the target's own declarations; it is empty for target-owned `assert` statements, which
  have no offending-fact collection.
- The document carries identities and outcomes only. Parameter values, template content and other
  payloads are never included.

## Worked example

The complete example lives in [`docs/experimental/examples/test-framework`](./examples/test-framework). It contains:

| File | Purpose |
|------|---------|
| `bicepconfig.json` | Enables the `testFramework` and `assertions` features |
| `storage.bicep` | The template under test, including two `assert` statements |
| `storage.biceptest` | Two passing tests |
| `storage-failing.biceptest` | A test that intentionally violates an assertion |
| `modules/blobStorage.bicep`, `modules/fileStorage.bicep` | Two modules sharing a naming policy |
| `modules/_naming.bicep` | A helper that is not a deployable module, excluded by the selector |
| `naming.biceptest` | One test applied to every module via `match` |
| `app.bicep` | A composition entrypoint that declares no resources of its own |
| `source-policy.biceptest` | Test-owned source policies, including a `withModules` query |
| `source-policy-failing.biceptest` | A source policy that is violated on purpose |
| `storage-cases.biceptest` | A test that declares typed inputs and maps them into the target |
| `storage-cases.biceptestparam` | Two passing input cases for that test |
| `storage-cases-failing.biceptestparam` | Input cases where one violates a rule and one does not |
| `context.bicep` | A template whose behavior depends on ambient deployment context |
| `context.biceptest` | A test that declares no inputs at all |
| `context.biceptestparam` | File-level `deploymentContext` defaults and per-case overrides |
| `fleet.bicep` | A target with a loop, a condition and two calls to the same module |
| `fleet/regionStamp.bicep` | The module `fleet.bicep` calls twice |
| `fleet.biceptest` | Source facts and evaluated instances asserted side by side |
| `fleet.biceptestparam` | Two cases that deploy different shapes from the same source |
| `fleet-failing.biceptest` | An evaluated-instance policy that is violated on purpose |
| `deployment-name.bicep` | A target whose module names derive from `deployment().name` |
| `deployment-name/stamp.bicep` | A module that reports the deployment name it was given |
| `deployment-name.biceptest` | Asserts the root and module deployment names |
| `deployment-name.biceptestparam` | Supplies a deterministic `deploymentName` |
| `mocks.bicep` | A target that reads an existing identity and a storage account's keys |
| `mocks.biceptest` | Test-owned `reference` and `listKeys` mocks |
| `mocks.biceptestparam` | One case supplying the names the mocks are built from |
| `mocks-failing.biceptest` | A response that omits a field the target reads |
| `mocks-failing.biceptestparam` | The same case, bound to the failing test file |
| `rbac.bicep` | A composition whose second module is wired from the first module's output |
| `rbac/workloadIdentity.bicep` | Creates an identity whose principal ID Azure assigns |
| `rbac/vaultSecretsAccess.bicep` | Grants a principal vault access, naming the grant after what it grants |
| `rbac.biceptest` | Mocks only the principal ID and asserts everything derived from it |
| `rbac.biceptestparam` | One case supplying the workload and vault names |
| `rbac-miswired.bicep` | The same composition with the identity's resource ID passed as a principal ID |
| `rbac-failing.biceptest` | The same policy, catching the miswiring |
| `rbac-failing.biceptestparam` | The same case, bound to the miswired target |

Running the passing tests:

```console
$ bicep test storage.biceptest
WARNING: The following experimental Bicep features have been enabled: TestFramework. Experimental features should be enabled for testing purposes only, as there are no guarantees about the quality or stability of these features. Do not enable these settings for any production usage, or your production environment may be subject to breaking.
[✓] Evaluation validPrefix (storage.bicep) Passed!
[✓] Evaluation prefixAtLengthLimit (storage.bicep) Passed!
All 2 evaluations passed!
```

The command exits with code `0`. Each result names both the test and the target it was evaluated against, because one test may cover several targets.

Running the failing test shows which assertion failed:

```console
$ bicep test storage-failing.biceptest
[✗] Evaluation prefixTooLong (storage.bicep) Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
Evaluation Summary: Failure!
Total: 1 - Success: 0 - Skipped: 0 - Failed: 1
```

The command exits with code `1`. In this example `namePrefix` is long enough that the generated storage account name exceeds the 24 character limit asserted by `storage.bicep`.

Running the selector-based test applies one declaration to both modules, while skipping the excluded helper:

```console
$ bicep test naming.biceptest
[✓] Evaluation namingPolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation namingPolicy (modules/fileStorage.bicep) Passed!
All 2 evaluations passed!
```

Adding another module to `modules/` puts it under the same policy automatically.

Source policies assert about the targets without supplying any parameters, because nothing is
evaluated:

```console
$ bicep test source-policy.biceptest
[✓] Evaluation moduleSourcePolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation moduleSourcePolicy (modules/fileStorage.bicep) Passed!
[✓] Evaluation compositionPolicy (app.bicep) Passed!
All 3 evaluations passed!
```

When a source policy is violated, the failure names the declarations responsible:

```console
$ bicep test source-policy-failing.biceptest
[✗] Evaluation forbidStorageAccounts (modules/blobStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		blobStorage.bicep(12): storageAccount
[✗] Evaluation forbidStorageAccounts (modules/fileStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		fileStorage.bicep(12): storageAccount
Evaluation Summary: Failure!
Total: 2 - Success: 0 - Skipped: 0 - Failed: 2
```

The locations are relative to the selector root (`modules`), which is the frame of reference the
policy was written in.

Listing the same test file reports the targets without evaluating them:

```console
$ bicep test naming.biceptest --list
naming.biceptest: namingPolicy -> modules/blobStorage.bicep
naming.biceptest: namingPolicy -> modules/fileStorage.bicep
```

Running the whole example folder with a pattern gathers every test file into one run:

```console
$ bicep test --pattern "*.biceptest"
[✓] Evaluation naming.biceptest: namingPolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation naming.biceptest: namingPolicy (modules/fileStorage.bicep) Passed!
[✗] Evaluation source-policy-failing.biceptest: forbidStorageAccounts (modules/blobStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		blobStorage.bicep(12): storageAccount
[✗] Evaluation source-policy-failing.biceptest: forbidStorageAccounts (modules/fileStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		fileStorage.bicep(12): storageAccount
[✓] Evaluation source-policy.biceptest: moduleSourcePolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation source-policy.biceptest: moduleSourcePolicy (modules/fileStorage.bicep) Passed!
[✓] Evaluation source-policy.biceptest: compositionPolicy (app.bicep) Passed!
[-] Evaluation storage-cases.biceptest: namingRules (storage.bicep) Skipped!
Reason: The input "namePrefix" has no value. Supply it from a test case or give it a default.
[✓] Evaluation storage-cases.biceptest: sizePolicy (storage.bicep) Passed!
[✗] Evaluation storage-failing.biceptest: prefixTooLong (storage.bicep) Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
[✓] Evaluation storage.biceptest: validPrefix (storage.bicep) Passed!
[✓] Evaluation storage.biceptest: prefixAtLengthLimit (storage.bicep) Passed!
Evaluation Summary: Failure!
Total: 12 - Success: 8 - Skipped: 1 - Failed: 3
```

The command exits with code `1` because `storage-failing.biceptest` and
`source-policy-failing.biceptest` are expected to fail, and because `storage-cases.biceptest` was run
without the `--inputs` file its `namingRules` test needs. The failure of one file does not stop the
others from running, and one summary reports the aggregate.

Supplying a file that is neither `.bicep` nor `.biceptest` is rejected:

```console
$ bicep test bicepconfig.json
The specified input "...\bicepconfig.json" was not recognized as a Bicep or Bicep test file. Valid files must use either the .bicep or .biceptest extension.
```

## Current limitations

- Evaluated values cover resource instances and outputs. Individual resource properties are not yet exposed.
- Module-to-module argument flow is resolved by repeated evaluation, up to a bounded number of rounds. A longer chain than that is left unresolved.
- Mocks answer exact `reference` and `listKeys` requests. There is no conditional, sequenced or counted setup, and mocks cannot be declared in an input file.
- `target.evaluated.outputs` is computed as a set, so a target with any unanswered runtime read reports none of its outputs for that case.
- Tests evaluate templates offline. They do not deploy resources, call Azure, or validate authorization.

For background and ongoing discussion, see [Bicep Experimental Test Framework](https://github.com/Azure/bicep/issues/11967).
