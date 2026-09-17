// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// Grants a principal the Key Vault Secrets User role on an existing vault. The role assignment name
// is derived from what it grants, so the same grant is always the same assignment.

param vaultName string
param principalId string

@description('Key Vault Secrets User.')
var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

resource grant 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, principalId, secretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output grantName string = grant.name
