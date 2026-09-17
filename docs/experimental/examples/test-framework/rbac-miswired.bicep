// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// The same composition with one wire crossed: the access module is given the identity's resource ID
// where it expects a principal ID. Both are strings, so nothing about this fails to compile.

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
    principalId: identity.outputs.identityId
  }
}

output grantedPrincipalId string = identity.outputs.principalId

output grantName string = vaultAccess.outputs.grantName
