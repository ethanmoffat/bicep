// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// Composes the two modules: the identity module creates the principal, and the access module grants
// it vault access. The grant's arguments come from the first module's output, so the second module
// cannot be evaluated until the first one has been.

param workloadName string
param location string
param vaultName string

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

output grantedPrincipalId string = identity.outputs.principalId

output grantName string = vaultAccess.outputs.grantName
