// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// One regional stamp. `fleet.bicep` calls this module twice with different arguments, which is what
// makes the difference between source declarations and evaluated instances visible.

@description('Prefix used to build the stamp resource names.')
param prefix string

@description('Region this stamp is deployed to.')
param region string

@description('Role this stamp plays in the fleet.')
param role string

resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: toLower('${prefix}-${role}-plan')
  location: region
  sku: {
    name: 'P1v3'
  }
}

resource site 'Microsoft.Web/sites@2022-09-01' = {
  name: toLower('${prefix}-${role}-site')
  location: region
  properties: {
    serverFarmId: plan.id
  }
}

output siteName string = site.name
