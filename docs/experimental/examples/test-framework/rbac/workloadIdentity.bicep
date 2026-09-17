// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// Deploys a workload identity. Its principal ID is assigned by Azure, so it is a runtime read even
// though this template creates the resource.

param identityName string
param location string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

output principalId string = identity.properties.principalId

output identityId string = identity.id
