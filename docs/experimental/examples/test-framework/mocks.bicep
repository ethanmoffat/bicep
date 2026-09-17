// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A deployment that reads values it does not create. Both reads are runtime reads: the identity is
// provisioned elsewhere, and the storage keys only exist once the account does.

@description('Name of the pre-provisioned deployment identity.')
param identityName string

@description('Name of the storage account holding release artifacts.')
param artifactStorageName string

resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

resource artifacts 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: artifactStorageName
}

output deployPrincipalId string = deployIdentity.properties.principalId

output deployClientId string = deployIdentity.properties.clientId

// Reading a field outside 'properties' asks Azure for the whole resource rather than its properties,
// so one response has to cover both views.
output deployIdentityLocation string = deployIdentity.location

// The key itself is a secret, so the deployment publishes which key it selected rather than its value.
#disable-next-line outputs-should-not-contain-secrets
output artifactKeyName string = artifacts.listKeys().keys[0].keyName
